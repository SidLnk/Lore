using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Lore.Api.Data;
using Lore.Contracts;
using MetadataExtractor;
using MetadataExtractor.Formats.Exif;

namespace Lore.Api.Media;

/// <summary>
/// Runs on a verified upload before it becomes a MediaAsset.
/// Later features (faces, OCR, AI tags) plug in as more steps.
/// </summary>
public interface IIngestStep
{
    Task RunAsync(string filePath, MediaAssetRow media, CancellationToken ct);
}

public sealed partial class MetadataIngestStep(Ffmpeg ffmpeg, ILogger<MetadataIngestStep> log) : IIngestStep
{
    public async Task RunAsync(string filePath, MediaAssetRow media, CancellationToken ct)
    {
        try
        {
            if (media.MediaType == MediaTypes.Video) await ReadVideoAsync(filePath, media, ct);
            else ReadImage(filePath, media);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            // Bad or exotic metadata must never block storing the original.
            log.LogWarning(e, "Metadata extraction failed for {File}", media.OriginalFilename);
        }
    }

    static void ReadImage(string path, MediaAssetRow m)
    {
        var dirs = ImageMetadataReader.ReadMetadata(path);
        var sub = dirs.OfType<ExifSubIfdDirectory>().FirstOrDefault();
        var ifd0 = dirs.OfType<ExifIfd0Directory>().FirstOrDefault();

        if (sub != null && (sub.TryGetDateTime(ExifDirectoryBase.TagDateTimeOriginal, out var dt) ||
                            sub.TryGetDateTime(ExifDirectoryBase.TagDateTimeDigitized, out dt)))
            m.DateTaken = dt;
        m.CameraMake = ifd0?.GetDescription(ExifDirectoryBase.TagMake)?.Trim();
        m.CameraModel = ifd0?.GetDescription(ExifDirectoryBase.TagModel)?.Trim();

        var gps = dirs.OfType<GpsDirectory>().FirstOrDefault();
        if (gps?.GetGeoLocation() is { IsZero: false } loc)
        {
            m.Latitude = loc.Latitude;
            m.Longitude = loc.Longitude;
            m.LocationSource = "exif";
            if (gps.TryGetRational(GpsDirectory.TagAltitude, out var alt))
                m.Altitude = gps.TryGetInt32(GpsDirectory.TagAltitudeRef, out var aref) && aref == 1 ? -alt.ToDouble() : alt.ToDouble();
        }

        if (Skia.Dimensions(path) is { } size) (m.Width, m.Height) = size;
    }

    async Task ReadVideoAsync(string path, MediaAssetRow m, CancellationToken ct)
    {
        var json = await ffmpeg.ProbeAsync(path, ct);
        if (json == null) return;
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (root.TryGetProperty("streams", out var streams))
        {
            foreach (var s in streams.EnumerateArray())
            {
                if (s.GetProperty("codec_type").GetString() != "video") continue;
                int w = s.GetProperty("width").GetInt32(), h = s.GetProperty("height").GetInt32();
                (m.Width, m.Height) = Math.Abs(Rotation(s)) % 180 == 90 ? (h, w) : (w, h);
                break;
            }
        }

        if (!root.TryGetProperty("format", out var format)) return;
        if (format.TryGetProperty("duration", out var d) && double.TryParse(d.GetString(), CultureInfo.InvariantCulture, out var secs))
            m.Duration = secs;
        if (!format.TryGetProperty("tags", out var tags)) return;

        if (Tag(tags, "creation_time") is { } created &&
            DateTimeOffset.TryParse(created, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var c) && c.Year > 1970)
            m.DateTaken = c.ToLocalTime().DateTime; // container TZ = home TZ, so this is local wall-clock like EXIF
        if ((Tag(tags, "com.apple.quicktime.location.ISO6709") ?? Tag(tags, "location")) is { } iso && ParseIso6709(iso) is { } p)
        {
            (m.Latitude, m.Longitude, m.Altitude) = p;
            m.LocationSource = "exif";
        }
    }

    static int Rotation(JsonElement stream)
    {
        if (stream.TryGetProperty("side_data_list", out var sd))
            foreach (var e in sd.EnumerateArray())
                if (e.TryGetProperty("rotation", out var r)) return r.GetInt32();
        if (stream.TryGetProperty("tags", out var t) && Tag(t, "rotate") is { } rot && int.TryParse(rot, out var deg)) return deg;
        return 0;
    }

    static string? Tag(JsonElement tags, string name)
    {
        foreach (var p in tags.EnumerateObject())
            if (p.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) return p.Value.GetString();
        return null;
    }

    [GeneratedRegex(@"^([+-]\d+(?:\.\d+)?)([+-]\d+(?:\.\d+)?)([+-]\d+(?:\.\d+)?)?")]
    private static partial Regex Iso6709();

    public static (double Lat, double Lon, double? Alt)? ParseIso6709(string s)
    {
        var match = Iso6709().Match(s);
        if (!match.Success) return null;
        double P(int i) => double.Parse(match.Groups[i].Value, CultureInfo.InvariantCulture);
        var (lat, lon) = (P(1), P(2));
        if (lat == 0 && lon == 0) return null;
        return (lat, lon, match.Groups[3].Success ? P(3) : null);
    }
}

/// <summary>Thin wrapper over the ffmpeg/ffprobe CLIs. Missing binaries degrade to "no metadata / no thumbnail".</summary>
public sealed class Ffmpeg(ILogger<Ffmpeg> log)
{
    Task<bool>? _available;

    /// <summary>Whether ffprobe runs at all (checked once). Without it, videos get no metadata or poster frames.</summary>
    public Task<bool> IsAvailableAsync() =>
        _available ??= RunAsync("ffprobe", ["-version"], CancellationToken.None).ContinueWith(t => t.Result.Code == 0);

    public async Task<string?> ProbeAsync(string path, CancellationToken ct)
    {
        var (code, stdout) = await RunAsync("ffprobe", ["-v", "quiet", "-print_format", "json", "-show_format", "-show_streams", path], ct);
        return code == 0 ? stdout : null;
    }

    public async Task<bool> FrameAsync(string input, string output, int maxSize, double atSeconds, CancellationToken ct)
    {
        var scale = $"scale='min({maxSize},iw)':'min({maxSize},ih)':force_original_aspect_ratio=decrease";
        var (code, _) = await RunAsync("ffmpeg",
            ["-v", "error", "-y", "-ss", atSeconds.ToString(CultureInfo.InvariantCulture), "-i", input, "-frames:v", "1", "-vf", scale, "-q:v", "4", output], ct);
        return code == 0 && File.Exists(output) && new FileInfo(output).Length > 0;
    }

    async Task<(int Code, string Stdout)> RunAsync(string exe, string[] args, CancellationToken ct)
    {
        var psi = new ProcessStartInfo(exe) { RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var a in args) psi.ArgumentList.Add(a);
        try
        {
            using var p = Process.Start(psi)!;
            var stdout = p.StandardOutput.ReadToEndAsync(ct);
            var stderr = p.StandardError.ReadToEndAsync(ct);
            await p.WaitForExitAsync(ct);
            if (p.ExitCode != 0) log.LogWarning("{Exe} exited {Code}: {Err}", exe, p.ExitCode, await stderr);
            return (p.ExitCode, await stdout);
        }
        catch (System.ComponentModel.Win32Exception)
        {
            log.LogWarning("{Exe} not found; skipping", exe);
            return (-1, "");
        }
    }
}

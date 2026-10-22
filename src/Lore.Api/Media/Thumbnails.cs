using Lore.Api.Data;
using Lore.Api.Storage;
using Lore.Contracts;

namespace Lore.Api.Media;

/// <summary>
/// Derived images live outside the storage volume and are disposable:
/// delete the derived folder and they regenerate on next request.
/// </summary>
public sealed class Thumbnails(string derivedRoot, StorageRegistry storage, Ffmpeg ffmpeg, ILogger<Thumbnails> log)
{
    public static readonly Dictionary<string, int> Sizes = new() { ["thumb"] = 400, ["preview"] = 1600 };

    readonly SemaphoreSlim _gate = new(2);

    public void Delete(Guid mediaId)
    {
        foreach (var kind in Sizes.Keys)
        {
            var path = PathFor(mediaId, kind);
            if (File.Exists(path)) File.Delete(path);
        }
    }

    string PathFor(Guid mediaId, string kind)
    {
        var id = mediaId.ToString("N");
        return Path.Combine(derivedRoot, kind, id[..2], id + ".jpg");
    }

    /// <returns>Path to a JPEG, or null when this media can't be rendered (e.g. HEIC without libheif).</returns>
    public async Task<string?> GetAsync(MediaAssetRow m, string kind, CancellationToken ct)
    {
        var path = PathFor(m.Id, kind);
        if (File.Exists(path)) return path;

        await _gate.WaitAsync(ct);
        try
        {
            if (File.Exists(path)) return path;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var tmp = path + "." + Guid.NewGuid().ToString("N") + ".tmp.jpg";
            try
            {
                if (!await RenderAsync(m, tmp, Sizes[kind], ct)) return null;
                File.Move(tmp, path, overwrite: true);
                return path;
            }
            finally
            {
                if (File.Exists(tmp)) File.Delete(tmp);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    async Task<bool> RenderAsync(MediaAssetRow m, string output, int size, CancellationToken ct)
    {
        var volume = storage.Get(m.StorageVolumeId);
        if (m.MediaType == MediaTypes.Video)
        {
            // ffmpeg needs a seekable path, not a stream. shortcut: assumes a local volume path; NAS volumes need a temp copy.
            string src;
            await using (var fs = (FileStream)volume.OpenRead(m.ObjectKey)) src = fs.Name;
            return await ffmpeg.FrameAsync(src, output, size, 1, ct) || await ffmpeg.FrameAsync(src, output, size, 0, ct);
        }

        await using (var stream = volume.OpenRead(m.ObjectKey))
            if (Skia.WriteJpegThumbnail(stream, output, size)) return true;

        if (m.MimeType is "image/heic" or "image/heif")
        {
            // Skia on Linux has no HEIF codec: libheif decodes to a full-size JPEG, then Skia scales it as usual.
            string src;
            await using (var fs = (FileStream)volume.OpenRead(m.ObjectKey)) src = fs.Name;
            var full = output + ".full.jpg";
            try
            {
                if (await ffmpeg.HeifToJpegAsync(src, full, ct))
                {
                    await using var decoded = File.OpenRead(full);
                    if (Skia.WriteJpegThumbnail(decoded, output, size)) return true;
                }
            }
            finally
            {
                File.Delete(full);
            }
        }
        log.LogInformation("No thumbnail for {Id} ({Mime})", m.Id, m.MimeType);
        return false;
    }
}

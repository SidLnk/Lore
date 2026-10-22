using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Lore.Contracts;
using Microsoft.AspNetCore.Http.Features;

namespace Lore.Api.Endpoints;

/// <summary>
/// In-app updates from the home server: the admin uploads a signed APK, phones on the LAN check and download it.
/// Stored as {DataRoot}/app/lore.apk plus lore.json; the newest upload replaces the previous one.
/// </summary>
public static class AppEndpoints
{
    public const string PackageName = "arpa.home.lore";
    const long MaxApkBytes = 300L * 1024 * 1024;

    public static void Map(RouteGroupBuilder api, RouteGroupBuilder admin, LorePaths paths)
    {
        string Apk() => Path.Combine(paths.App, "lore.apk");
        string Info() => Path.Combine(paths.App, "lore.json");
        AppVersionDto? Current() => File.Exists(Info()) && File.Exists(Apk())
            ? JsonSerializer.Deserialize<AppVersionDto>(File.ReadAllText(Info()), JsonSerializerOptions.Web) : null;

        api.MapGet("/app", () => Current() is { } v ? Results.Ok(v) : Results.NotFound());
        api.MapGet("/app/apk", () => Current() is null ? Results.NotFound()
            : Results.Stream(File.OpenRead(Apk()), "application/vnd.android.package-archive", "lore.apk", enableRangeProcessing: true));

        admin.MapGet("/api/app", () => Current() is { } v ? Results.Ok(v) : Results.NotFound());

        // Raw body (the admin page posts the file as-is); larger than Kestrel's 30 MB default.
        admin.MapPost("/api/app", async (HttpContext ctx) =>
        {
            if (ctx.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } limit) limit.MaxRequestBodySize = MaxApkBytes;
            var tmp = Path.Combine(paths.App, $"upload-{Guid.NewGuid():N}.apk");
            try
            {
                string sha;
                await using (var file = File.Create(tmp))
                using (var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
                {
                    var buffer = new byte[81920];
                    int n;
                    while ((n = await ctx.Request.Body.ReadAsync(buffer, ctx.RequestAborted)) > 0)
                    {
                        hash.AppendData(buffer, 0, n);
                        await file.WriteAsync(buffer.AsMemory(0, n), ctx.RequestAborted);
                    }
                    sha = Convert.ToHexStringLower(hash.GetHashAndReset());
                }

                ApkManifest m;
                try { m = ApkManifest.Read(tmp); }
                catch (Exception e) when (e is InvalidDataException or EndOfStreamException or ArgumentOutOfRangeException)
                {
                    return Results.BadRequest("That file isn't an Android app (APK).");
                }
                if (m.Package != PackageName) return Results.BadRequest($"That APK is {m.Package}, not Lore ({PackageName}).");
                if (Current() is { } cur && m.VersionCode < cur.VersionCode)
                    return Results.BadRequest($"That's version {m.VersionName}, older than {cur.VersionName} already on the server.");

                var dto = new AppVersionDto(m.VersionCode, m.VersionName, sha, new FileInfo(tmp).Length, DateTimeOffset.UtcNow);
                File.Move(tmp, Apk(), overwrite: true);
                await File.WriteAllTextAsync(Info(), JsonSerializer.Serialize(dto, JsonSerializerOptions.Web));
                return Results.Ok(dto);
            }
            finally
            {
                if (File.Exists(tmp)) File.Delete(tmp);
            }
        });
    }
}

/// <summary>Package name and version from an APK's binary AndroidManifest.xml (no aapt needed).</summary>
public sealed record ApkManifest(string Package, int VersionCode, string VersionName)
{
    public static ApkManifest Read(string apkPath)
    {
        using var zip = ZipFile.OpenRead(apkPath);
        var entry = zip.GetEntry("AndroidManifest.xml") ?? throw new InvalidDataException("No AndroidManifest.xml");
        using var s = entry.Open();
        using var ms = new MemoryStream();
        s.CopyTo(ms);
        return Parse(ms.ToArray());
    }

    /// <summary>Walks the AXML chunks to the first start tag (manifest) and reads its attributes.</summary>
    public static ApkManifest Parse(byte[] axml)
    {
        if (BitConverter.ToUInt16(axml, 0) != 0x0003) throw new InvalidDataException("Not binary XML");
        string[] strings = [];
        int pos = BitConverter.ToUInt16(axml, 2);
        while (pos + 8 <= axml.Length)
        {
            var type = BitConverter.ToUInt16(axml, pos);
            var headerSize = BitConverter.ToUInt16(axml, pos + 2);
            var size = BitConverter.ToInt32(axml, pos + 4);
            if (size <= 0) break;
            if (type == 0x0001) strings = ReadStringPool(axml, pos);
            else if (type == 0x0102 && strings.Length > 0)
            {
                string? package = null, versionName = null;
                int? versionCode = null;
                int attrStart = BitConverter.ToUInt16(axml, pos + 16 + 8), attrSize = BitConverter.ToUInt16(axml, pos + 18 + 8),
                    attrCount = BitConverter.ToUInt16(axml, pos + 20 + 8);
                var a = pos + headerSize + attrStart;
                for (var i = 0; i < attrCount; i++, a += attrSize)
                {
                    var name = strings[BitConverter.ToInt32(axml, a + 4)];
                    var raw = BitConverter.ToInt32(axml, a + 8);
                    var dataType = axml[a + 15];
                    var data = BitConverter.ToInt32(axml, a + 16);
                    string? Str() => raw >= 0 ? strings[raw] : dataType == 0x03 ? strings[data] : null;
                    switch (name)
                    {
                        case "package": package = Str(); break;
                        case "versionName": versionName = Str(); break;
                        case "versionCode": versionCode = dataType is >= 0x10 and <= 0x11 ? data : int.TryParse(Str(), out var v) ? v : null; break;
                    }
                }
                return new ApkManifest(package ?? throw new InvalidDataException("No package"),
                    versionCode ?? throw new InvalidDataException("No versionCode"), versionName ?? versionCode.ToString()!);
            }
            pos += size;
        }
        throw new InvalidDataException("No manifest element");
    }

    static string[] ReadStringPool(byte[] b, int pos)
    {
        var count = BitConverter.ToInt32(b, pos + 8);
        var utf8 = (BitConverter.ToInt32(b, pos + 16) & 0x100) != 0;
        var stringsStart = pos + BitConverter.ToInt32(b, pos + 20);
        var result = new string[count];
        for (var i = 0; i < count; i++)
        {
            var p = stringsStart + BitConverter.ToInt32(b, pos + 28 + i * 4);
            if (utf8)
            {
                p += (b[p] & 0x80) != 0 ? 2 : 1; // UTF-16 length, skipped
                var len = b[p] & 0x7F;
                if ((b[p] & 0x80) != 0) len = (len << 8) | b[++p];
                result[i] = Encoding.UTF8.GetString(b, p + 1, len);
            }
            else
            {
                int len = BitConverter.ToUInt16(b, p);
                if ((len & 0x8000) != 0) { len = ((len & 0x7FFF) << 16) | BitConverter.ToUInt16(b, p + 2); p += 2; }
                result[i] = Encoding.Unicode.GetString(b, p + 2, len * 2);
            }
        }
        return result;
    }
}

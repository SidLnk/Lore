using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using Lore.Api.Endpoints;
using Lore.Client;

namespace Lore.Api.Tests;

public sealed class AppUpdateTests : IDisposable
{
    readonly LoreFactory _f = new();
    public void Dispose() => _f.Dispose();

    // Binary manifest taken from the real Lore 1.2 release APK.
    static byte[] Manifest => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "AndroidManifest.bin"));

    static byte[] Apk()
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            using (var s = zip.CreateEntry("AndroidManifest.xml").Open()) s.Write(Manifest);
            using (var s = zip.CreateEntry("classes.dex").Open()) s.Write(Fixtures.RandomBytes(5000));
        }
        return ms.ToArray();
    }

    [Fact]
    public void Reads_package_and_version_from_binary_manifest() =>
        Assert.Equal(new ApkManifest("arpa.home.lore", 3, "1.2"), ApkManifest.Parse(Manifest));

    [Fact]
    public async Task Admin_uploads_apk_and_phones_download_it()
    {
        var api = await _f.PairAsync();
        Assert.Null(await api.GetAppVersionAsync());

        var apk = Apk();
        var up = await _f.Admin().PostAsync("/admin/api/app", new ByteArrayContent(apk));
        Assert.Equal(HttpStatusCode.OK, up.StatusCode);

        var v = await api.GetAppVersionAsync();
        Assert.Equal((3, "1.2", Fixtures.Sha256(apk), (long)apk.Length), (v!.VersionCode, v.VersionName, v.Sha256, v.Size));
        await using var s = await api.OpenAppApkAsync();
        using var ms = new MemoryStream();
        await s.CopyToAsync(ms);
        Assert.Equal(apk, ms.ToArray());
    }

    [Fact]
    public async Task Rejects_files_that_are_not_an_apk_and_requires_admin()
    {
        var notApk = await _f.Admin().PostAsync("/admin/api/app", new ByteArrayContent(Fixtures.RandomBytes(2000)));
        Assert.Equal(HttpStatusCode.BadRequest, notApk.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _f.CreateClient().PostAsync("/admin/api/app", new ByteArrayContent(Apk()))).StatusCode);
        Assert.Null(await (await _f.PairAsync()).GetAppVersionAsync());
    }
}

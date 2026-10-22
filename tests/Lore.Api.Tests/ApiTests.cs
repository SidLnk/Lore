using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Dapper;
using Lore.Api.Data;
using Lore.Client;
using Lore.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace Lore.Api.Tests;

public sealed class ApiTests : IDisposable
{
    readonly LoreFactory _f = new();
    public void Dispose() => _f.Dispose();

    [Fact]
    public async Task Admin_page_requires_password()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await _f.CreateClient().GetAsync("/admin/")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _f.Admin().GetAsync("/admin/")).StatusCode);
    }

    [Fact]
    public async Task Pairing_code_works_once_and_expires()
    {
        var api = new LoreApiClient(_f.CreateClient());
        var code = await _f.NewPairingCodeAsync();
        await api.PairAsync(code, "Phone A");
        var reused = await Assert.ThrowsAsync<LoreApiException>(() => api.PairAsync(code, "Phone B"));
        Assert.Equal(HttpStatusCode.Forbidden, reused.Status);

        using (var c = _f.Services.GetRequiredService<Db>().Open())
            await c.ExecuteAsync("INSERT INTO PairingCode(Code, ExpiresAt) VALUES ('111111', @t)", new { t = DateTimeOffset.UtcNow.AddMinutes(-1) });
        var expired = await Assert.ThrowsAsync<LoreApiException>(() => api.PairAsync("111111", "Phone C"));
        Assert.Equal(HttpStatusCode.Forbidden, expired.Status);
    }

    [Fact]
    public async Task Api_rejects_unpaired_and_revoked_devices()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await _f.CreateClient().GetAsync("/api/media")).StatusCode);

        var api = await _f.PairAsync();
        var device = await api.GetDeviceAsync();
        (await _f.Admin().PostAsync($"/admin/api/devices/{device.Id}/revoke", null)).EnsureSuccessStatusCode();
        var e = await Assert.ThrowsAsync<LoreApiException>(() => api.GetMediaAsync(new MediaQuery()));
        Assert.Equal(HttpStatusCode.Unauthorized, e.Status);
    }

    [Fact]
    public async Task Chunk_at_wrong_offset_returns_server_offset_and_upload_resumes()
    {
        var api = await _f.PairAsync();
        var data = Fixtures.RandomBytes(1000);
        var up = await api.CreateUploadAsync(new(Fixtures.Sha256(data), data.Length, "clip.mp4", "video/mp4", 1, null));

        Assert.Equal(400, await api.PutChunkAsync(up.UploadId!.Value, 0, data.AsMemory(0, 400)));
        Assert.Equal(400, await api.PutChunkAsync(up.UploadId!.Value, 0, data.AsMemory(0, 400))); // stale offset: 409, nothing appended

        var again = await api.CreateUploadAsync(new(Fixtures.Sha256(data), data.Length, "clip.mp4", "video/mp4", 1, null));
        Assert.Equal(up.UploadId, again.UploadId);
        Assert.Equal(400, again.ReceivedBytes);

        Assert.Equal(1000, await api.PutChunkAsync(up.UploadId!.Value, 400, data.AsMemory(400)));
        var done = await api.CompleteUploadAsync(up.UploadId!.Value);
        Assert.False(done.Duplicate);
        Assert.Equal(data, await _f.CreateClient().GetByteArrayAsync(api.OriginalUrl(done.MediaId)));
    }

    [Fact]
    public async Task Sha_mismatch_is_rejected_and_nothing_is_stored()
    {
        var api = await _f.PairAsync();
        var data = Fixtures.RandomBytes(500);
        var up = await api.CreateUploadAsync(new(Fixtures.Sha256(Fixtures.RandomBytes(500)), data.Length, "x.jpg", "image/jpeg", 1, null));
        await api.PutChunkAsync(up.UploadId!.Value, 0, data);

        var e = await Assert.ThrowsAsync<LoreApiException>(() => api.CompleteUploadAsync(up.UploadId!.Value));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, e.Status);
        Assert.Empty((await api.GetMediaAsync(new MediaQuery())).Items);
        Assert.False(Directory.Exists(Path.Combine(_f.MediaRoot, "originals")) && Directory.EnumerateFiles(Path.Combine(_f.MediaRoot, "originals"), "*", SearchOption.AllDirectories).Any());
    }

    [Fact]
    public async Task Same_content_from_another_device_is_a_duplicate()
    {
        var a = await _f.PairAsync("A");
        var b = await _f.PairAsync("B", defaultOwner: 2);
        var jpeg = Fixtures.Jpeg(1, new DateTime(2024, 1, 1));
        var sha = Fixtures.Sha256(jpeg);

        var up = await a.CreateUploadAsync(new(sha, jpeg.Length, "a.jpg", "image/jpeg", 1, null));
        await a.PutChunkAsync(up.UploadId!.Value, 0, jpeg);
        var done = await a.CompleteUploadAsync(up.UploadId!.Value);

        var dup = await b.CreateUploadAsync(new(sha, jpeg.Length, "copy.jpg", "image/jpeg", 2, null));
        Assert.True(dup.Duplicate);
        Assert.Equal(done.MediaId, dup.MediaId);
    }

    [Fact]
    public async Task Photo_metadata_and_thumbnail()
    {
        var api = await _f.PairAsync(defaultOwner: 2);
        // Orientation 6 = rotated 90°: stored 64x48 must report as 48x64.
        var jpeg = Fixtures.Jpeg(7, new DateTime(2023, 7, 4, 18, 30, 0), gps: (40.4462, -79.9822), orientation: 6);
        var up = await api.CreateUploadAsync(new(Fixtures.Sha256(jpeg), jpeg.Length, "IMG_1.JPG", "image/jpeg", 3, new DateTime(2000, 1, 1)));
        await api.PutChunkAsync(up.UploadId!.Value, 0, jpeg);
        var done = await api.CompleteUploadAsync(up.UploadId!.Value);
        var m = (await api.GetMediaAsync(new MediaQuery())).Items.Single(x => x.Id == done.MediaId);

        Assert.Equal(new DateTime(2023, 7, 4, 18, 30, 0), m.DateTaken);
        Assert.Equal(40.4462, m.Latitude!.Value, 3);
        Assert.Equal(-79.9822, m.Longitude!.Value, 3);
        Assert.Equal((48, 64), (m.Width, m.Height));
        Assert.Equal(("TestMake", "TestModel"), (m.CameraMake, m.CameraModel));
        Assert.Equal((3, 2), (m.OwnerId, m.UploadedById));
        Assert.Equal(MediaTypes.Photo, m.MediaType);

        var thumb = await _f.CreateClient().GetAsync(api.ThumbUrl(m.Id));
        Assert.Equal(HttpStatusCode.OK, thumb.StatusCode);
        Assert.Equal("image/jpeg", thumb.Content.Headers.ContentType!.MediaType);
        using var decoded = SixLabors.ImageSharp.Image.Load(await thumb.Content.ReadAsByteArrayAsync());
        Assert.Equal((48, 64), (decoded.Width, decoded.Height)); // thumbnail is upright
    }

    [Fact]
    public async Task Original_supports_range_requests()
    {
        var api = await _f.PairAsync();
        var data = Fixtures.RandomBytes(4096);
        var up = await api.CreateUploadAsync(new(Fixtures.Sha256(data), data.Length, "v.mp4", "video/mp4", 1, null));
        await api.PutChunkAsync(up.UploadId!.Value, 0, data);
        var id = (await api.CompleteUploadAsync(up.UploadId!.Value)).MediaId;

        var req = new HttpRequestMessage(HttpMethod.Get, api.OriginalUrl(id));
        req.Headers.Range = new RangeHeaderValue(100, 199);
        var res = await _f.CreateClient().SendAsync(req);
        Assert.Equal(HttpStatusCode.PartialContent, res.StatusCode);
        Assert.Equal(data[100..200], await res.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task Bad_query_values_are_rejected()
    {
        var api = await _f.PairAsync();
        var e = await Assert.ThrowsAsync<LoreApiException>(() => api.GetMediaAsync(new MediaQuery(Type: "document")));
        Assert.Equal(HttpStatusCode.BadRequest, e.Status);
    }
}

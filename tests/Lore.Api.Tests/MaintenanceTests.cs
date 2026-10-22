using System.Net;
using System.Text.Json;
using Lore.Api.Data;
using Lore.Api.Endpoints;
using Lore.Client;
using Microsoft.Extensions.DependencyInjection;

namespace Lore.Api.Tests;

public sealed class MaintenanceTests : IDisposable
{
    readonly LoreFactory _f = new();
    public void Dispose() => _f.Dispose();

    [Fact]
    public async Task Abandoned_uploads_and_stray_parts_are_removed_active_ones_kept()
    {
        var api = await _f.PairAsync();
        var paths = _f.Services.GetRequiredService<LorePaths>();
        var maintenance = _f.Services.GetRequiredService<MaintenanceService>();

        var stale = Fixtures.RandomBytes(100);
        var staleUp = await api.CreateUploadAsync(new(Fixtures.Sha256(stale), 200, "old.mp4", "video/mp4", 1, null));
        await api.PutChunkAsync(staleUp.UploadId!.Value, 0, stale);
        File.SetLastWriteTimeUtc(paths.Part(staleUp.UploadId!.Value), DateTime.UtcNow.AddDays(-8));

        var active = Fixtures.RandomBytes(100);
        var activeUp = await api.CreateUploadAsync(new(Fixtures.Sha256(active), 200, "new.mp4", "video/mp4", 1, null));
        await api.PutChunkAsync(activeUp.UploadId!.Value, 0, active);

        var stray = Path.Combine(paths.Uploads, Guid.NewGuid().ToString("N") + ".part");
        await File.WriteAllBytesAsync(stray, [1, 2, 3]);
        File.SetLastWriteTimeUtc(stray, DateTime.UtcNow.AddDays(-8));

        Assert.Equal(2, await maintenance.CleanAbandonedUploadsAsync(DateTimeOffset.UtcNow));
        Assert.False(File.Exists(paths.Part(staleUp.UploadId!.Value)));
        Assert.False(File.Exists(stray));
        Assert.Equal(HttpStatusCode.NotFound, (await Assert.ThrowsAsync<LoreApiException>(() => api.GetUploadAsync(staleUp.UploadId!.Value))).Status);
        Assert.Equal(100, (await api.GetUploadAsync(activeUp.UploadId!.Value)).ReceivedBytes);

        // A phone coming back to the abandoned upload starts over transparently.
        var again = await api.CreateUploadAsync(new(Fixtures.Sha256(stale), 200, "old.mp4", "video/mp4", 1, null));
        Assert.Equal(0, again.ReceivedBytes);
    }

    [Fact]
    public async Task Admin_status_reports_library_and_uploads()
    {
        var api = await _f.PairAsync();
        var data = Fixtures.RandomBytes(300);
        var up = await api.CreateUploadAsync(new(Fixtures.Sha256(data), data.Length, "x.mp4", "video/mp4", 1, null));
        await api.PutChunkAsync(up.UploadId!.Value, 0, data.AsMemory(0, 100));

        var res = await _f.Admin().GetAsync("/admin/api/status");
        res.EnsureSuccessStatusCode();
        var s = JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal(0, s.GetProperty("mediaCount").GetInt64());
        Assert.Equal(1, s.GetProperty("uploadsInProgress").GetInt64());
        Assert.Equal(100, s.GetProperty("partialBytes").GetInt64());
        Assert.True(s.GetProperty("volumeFreeBytes").GetInt64() > 0);
        Assert.True(s.GetProperty("ffmpegAvailable").ValueKind is JsonValueKind.True or JsonValueKind.False);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _f.CreateClient().GetAsync("/admin/api/status")).StatusCode);
    }
}

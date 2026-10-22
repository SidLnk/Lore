using Dapper;
using Lore.Api.Endpoints;

namespace Lore.Api.Data;

/// <summary>
/// Daily housekeeping:
/// - The live DB sits on a Docker volume (SQLite locking is unreliable on Windows bind mounts),
///   so copy a consistent snapshot next to the media. Keeps the newest 7.
/// - Drop uploads abandoned for a week, so half-sent videos don't fill the disk.
/// </summary>
public sealed class MaintenanceService(Db db, LorePaths paths, ILogger<MaintenanceService> log) : BackgroundService
{
    public static readonly TimeSpan AbandonedAfter = TimeSpan.FromDays(7);

    public DateTimeOffset? LastSnapshotAt { get; private set; }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromHours(24));
        do
        {
            await RunAsync("DB snapshot", SnapshotAsync);
            await RunAsync("Upload cleanup", async () =>
            {
                var removed = await CleanAbandonedUploadsAsync(DateTimeOffset.UtcNow);
                if (removed > 0) log.LogInformation("Removed {Count} abandoned uploads", removed);
            });
        } while (await timer.WaitForNextTickAsync(ct));
    }

    async Task RunAsync(string name, Func<Task> job)
    {
        try { await job(); }
        catch (Exception e) when (e is not OperationCanceledException) { log.LogError(e, "{Job} failed", name); }
    }

    async Task SnapshotAsync()
    {
        var target = Path.Combine(paths.Snapshots, $"lore-{DateTime.Now:yyyyMMdd}.db");
        if (File.Exists(target)) File.Delete(target);
        using (var c = db.Open()) await c.ExecuteAsync("VACUUM INTO @target", new { target });
        foreach (var old in Directory.GetFiles(paths.Snapshots, "lore-*.db").OrderDescending().Skip(7)) File.Delete(old);
        LastSnapshotAt = DateTimeOffset.UtcNow;
        log.LogInformation("DB snapshot written to {Target}", target);
    }

    /// <summary>
    /// Removes uploads with no bytes received for <see cref="AbandonedAfter"/>, and stray .part files with no upload row.
    /// A phone resuming later gets 404 and starts over, which the upload engine already handles.
    /// </summary>
    public async Task<int> CleanAbandonedUploadsAsync(DateTimeOffset now)
    {
        var cutoff = now - AbandonedAfter;
        using var c = db.Open();
        var uploads = (await c.QueryAsync<UploadRow>("SELECT * FROM Upload")).ToList();
        var removed = 0;
        foreach (var up in uploads)
        {
            var part = new FileInfo(paths.Part(up.Id));
            var lastActivity = part.Exists ? new DateTimeOffset(part.LastWriteTimeUtc, TimeSpan.Zero) : up.CreatedAt;
            if (lastActivity >= cutoff) continue;
            await c.ExecuteAsync("DELETE FROM Upload WHERE Id = @Id", up);
            if (part.Exists) part.Delete();
            removed++;
        }

        var live = uploads.Select(u => u.Id.ToString("N")).ToHashSet();
        foreach (var file in new DirectoryInfo(paths.Uploads).EnumerateFiles("*.part"))
        {
            if (!live.Contains(Path.GetFileNameWithoutExtension(file.Name)) && file.LastWriteTimeUtc < cutoff.UtcDateTime)
            {
                file.Delete();
                removed++;
            }
        }
        return removed;
    }
}

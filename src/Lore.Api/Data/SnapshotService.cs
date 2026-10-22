using Dapper;
using Lore.Api.Endpoints;

namespace Lore.Api.Data;

/// <summary>
/// The live DB sits on a Docker volume (SQLite locking is unreliable on Windows bind mounts),
/// so copy a consistent snapshot next to the media daily. Keeps the newest 7.
/// </summary>
public sealed class SnapshotService(Db db, LorePaths paths, ILogger<SnapshotService> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromHours(24));
        do
        {
            try
            {
                var target = Path.Combine(paths.Snapshots, $"lore-{DateTime.Now:yyyyMMdd}.db");
                if (File.Exists(target)) File.Delete(target);
                using (var c = db.Open()) await c.ExecuteAsync("VACUUM INTO @target", new { target });
                foreach (var old in Directory.GetFiles(paths.Snapshots, "lore-*.db").OrderDescending().Skip(7)) File.Delete(old);
                log.LogInformation("DB snapshot written to {Target}", target);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                log.LogError(e, "DB snapshot failed");
            }
        } while (await timer.WaitForNextTickAsync(ct));
    }
}

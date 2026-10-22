using SQLite;

namespace Lore.Client;

public enum UploadStatus { Pending, Uploading, Done, Failed }

/// <summary>
/// A phone gallery file whose exact bytes (Sha256) Lore holds, as of RecordedAt. Kept apart from the queue,
/// so "Clear done" doesn't forget it. It's a hint, not proof: Free up space re-hashes and re-asks the server first.
/// </summary>
public sealed class InLoreItem
{
    [PrimaryKey] public string ContentUri { get; set; } = "";
    public string Sha256 { get; set; } = "";
    public long Size { get; set; }
    public DateTime RecordedAt { get; set; }
}

/// <summary>One file waiting to go to the server. Persisted so the queue survives app restarts.</summary>
public sealed class QueueItem
{
    [PrimaryKey, AutoIncrement] public int Id { get; set; }
    /// <summary>Private copy in app storage (deleted once uploaded), or a content URI read in place.</summary>
    public string FilePath { get; set; } = "";
    public string FileName { get; set; } = "";
    public string MimeType { get; set; } = "";
    public long Size { get; set; }
    public int OwnerId { get; set; }
    public DateTime? ClientDateTaken { get; set; }
    public string? Sha256 { get; set; }
    public Guid? UploadId { get; set; }
    public long Offset { get; set; }
    public int ChunkSize { get; set; }
    [Indexed] public UploadStatus Status { get; set; }
    public int Attempts { get; set; }
    public string? Error { get; set; }
    public Guid? MediaId { get; set; }
    public bool WasDuplicate { get; set; }
    public DateTime QueuedAt { get; set; }
}

public sealed class UploadQueue
{
    readonly SQLiteAsyncConnection _db;

    public UploadQueue(string dbPath)
    {
        _db = new SQLiteAsyncConnection(dbPath);
        var c = _db.GetConnection();
        c.CreateTable<QueueItem>();
        c.CreateTable<InLoreItem>();
        // Backfill from finished gallery uploads that predate the InLoreItem table.
        c.Execute("""
            INSERT OR IGNORE INTO InLoreItem(ContentUri, Sha256, Size, RecordedAt)
            SELECT FilePath, Sha256, Size, QueuedAt FROM QueueItem
            WHERE Status = ? AND Sha256 IS NOT NULL AND FilePath LIKE 'content://%'
            """, (int)UploadStatus.Done);
    }

    public Task MarkInLoreAsync(string contentUri, string sha256, long size) =>
        _db.InsertOrReplaceAsync(new InLoreItem { ContentUri = contentUri, Sha256 = sha256, Size = size, RecordedAt = DateTime.UtcNow });

    public Task<List<InLoreItem>> InLoreAsync() => _db.Table<InLoreItem>().ToListAsync();

    public Task ForgetInLoreAsync(string contentUri) => _db.DeleteAsync<InLoreItem>(contentUri);

    public Task AddAsync(QueueItem item)
    {
        item.Status = UploadStatus.Pending;
        item.QueuedAt = DateTime.UtcNow;
        return _db.InsertAsync(item);
    }

    public Task UpdateAsync(QueueItem item) => _db.UpdateAsync(item);

    /// <summary>Oldest unfinished item. "Uploading" counts: the app may have died mid-file.</summary>
    public Task<QueueItem?> NextAsync() =>
        _db.Table<QueueItem>()
            .Where(i => i.Status == UploadStatus.Pending || i.Status == UploadStatus.Uploading)
            .OrderBy(i => i.Id)
            .FirstOrDefaultAsync()!;

    public Task<List<QueueItem>> AllAsync() => _db.Table<QueueItem>().OrderByDescending(i => i.Id).ToListAsync();

    public async Task RetryFailedAsync()
    {
        foreach (var i in await _db.Table<QueueItem>().Where(i => i.Status == UploadStatus.Failed).ToListAsync())
        {
            i.Status = UploadStatus.Pending;
            i.Attempts = 0;
            i.Error = null;
            await _db.UpdateAsync(i);
        }
    }

    public Task<int> ClearDoneAsync() => _db.Table<QueueItem>().DeleteAsync(i => i.Status == UploadStatus.Done);

    public Task CloseAsync() => _db.CloseAsync();
}

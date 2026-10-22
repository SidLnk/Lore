using System.Net;
using System.Security.Cryptography;
using Lore.Contracts;

namespace Lore.Client;

/// <summary>
/// Drains the upload queue one file at a time: hash, create (dedupe), send chunks from the server's offset, complete.
/// Transient failures back off and retry; after MaxAttempts an item is Failed until the user retries it.
/// openRead/discard let the app use non-file sources (Android content URIs); the stream must be seekable.
/// </summary>
public sealed class UploadEngine(
    LoreApiClient api,
    UploadQueue queue,
    Func<CancellationToken, Task> waitForNetwork,
    Func<TimeSpan, CancellationToken, Task>? delay = null,
    Func<string, Stream>? openRead = null,
    Action<string>? discard = null)
{
    public const int MaxAttempts = 5;
    readonly Func<TimeSpan, CancellationToken, Task> _delay = delay ?? Task.Delay;
    readonly Func<string, Stream> _openRead = openRead ?? File.OpenRead;
    readonly Action<string> _discard = discard ?? File.Delete;

    /// <summary>Raised on every status or progress change, for UI and notifications.</summary>
    public event Action<QueueItem>? ItemChanged;

    public async Task RunAsync(CancellationToken ct)
    {
        while (await queue.NextAsync() is { } item)
        {
            await waitForNetwork(ct);
            try
            {
                item.Status = UploadStatus.Uploading;
                await SaveAsync(item);
                await UploadAsync(item, ct);
                item.Status = UploadStatus.Done;
                item.Error = null;
                await SaveAsync(item);
                _discard(item.FilePath);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                item.Status = UploadStatus.Pending;
                await SaveAsync(item);
                throw;
            }
            catch (Exception e) when (IsTransient(e))
            {
                item.Attempts++;
                item.Error = e.Message;
                item.Status = item.Attempts >= MaxAttempts ? UploadStatus.Failed : UploadStatus.Pending;
                await SaveAsync(item);
                if (item.Status == UploadStatus.Pending)
                    await _delay(TimeSpan.FromSeconds(Math.Min(60, Math.Pow(2, item.Attempts))), ct);
            }
            catch (Exception e)
            {
                item.Status = UploadStatus.Failed;
                item.Error = e.Message;
                await SaveAsync(item);
            }
        }
    }

    async Task UploadAsync(QueueItem item, CancellationToken ct)
    {
        if (item.Sha256 == null)
        {
            await using var f = _openRead(item.FilePath);
            item.Sha256 = Convert.ToHexStringLower(await SHA256.HashDataAsync(f, ct));
            await SaveAsync(item);
        }

        // A 404 means the server forgot this upload (finished, expired or discarded): start over from create,
        // which reports "duplicate" if an earlier complete actually succeeded.
        for (var restart = 0; ; restart++)
        {
            try
            {
                if (await CreateAsync(item, ct)) return;
                await SendChunksAsync(item, ct);
                var done = await api.CompleteUploadAsync(item.UploadId!.Value, ct);
                item.MediaId = done.MediaId;
                item.WasDuplicate = done.Duplicate;
                return;
            }
            catch (LoreApiException e) when (e.Status == HttpStatusCode.NotFound && restart == 0)
            {
                item.UploadId = null;
            }
        }
    }

    /// <returns>True when the server already has this content.</returns>
    async Task<bool> CreateAsync(QueueItem item, CancellationToken ct)
    {
        var r = await api.CreateUploadAsync(
            new CreateUploadRequest(item.Sha256!, item.Size, item.FileName, item.MimeType, item.OwnerId, item.ClientDateTaken), ct);
        if (r.Duplicate)
        {
            item.MediaId = r.MediaId;
            item.WasDuplicate = true;
            return true;
        }
        item.UploadId = r.UploadId;
        item.Offset = r.ReceivedBytes;
        item.ChunkSize = r.ChunkSize;
        await SaveAsync(item);
        return false;
    }

    async Task SendChunksAsync(QueueItem item, CancellationToken ct)
    {
        await using var file = _openRead(item.FilePath);
        var buffer = new byte[item.ChunkSize];
        while (item.Offset < item.Size)
        {
            file.Position = item.Offset;
            var n = await file.ReadAtLeastAsync(buffer, (int)Math.Min(buffer.Length, item.Size - item.Offset), throwOnEndOfStream: true, ct);
            item.Offset = await api.PutChunkAsync(item.UploadId!.Value, item.Offset, buffer.AsMemory(0, n), ct);
            await SaveAsync(item);
        }
    }

    Task SaveAsync(QueueItem item)
    {
        ItemChanged?.Invoke(item);
        return queue.UpdateAsync(item);
    }

    static bool IsTransient(Exception e) => e switch
    {
        LoreApiException api => (int)api.Status >= 500 || api.Status is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests or HttpStatusCode.NotFound,
        HttpRequestException or TaskCanceledException or IOException and not FileNotFoundException and not DirectoryNotFoundException => true,
        _ => false,
    };
}

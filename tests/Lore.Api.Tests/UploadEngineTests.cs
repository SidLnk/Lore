using Lore.Client;
using Lore.Contracts;

namespace Lore.Api.Tests;

public sealed class UploadEngineTests : IDisposable
{
    readonly LoreFactory _f = new();
    readonly string _phone = Directory.CreateTempSubdirectory("lore-phone-").FullName;
    readonly List<UploadQueue> _queues = [];

    public void Dispose()
    {
        foreach (var q in _queues) q.CloseAsync().Wait();
        _f.Dispose();
        try { Directory.Delete(_phone, true); } catch (IOException) { }
    }

    static Task Online(CancellationToken _) => Task.CompletedTask;
    static Task NoWait(TimeSpan _, CancellationToken __) => Task.CompletedTask;

    UploadQueue NewQueue()
    {
        var q = new UploadQueue(Path.Combine(_phone, $"queue-{_queues.Count}.db"));
        _queues.Add(q);
        return q;
    }

    async Task<QueueItem> EnqueueAsync(UploadQueue q, string name, byte[] data, string mime, int owner, DateTime? taken = null)
    {
        var path = Path.Combine(_phone, name);
        await File.WriteAllBytesAsync(path, data);
        var item = new QueueItem { FilePath = path, FileName = name, MimeType = mime, Size = data.Length, OwnerId = owner, ClientDateTaken = taken };
        await q.AddAsync(item);
        return item;
    }

    /// <summary>The V1 milestone: 100+ files from one phone, browsed from another by date, owner and type.</summary>
    [Fact]
    public async Task Milestone_150_files_upload_and_browse_from_second_device()
    {
        var alexPhone = await _f.PairAsync("Alex phone", defaultOwner: 1);
        var queue = NewQueue();
        var start = new DateTime(2024, 1, 1, 9, 0, 0);
        for (var i = 0; i < 140; i++)
            await EnqueueAsync(queue, $"IMG_{i:D4}.jpg", Fixtures.Jpeg(i, start.AddDays(i), gps: i % 2 == 0 ? (51.5, -0.12) : null), "image/jpeg", owner: i % 3 == 0 ? 2 : 1);
        for (var i = 0; i < 10; i++)
            await EnqueueAsync(queue, $"VID_{i:D4}.mp4", Fixtures.RandomBytes(3000 + i), "video/mp4", owner: 1, taken: start.AddDays(200 + i));

        await new UploadEngine(alexPhone, queue, Online, NoWait).RunAsync(CancellationToken.None);

        var items = await queue.AllAsync();
        Assert.All(items, i => Assert.Equal(UploadStatus.Done, i.Status));
        Assert.All(items, i => Assert.False(File.Exists(i.FilePath))); // local copies cleaned up

        var samPhone = await _f.PairAsync("Sam phone", defaultOwner: 2);
        var all = await PageAllAsync(samPhone, new MediaQuery(), pageSize: 32);
        Assert.Equal(150, all.Count);
        Assert.Equal(150, all.Select(m => m.Id).Distinct().Count());
        Assert.Equal(all.OrderByDescending(m => m.DateTaken).Select(m => m.Id), all.Select(m => m.Id)); // newest first

        Assert.Equal(10, (await PageAllAsync(samPhone, new MediaQuery(Type: MediaTypes.Video))).Count);
        Assert.Equal(140, (await PageAllAsync(samPhone, new MediaQuery(Type: MediaTypes.Photo))).Count);
        Assert.Equal(47, (await PageAllAsync(samPhone, new MediaQuery(OwnerId: 2))).Count); // i % 3 == 0 for i < 140
        var january = await PageAllAsync(samPhone, new MediaQuery(From: new DateTime(2024, 1, 1), To: new DateTime(2024, 2, 1)));
        Assert.Equal(31, january.Count);
        Assert.All(january, m => Assert.Equal(1, m.DateTaken.Month));
        Assert.Equal(70, all.Count(m => m.Latitude != null));
    }

    [Fact]
    public async Task Resumes_large_file_after_connection_drops()
    {
        // Drop the 2nd chunk after the server stored it, and the 3rd before sending: both must resume without corrupting the file.
        var putCount = 0;
        var api = await _f.PairAsync(wrap: inner => new FaultHandler(inner, (req, _) =>
            req.Method != HttpMethod.Put || !req.RequestUri!.AbsolutePath.StartsWith("/api/uploads") ? Fault.None
            : Interlocked.Increment(ref putCount) switch { 2 => Fault.DropAfterSend, 3 => Fault.DropBeforeSend, _ => Fault.None }));
        var queue = NewQueue();
        var data = Fixtures.RandomBytes(20 * 1024 * 1024 + 123);
        await EnqueueAsync(queue, "big.mp4", data, "video/mp4", 1);

        await new UploadEngine(api, queue, Online, NoWait).RunAsync(CancellationToken.None);

        var item = (await queue.AllAsync()).Single();
        Assert.Equal(UploadStatus.Done, item.Status);
        Assert.Equal(2, item.Attempts);
        Assert.Equal(data, await _f.CreateClient().GetByteArrayAsync(api.OriginalUrl(item.MediaId!.Value)));
    }

    [Fact]
    public async Task Server_down_marks_item_failed_after_max_attempts_then_manual_retry_succeeds()
    {
        var serverDown = true;
        var api = await _f.PairAsync(wrap: inner => new FaultHandler(inner, (req, _) =>
            serverDown && req.RequestUri!.AbsolutePath.StartsWith("/api/uploads") ? Fault.ServiceUnavailable : Fault.None));
        var queue = NewQueue();
        var waits = new List<TimeSpan>();
        await EnqueueAsync(queue, "a.jpg", Fixtures.Jpeg(1, DateTime.Now), "image/jpeg", 1);
        var engine = new UploadEngine(api, queue, Online, (t, _) => { waits.Add(t); return Task.CompletedTask; });

        await engine.RunAsync(CancellationToken.None);
        var item = (await queue.AllAsync()).Single();
        Assert.Equal(UploadStatus.Failed, item.Status);
        Assert.Equal(UploadEngine.MaxAttempts, item.Attempts);
        Assert.Equal([2, 4, 8, 16], waits.Select(w => w.TotalSeconds));

        serverDown = false;
        await queue.RetryFailedAsync();
        await engine.RunAsync(CancellationToken.None);
        Assert.Equal(UploadStatus.Done, (await queue.AllAsync()).Single().Status);
    }

    [Fact]
    public async Task Reupload_of_existing_file_is_reported_as_duplicate()
    {
        var api = await _f.PairAsync();
        var jpeg = Fixtures.Jpeg(42, DateTime.Now);
        var queue = NewQueue();
        await EnqueueAsync(queue, "one.jpg", jpeg, "image/jpeg", 1);
        await EnqueueAsync(queue, "same.jpg", jpeg, "image/jpeg", 1);

        await new UploadEngine(api, queue, Online, NoWait).RunAsync(CancellationToken.None);

        var items = (await queue.AllAsync()).OrderBy(i => i.Id).ToList();
        Assert.Equal([false, true], items.Select(i => i.WasDuplicate));
        Assert.Equal(items[0].MediaId, items[1].MediaId);
    }

    static async Task<List<MediaDto>> PageAllAsync(LoreApiClient api, MediaQuery q, int pageSize = 60)
    {
        var all = new List<MediaDto>();
        string? cursor = null;
        do
        {
            var page = await api.GetMediaAsync(q, cursor, pageSize);
            all.AddRange(page.Items);
            cursor = page.NextCursor;
        } while (cursor != null);
        return all;
    }
}

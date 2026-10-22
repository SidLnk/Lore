using Lore.Client;

namespace Lore.App.Services;

/// <summary>
/// App-side upload entry point: queues files and kicks the background service.
/// Gallery items are read in place by content URI; system-picker files are copied into app storage first.
/// </summary>
public sealed class Uploads(Session session)
{
	public UploadQueue Queue { get; } = new(Path.Combine(FileSystem.AppDataDirectory, "uploads.db"));
	static string PendingDir => Directory.CreateDirectory(Path.Combine(FileSystem.AppDataDirectory, "pending")).FullName;

	/// <summary>Lore only talks to the home server, so it only uploads on Wi-Fi or Ethernet.</summary>
	public static bool OnLan => Connectivity.Current.ConnectionProfiles.Any(p => p is ConnectionProfile.WiFi or ConnectionProfile.Ethernet);

	public static async Task WaitForLanAsync(CancellationToken ct)
	{
		// shortcut: polls every 5 s; switch to ConnectivityChanged if battery use shows up.
		while (!OnLan) await Task.Delay(TimeSpan.FromSeconds(5), ct);
	}

#if ANDROID
	/// <summary>Queues phone media without copying; the original (with GPS) is streamed at upload time.</summary>
	public async Task EnqueueAsync(IEnumerable<Droid.DeviceMedia> media, int ownerId)
	{
		foreach (var m in media)
		{
			await Queue.AddAsync(new QueueItem
			{
				FilePath = m.ContentUri.ToString()!,
				FileName = m.Name,
				MimeType = m.MimeType is { } mt && (mt.StartsWith("image/") || mt.StartsWith("video/")) ? mt : MimeFromExtension(m.Name),
				Size = m.Size,
				OwnerId = ownerId,
				ClientDateTaken = m.Taken,
			});
		}
		Start();
	}
#endif

	/// <summary>Fallback path via the system picker. Android redacts GPS from these copies.</summary>
	public async Task EnqueueAsync(IEnumerable<FileResult> files, int ownerId)
	{
		foreach (var f in files)
		{
			// The picker's copy lives in the cache dir, which Android may purge before the upload runs.
			var dest = Path.Combine(PendingDir, Guid.NewGuid().ToString("N") + Path.GetExtension(f.FileName));
			if (File.Exists(f.FullPath) && f.FullPath.StartsWith(FileSystem.CacheDirectory))
				File.Move(f.FullPath, dest);
			else
			{
				await using var src = await f.OpenReadAsync();
				await using var dst = File.Create(dest);
				await src.CopyToAsync(dst);
			}

			await Queue.AddAsync(new QueueItem
			{
				FilePath = dest,
				FileName = f.FileName,
				MimeType = f.ContentType is { } ct && (ct.StartsWith("image/") || ct.StartsWith("video/")) ? ct : MimeFromExtension(f.FileName),
				Size = new FileInfo(dest).Length,
				OwnerId = ownerId,
			});
		}
		Start();
	}

	static string MimeFromExtension(string name) => Path.GetExtension(name).ToLowerInvariant() switch
	{
		".png" => "image/png",
		".heic" or ".heif" => "image/heic",
		".webp" => "image/webp",
		".gif" => "image/gif",
		".mp4" or ".m4v" => "video/mp4",
		".mov" => "video/quicktime",
		".3gp" => "video/3gpp",
		".webm" => "video/webm",
		".mkv" => "video/x-matroska",
		_ => "image/jpeg",
	};

	public UploadEngine CreateEngine() => new(session.Api!, Queue, WaitForLanAsync, openRead: OpenRead, discard: Discard);

	static Stream OpenRead(string path) =>
#if ANDROID
		path.StartsWith("content://") ? Droid.DeviceGallery.OpenOriginal(path) :
#endif
		File.OpenRead(path);

	/// <summary>Deletes only Lore's own copies, never the user's gallery items.</summary>
	static void Discard(string path)
	{
		if (path.StartsWith(PendingDir)) File.Delete(path);
	}

	public void Start()
	{
		if (!session.IsPaired) return;
#if ANDROID
		Droid.UploadService.Start();
#endif
	}
}

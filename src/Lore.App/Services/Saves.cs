using System.Collections.Concurrent;
using Lore.Client;
using Lore.Contracts;

namespace Lore.App.Services;

/// <summary>
/// "Save to phone": downloads originals into the phone's gallery, drained by the background service after uploads.
/// The queue is in memory on purpose: a file already on the phone is skipped, so running Save again after the app
/// was killed only fetches what's missing.
/// </summary>
public sealed class Saves(Session session)
{
	readonly ConcurrentQueue<MediaDto> _queue = new();
	int _total, _done, _saved, _already, _failed;

	public bool HasWork => !_queue.IsEmpty;

	public async Task EnqueueAsync(IReadOnlyList<MediaDto> items)
	{
		if (items.Count == 0) return;
#if ANDROID
		// Read access lets the duplicate check see the whole gallery; without it only Lore's own saves are visible.
		await Permissions.RequestAsync<Droid.DeviceGallery.MediaPermission>();
		// Progress and the "Saved 3 · 2 already on this phone" summary come as notifications.
		await Permissions.RequestAsync<Permissions.PostNotifications>();
		foreach (var m in items) _queue.Enqueue(m);
		Interlocked.Add(ref _total, items.Count);
		Droid.UploadService.Start();
#else
		await Task.CompletedTask;
#endif
	}

#if ANDROID
	/// <param name="progress">Called before each item with its file name (null while waiting for Wi-Fi), items done, total.</param>
	/// <returns>A summary of the batch, or null when there was nothing to save.</returns>
	public async Task<string?> DrainAsync(Action<string?, int, int> progress, CancellationToken ct)
	{
		if (_queue.IsEmpty) return null;
		while (_queue.TryPeek(out var m))
		{
			for (var attempt = 1; ; attempt++)
			{
				if (!Uploads.OnLan) progress(null, _done, _total);
				await Uploads.WaitForLanAsync(ct);
				progress(m.OriginalFilename, _done, _total);
				try
				{
					await SaveOneAsync(m, ct);
					break;
				}
				// An HTTP timeout is a TaskCanceledException too; only our own token means "stop".
				catch (Exception e) when ((e is HttpRequestException or IOException || e is TaskCanceledException && !ct.IsCancellationRequested) && attempt < 3)
				{
					await Task.Delay(TimeSpan.FromSeconds(2 * attempt), ct); // Wi-Fi blip: try again
				}
				catch (Exception e) when (e is not OperationCanceledException || !ct.IsCancellationRequested)
				{
					Android.Util.Log.Warn("Lore", $"Save to phone failed for {m.OriginalFilename}: {e.Message}");
					_failed++;
					break;
				}
			}
			_done++;
			_queue.TryDequeue(out _);
		}

		string?[] parts =
		[
			_saved > 0 ? $"Saved {Format.Plural(_saved, "item")} to Pictures/Lore or Movies/Lore" : null,
			_already > 0 ? $"{_already:N0} already on this phone" : null,
			_failed > 0 ? $"{_failed:N0} failed (try again on home Wi-Fi)" : null,
		];
		_total = _done = _saved = _already = _failed = 0;
		return string.Join(" · ", parts.Where(p => p != null));
	}

	async Task SaveOneAsync(MediaDto m, CancellationToken ct)
	{
		if (await Task.Run(() => Droid.DeviceGallery.FindOriginal(m.FileSize, m.Sha256), ct) != null)
		{
			_already++;
			return;
		}
		var api = session.Api ?? throw new InvalidOperationException("This phone isn't paired");
		await using var source = await api.OpenOriginalAsync(m.Id, ct);
		await Droid.DeviceGallery.SaveAsync(m, source, ct);
		_saved++;
	}
#endif
}

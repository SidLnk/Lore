using Lore.Client;

namespace Lore.App.Services;

/// <summary>
/// Knows which gallery files are already safe in Lore, so the family can free space on the phone.
/// The record (UploadQueue.InLoreItem) is only a hint: freeing re-hashes each file and re-asks the server first.
/// </summary>
public sealed class FreeUp(Session session, Uploads uploads)
{
	const int Batch = 500;
	int _checkRequested;

	public bool HasWork => Volatile.Read(ref _checkRequested) == 1;

	/// <summary>Gallery files recorded as in Lore that are still on the phone, unchanged in size.</summary>
	public async Task<List<InLoreItem>> CandidatesAsync()
	{
		var records = await uploads.Queue.InLoreAsync();
#if ANDROID
		return await Task.Run(() => records.Where(r => Droid.DeviceGallery.CurrentSize(r.ContentUri) == r.Size).ToList());
#else
		return [];
#endif
	}

#if ANDROID
	/// <summary>Starts "Check this phone" in the background service.</summary>
	public async Task RequestCheckAsync()
	{
		await Permissions.RequestAsync<Droid.DeviceGallery.MediaPermission>();
		await Permissions.RequestAsync<Permissions.PostNotifications>();
		Interlocked.Exchange(ref _checkRequested, 1);
		Droid.UploadService.Start();
	}

	/// <summary>
	/// Fingerprints every gallery photo and video not yet known to be in Lore and asks the server which it has.
	/// Runs in the background service. Progress gets (done, total); returns a summary, or null when not requested.
	/// </summary>
	public async Task<string?> CheckPhoneAsync(Action<int, int> progress, CancellationToken ct)
	{
		if (Interlocked.Exchange(ref _checkRequested, 0) == 0) return null;
		var api = session.Api ?? throw new InvalidOperationException("This phone isn't paired");
		var known = (await uploads.Queue.InLoreAsync()).ToDictionary(r => r.ContentUri, r => r.Size);

		var media = new List<Droid.DeviceMedia>();
		for (List<Droid.DeviceMedia> page; (page = Droid.DeviceGallery.Query(media.Count, 1000)).Count > 0;) media.AddRange(page);
		var todo = media.Where(m => !known.TryGetValue(m.ContentUri.ToString()!, out var size) || size != m.Size).ToList();

		int done = 0, found = 0;
		foreach (var chunk in todo.Chunk(Batch))
		{
			var hashed = new List<(Droid.DeviceMedia Media, string Sha)>();
			foreach (var m in chunk)
			{
				progress(done++, todo.Count);
				if (await Task.Run(() => Droid.DeviceGallery.Hash(m.ContentUri.ToString()!), ct) is { } sha) hashed.Add((m, sha));
			}
			await Uploads.WaitForLanAsync(ct);
			var present = (await api.ExistsAsync([.. hashed.Select(h => h.Sha)], ct)).Present.ToHashSet(StringComparer.OrdinalIgnoreCase);
			foreach (var (m, sha) in hashed.Where(h => present.Contains(h.Sha)))
			{
				await uploads.Queue.MarkInLoreAsync(m.ContentUri.ToString()!, sha, m.Size);
				found++;
			}
		}
		var inLore = media.Count - todo.Count + found;
		return $"Checked {media.Count:N0} photos and videos on this phone: {inLore:N0} are safe in Lore." +
			(inLore > 0 ? " Free up space on the Upload tab." : "");
	}

	/// <summary>
	/// Re-hashes each candidate, keeps only files whose exact bytes the server still holds outside the recycle bin,
	/// then asks Android to remove them (trash on Android 11+). Progress gets (done, total) while checking.
	/// </summary>
	/// <returns>Items and bytes removed; null when the user declined at the system prompt.</returns>
	public async Task<(int Count, long Bytes)?> FreeAsync(IReadOnlyList<InLoreItem> candidates, Action<int, int> progress, CancellationToken ct)
	{
		var api = session.Api ?? throw new InvalidOperationException("This phone isn't paired");
		var verified = new List<InLoreItem>();
		var done = 0;
		foreach (var chunk in candidates.Chunk(Batch))
		{
			var same = new List<InLoreItem>();
			foreach (var c in chunk)
			{
				progress(done++, candidates.Count);
				// Edited since upload? Then the phone copy is different from Lore's, so it isn't safe to remove.
				var sha = await Task.Run(() => Droid.DeviceGallery.Hash(c.ContentUri), ct);
				if (sha != null && sha.Equals(c.Sha256, StringComparison.OrdinalIgnoreCase)) same.Add(c);
				else await uploads.Queue.ForgetInLoreAsync(c.ContentUri);
			}
			var present = (await api.ExistsAsync([.. same.Select(s => s.Sha256)], ct)).Present.ToHashSet(StringComparer.OrdinalIgnoreCase);
			verified.AddRange(same.Where(s => present.Contains(s.Sha256)));
		}
		if (verified.Count == 0) return (0, 0);
		if (!await Droid.DeviceGallery.RemoveAsync([.. verified.Select(v => v.ContentUri)])) return null;
		return (verified.Count, verified.Sum(v => v.Size));
	}
#endif
}

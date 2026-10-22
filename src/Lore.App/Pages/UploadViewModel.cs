using System.Collections.ObjectModel;
using CommunityToolkit.Maui.Alerts;
using CommunityToolkit.Maui.Core;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lore.App.Services;
using Lore.Client;

namespace Lore.App.Pages;

public sealed partial class QueueRow(int id) : ObservableObject
{
	public int Id { get; } = id;
	[ObservableProperty] public partial string Name { get; set; } = "";
	[ObservableProperty] public partial string Detail { get; set; } = "";
	[ObservableProperty] public partial double Progress { get; set; }
	[ObservableProperty] public partial bool IsFailed { get; set; }
	/// <summary>Full reason, shown on its own line only when the upload failed.</summary>
	[ObservableProperty] public partial string? ErrorText { get; set; }

	public void Update(QueueItem i)
	{
		Name = i.FileName;
		Progress = i.Size == 0 ? 0 : (double)i.Offset / i.Size;
		IsFailed = i.Status == UploadStatus.Failed;
		ErrorText = IsFailed ? i.Error : null;
		// Short, so it fits beside the filename.
		Detail = i.Status switch
		{
			UploadStatus.Done when i.WasDuplicate => "Already in Lore",
			UploadStatus.Done => "Uploaded",
			UploadStatus.Failed => "Failed",
			UploadStatus.Uploading => $"{Progress:P0} · {i.Size / 1048576.0:0.#} MB",
			_ when i.Error != null => $"Retry {i.Attempts}/{UploadEngine.MaxAttempts}",
			_ => "Waiting",
		};
	}
}

public sealed partial class UploadViewModel : ObservableObject
{
	readonly Session _session;
	readonly Uploads _uploads;
	readonly FreeUp _freeUp;
	readonly Dictionary<int, QueueRow> _rows = [];

	public UploadViewModel(Session session, Uploads uploads, FreeUp freeUp)
	{
		_session = session;
		_uploads = uploads;
		_freeUp = freeUp;
		Owners = session.Family.Select(f => new OwnerOption(f.Id, f.Name)).ToList();
		SelectedOwner = Owners.FirstOrDefault(o => o.Id == session.DefaultOwnerId) ?? Owners.FirstOrDefault();
	}

	public ObservableCollection<QueueRow> Rows { get; } = [];
	public IReadOnlyList<OwnerOption> Owners { get; }

	[ObservableProperty] public partial OwnerOption? SelectedOwner { get; set; }
	[ObservableProperty] public partial string Summary { get; set; } = "";
	[ObservableProperty] public partial bool HasFailed { get; set; }
	[ObservableProperty] public partial string FreeUpText { get; set; } = "";
	[ObservableProperty] public partial bool CanFreeUp { get; set; }
	[ObservableProperty] public partial bool IsFreeingUp { get; set; }

	/// <summary>How much of this phone's gallery is already safe in Lore. Queries MediaStore, so not on every poll.</summary>
	public async Task RefreshFreeUpAsync()
	{
		if (IsFreeingUp) return;
		var candidates = await _freeUp.CandidatesAsync();
		CanFreeUp = candidates.Count > 0;
		FreeUpText = CanFreeUp
			? $"{Format.Bytes(candidates.Sum(c => c.Size))} on this phone ({Format.Plural(candidates.Count, "item")}) is already safe in Lore."
			: "Photos you upload from this phone are marked In Lore. Check this phone to find ones uploaded earlier or from another phone.";
	}

	[RelayCommand]
	async Task FreeUpAsync()
	{
#if ANDROID
		if (!Uploads.OnLan)
		{
			await Shell.Current.DisplayAlertAsync("Free up space", "Connect to home Wi-Fi first, so Lore can confirm it has every file.", "OK");
			return;
		}
		var candidates = await _freeUp.CandidatesAsync();
		if (candidates.Count == 0) return;
		var trash = OperatingSystem.IsAndroidVersionAtLeast(30);
		// Android 11+ asks itself (and keeps them in the trash for 30 days); older Android deletes, so ask first.
		if (!trash && !await Shell.Current.DisplayAlertAsync("Free up space?",
			$"Delete {Format.Plural(candidates.Count, "photo and video")} ({Format.Bytes(candidates.Sum(c => c.Size))}) from this phone? " +
			"Lore keeps the originals on the home PC. This can't be undone on the phone.", "Delete", "Cancel")) return;

		IsFreeingUp = true;
		try
		{
			var result = await _freeUp.FreeAsync(candidates, (done, total) =>
				MainThread.BeginInvokeOnMainThread(() => FreeUpText = $"Checking each file against Lore… {done + 1:N0} of {total:N0}"), CancellationToken.None);
			var message = result switch
			{
				null => "Nothing was removed.",
				(0, _) => "Nothing to free: those files changed on the phone or aren't in Lore any more.",
				var (n, bytes) when trash => $"Moved {Format.Plural(n, "item")} ({Format.Bytes(bytes)}) to the phone's trash. Android empties it after 30 days; Lore keeps the originals.",
				var (n, bytes) => $"Deleted {Format.Plural(n, "item")} ({Format.Bytes(bytes)}) from this phone. Lore keeps the originals.",
			};
			await Toast.Make(message, ToastDuration.Long).Show();
		}
		catch (Exception e) when (e is HttpRequestException or TaskCanceledException or LoreApiException)
		{
			await Shell.Current.DisplayAlertAsync("Free up space", "Couldn't reach Lore, so nothing was removed. Are you on home Wi-Fi?", "OK");
		}
		finally
		{
			IsFreeingUp = false;
			await RefreshFreeUpAsync();
		}
#else
		await Task.CompletedTask;
#endif
	}

	[RelayCommand]
	async Task CheckPhoneAsync()
	{
#if ANDROID
		await _freeUp.RequestCheckAsync();
		await Toast.Make("Checking this phone in the background. A notification says when it's done.", ToastDuration.Long).Show();
#else
		await Task.CompletedTask;
#endif
	}

	/// <summary>Raised when new queue rows appear at the top, so the page can scroll to them.</summary>
	public event Action? RowsAddedAtTop;

	[RelayCommand] Task ChooseFromPhoneAsync() => Shell.Current.GoToAsync("gallery");

	/// <summary>Fallback when Lore isn't allowed to read the gallery. Android strips location from these copies.</summary>
	[RelayCommand]
	async Task SystemPickerAsync()
	{
		var kind = await Shell.Current.DisplayActionSheetAsync("System picker removes photo locations", "Cancel", null, "Photos", "Videos");
		if (kind == "Photos") await PickAsync(MediaPicker.Default.PickPhotosAsync(new MediaPickerOptions { SelectionLimit = 0 }));
		else if (kind == "Videos") await PickAsync(MediaPicker.Default.PickVideosAsync(new MediaPickerOptions { SelectionLimit = 0 }));
	}

	async Task PickAsync(Task<List<FileResult>> pick)
	{
		if (SelectedOwner?.Id is not { } owner) return;
		// Progress notification on Android 13+; uploads still run if the user says no.
		await Permissions.RequestAsync<Permissions.PostNotifications>();
		var files = await pick;
		if (files is { Count: > 0 }) await _uploads.EnqueueAsync(files, owner);
		await RefreshAsync();
	}

	[RelayCommand]
	async Task RetryFailedAsync()
	{
		await _uploads.Queue.RetryFailedAsync();
		_uploads.Start();
		await RefreshAsync();
	}

	[RelayCommand]
	async Task ClearDoneAsync()
	{
		await _uploads.Queue.ClearDoneAsync();
		await RefreshAsync();
	}

	public async Task RefreshAsync()
	{
		var items = await _uploads.Queue.AllAsync();
		var ids = items.Select(i => i.Id).ToHashSet();
		foreach (var gone in _rows.Keys.Where(k => !ids.Contains(k)).ToList())
		{
			Rows.Remove(_rows[gone]);
			_rows.Remove(gone);
		}
		var addedAtTop = false;
		for (var n = 0; n < items.Count; n++)
		{
			var item = items[n];
			if (!_rows.TryGetValue(item.Id, out var row))
			{
				_rows[item.Id] = row = new QueueRow(item.Id);
				Rows.Insert(Math.Min(n, Rows.Count), row);
				addedAtTop |= n == 0;
			}
			row.Update(item);
		}
		if (addedAtTop) RowsAddedAtTop?.Invoke();

		int Count(UploadStatus s) => items.Count(i => i.Status == s);
		var left = Count(UploadStatus.Pending) + Count(UploadStatus.Uploading);
		HasFailed = Count(UploadStatus.Failed) > 0;
		Summary = left > 0 && !Uploads.OnLan ? $"{left} waiting for home Wi-Fi"
			: left > 0 ? $"{left} to upload"
			: items.Count > 0 ? $"All done · {Count(UploadStatus.Done)} uploaded" + (HasFailed ? $" · {Count(UploadStatus.Failed)} failed" : "")
			: "Pick photos or videos to upload.";
	}
}

using System.Collections.ObjectModel;
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

	public void Update(QueueItem i)
	{
		Name = i.FileName;
		Progress = i.Size == 0 ? 0 : (double)i.Offset / i.Size;
		IsFailed = i.Status == UploadStatus.Failed;
		Detail = i.Status switch
		{
			UploadStatus.Done when i.WasDuplicate => "Already in Lore",
			UploadStatus.Done => "Uploaded",
			UploadStatus.Failed => "Failed: " + i.Error,
			UploadStatus.Uploading => $"{Progress:P0} of {i.Size / 1048576.0:0.#} MB",
			_ when i.Error != null => $"Waiting to retry ({i.Attempts}/{UploadEngine.MaxAttempts})",
			_ => "Waiting",
		};
	}
}

public sealed partial class UploadViewModel : ObservableObject
{
	readonly Session _session;
	readonly Uploads _uploads;
	readonly Dictionary<int, QueueRow> _rows = [];

	public UploadViewModel(Session session, Uploads uploads)
	{
		_session = session;
		_uploads = uploads;
		Owners = session.Family.Select(f => new OwnerOption(f.Id, f.Name)).ToList();
		SelectedOwner = Owners.FirstOrDefault(o => o.Id == session.DefaultOwnerId) ?? Owners.FirstOrDefault();
	}

	public ObservableCollection<QueueRow> Rows { get; } = [];
	public IReadOnlyList<OwnerOption> Owners { get; }

	[ObservableProperty] public partial OwnerOption? SelectedOwner { get; set; }
	[ObservableProperty] public partial string Summary { get; set; } = "";
	[ObservableProperty] public partial bool HasFailed { get; set; }

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

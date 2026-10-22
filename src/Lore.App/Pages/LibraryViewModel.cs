using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lore.App.Services;
using Lore.Client;
using Lore.Contracts;

namespace Lore.App.Pages;

public sealed record OwnerOption(int? Id, string Name)
{
	public override string ToString() => Name;
}

/// <summary>A picker entry; Value null means "any".</summary>
public sealed record FilterOption(string Label, object? Value)
{
	public override string ToString() => Label;
}

public sealed partial class MediaItem(MediaDto dto, LoreApiClient api) : ObservableObject
{
	public MediaDto Dto { get; } = dto;
	public bool Favorite { get; set; } = dto.Favorite;
	public Uri ThumbUrl { get; } = api.ThumbUrl(dto.Id);
	public Uri PreviewUrl { get; } = api.PreviewUrl(dto.Id);
	public Uri OriginalUrl { get; } = api.OriginalUrl(dto.Id);
	public bool IsVideo => Dto.MediaType == MediaTypes.Video;
	public string DurationText => Dto.Duration is { } d ? TimeSpan.FromSeconds(d).ToString(d >= 3600 ? @"h\:mm\:ss" : @"m\:ss") : "";
	public bool HasDuration => Dto.Duration != null;
	[ObservableProperty] public partial bool IsSelected { get; set; }
	/// <summary>Library is in select mode: show the empty selection ring on every tile.</summary>
	[ObservableProperty] public partial bool IsSelecting { get; set; }
}

public sealed class MonthGroup(DateTime month) : ObservableCollection<MediaItem>
{
	public DateTime Month { get; } = month;
	public string Title => Month.ToString("MMMM yyyy");
}

/// <summary>Shared between the lists and the fullscreen viewer.</summary>
public sealed class ViewerState
{
	public List<MediaItem> Items { get; set; } = [];
	public int Index { get; set; }
	/// <summary>True when the viewer was opened from the recycle bin (offers Restore instead of Delete).</summary>
	public bool FromBin { get; set; }
	/// <summary>Bumped whenever media change; each list reloads when it sees a newer version.</summary>
	public int Version { get; set; }
}

public partial class LibraryViewModel : ObservableObject
{
	const int PageSize = 90;
	// Distinct "any" labels so an unset picker still says what it filters.
	static readonly FilterOption AnyFolder = new("Any folder", null), AnyAlbum = new("Any album", null),
		AnyTag = new("Any tag", null), AnyDevice = new("Any device", null), AnyLocation = new("Any location", null);
	readonly Session _session;
	readonly ViewerState _viewer;
	readonly Organizer _organizer;
	string? _cursor;
	bool _end, _loading, _suspendReload;
	int _generation;

	public LibraryViewModel(Session session, ViewerState viewer, Organizer organizer) : this(session, viewer, organizer, bin: false) { }

	protected LibraryViewModel(Session session, ViewerState viewer, Organizer organizer, bool bin)
	{
		_session = session;
		_viewer = viewer;
		_organizer = organizer;
		IsBin = bin;
		Owners = [new OwnerOption(null, "Everyone"), .. session.Family.Select(f => new OwnerOption(f.Id, f.Name))];
		SelectedOwner = Owners[0];
		(SelectedFolder, SelectedAlbum, SelectedTag, SelectedDevice, SelectedGps) = (AnyFolder, AnyAlbum, AnyTag, AnyDevice, AnyLocation);
	}

	protected Session Session => _session;
	public bool IsBin { get; }
	public bool ShowFilters => !IsBin;
	public ObservableCollection<MonthGroup> Groups { get; } = [];
	public IReadOnlyList<OwnerOption> Owners { get; }
	public IReadOnlyList<string> Types { get; } = ["All", "Photos", "Videos"];
	public ObservableCollection<FilterOption> Folders { get; } = [AnyFolder];
	public ObservableCollection<FilterOption> Albums { get; } = [AnyAlbum];
	public ObservableCollection<FilterOption> Tags { get; } = [AnyTag];
	public ObservableCollection<FilterOption> Devices { get; } = [AnyDevice];
	public IReadOnlyList<FilterOption> GpsOptions { get; } = [AnyLocation, new("With location", true), new("Without location", false)];

	[ObservableProperty] public partial OwnerOption SelectedOwner { get; set; }
	[ObservableProperty] public partial int TypeIndex { get; set; }
	[ObservableProperty] public partial bool UseDateRange { get; set; }
	[ObservableProperty] public partial bool FavoritesOnly { get; set; }
	[ObservableProperty] public partial DateTime FromDate { get; set; } = DateTime.Today.AddMonths(-1);
	[ObservableProperty] public partial DateTime ToDate { get; set; } = DateTime.Today;
	[ObservableProperty] public partial FilterOption SelectedFolder { get; set; }
	[ObservableProperty] public partial FilterOption SelectedAlbum { get; set; }
	[ObservableProperty] public partial FilterOption SelectedTag { get; set; }
	[ObservableProperty] public partial FilterOption SelectedDevice { get; set; }
	[ObservableProperty] public partial FilterOption SelectedGps { get; set; }
	/// <summary>Shows the from/to date pickers under the chips when "Choose dates…" was picked.</summary>
	[ObservableProperty] public partial bool ShowCustomDates { get; set; }
	[ObservableProperty] public partial bool IsRefreshing { get; set; }
	[ObservableProperty] public partial string? Message { get; set; }
	[ObservableProperty] public partial bool IsSelecting { get; set; }
	[ObservableProperty] public partial int SelectedCount { get; set; }
	/// <summary>Set from Places; shown as a removable chip rather than a picker.</summary>
	[ObservableProperty] public partial GeoArea? Area { get; set; }
	[ObservableProperty] public partial string? AreaLabel { get; set; }

	partial void OnSelectedOwnerChanged(OwnerOption oldValue, OwnerOption newValue) { if (oldValue is not null) FilterChanged(); }
	partial void OnTypeIndexChanged(int value) => FilterChanged();
	partial void OnUseDateRangeChanged(bool value) => FilterChanged();
	partial void OnFavoritesOnlyChanged(bool value) => FilterChanged();
	partial void OnFromDateChanged(DateTime value) { if (UseDateRange) FilterChanged(); }
	partial void OnToDateChanged(DateTime value) { if (UseDateRange) FilterChanged(); }
	// Pickers briefly set SelectedItem to null while their list is rebuilt; ignore that.
	partial void OnSelectedFolderChanged(FilterOption oldValue, FilterOption newValue) { if (oldValue is not null && newValue is not null) FilterChanged(); }
	partial void OnSelectedAlbumChanged(FilterOption oldValue, FilterOption newValue) { if (oldValue is not null && newValue is not null) FilterChanged(); }
	partial void OnSelectedTagChanged(FilterOption oldValue, FilterOption newValue) { if (oldValue is not null && newValue is not null) FilterChanged(); }
	partial void OnSelectedDeviceChanged(FilterOption oldValue, FilterOption newValue) { if (oldValue is not null && newValue is not null) FilterChanged(); }
	partial void OnSelectedGpsChanged(FilterOption oldValue, FilterOption newValue) { if (oldValue is not null && newValue is not null) FilterChanged(); }

	void FilterChanged()
	{
		NotifyChips();
		if (!_suspendReload) _ = ReloadAsync();
	}

	// ---- Filter chips: one horizontal row instead of a stack of pickers, so photos start near the top. ----

	string _datePreset = "Any time";

	public string OwnerChip => SelectedOwner.Id == null ? "Everyone" : SelectedOwner.Name;
	public bool OwnerActive => SelectedOwner.Id != null;
	public string TypeChip => TypeIndex switch { 1 => "Photos", 2 => "Videos", _ => "Photos & videos" };
	public bool TypeActive => TypeIndex != 0;
	public string DateChip => !UseDateRange ? "Any time" : ShowCustomDates ? $"{FromDate:d MMM} – {ToDate:d MMM yyyy}" : _datePreset;
	public string FolderChip => SelectedFolder.Value == null ? "Folder" : SelectedFolder.Label;
	public string AlbumChip => SelectedAlbum.Value == null ? "Album" : SelectedAlbum.Label;
	public string TagChip => SelectedTag.Value == null ? "Tag" : SelectedTag.Label;
	public string DeviceChip => SelectedDevice.Value == null ? "Device" : SelectedDevice.Label;
	public string LocationChip => SelectedGps.Value == null ? "Location" : SelectedGps.Label;
	public bool FolderActive => SelectedFolder.Value != null;
	public bool AlbumActive => SelectedAlbum.Value != null;
	public bool TagActive => SelectedTag.Value != null;
	public bool DeviceActive => SelectedDevice.Value != null;
	public bool LocationActive => SelectedGps.Value != null;
	public bool HasArea => Area != null;
	public bool AnyActive => OwnerActive || TypeActive || UseDateRange || FavoritesOnly || FolderActive || AlbumActive
		|| TagActive || DeviceActive || LocationActive || HasArea;

	static readonly string[] ChipProperties =
	[
		nameof(OwnerChip), nameof(OwnerActive), nameof(TypeChip), nameof(TypeActive), nameof(DateChip),
		nameof(FolderChip), nameof(AlbumChip), nameof(TagChip), nameof(DeviceChip), nameof(LocationChip),
		nameof(FolderActive), nameof(AlbumActive), nameof(TagActive), nameof(DeviceActive), nameof(LocationActive),
		nameof(HasArea), nameof(AnyActive),
	];

	void NotifyChips()
	{
		foreach (var name in ChipProperties) OnPropertyChanged(name);
	}

	static Task<string?> SheetAsync(string title, IEnumerable<string> options) =>
		Shell.Current.DisplayActionSheetAsync(title, "Cancel", null, [.. options]);

	[RelayCommand]
	async Task PickOwnerAsync()
	{
		var choice = await SheetAsync("Whose photos", Owners.Select(o => o.Name));
		if (Owners.FirstOrDefault(o => o.Name == choice) is { } owner) SelectedOwner = owner;
	}

	[RelayCommand]
	async Task PickTypeAsync()
	{
		string[] options = ["Photos & videos", "Photos", "Videos"];
		var index = Array.IndexOf(options, await SheetAsync("Show", options));
		if (index >= 0) TypeIndex = index;
	}

	[RelayCommand]
	async Task PickDatesAsync()
	{
		var today = DateTime.Today;
		(string Label, DateTime From, DateTime To)[] presets =
		[
			("This month", new DateTime(today.Year, today.Month, 1), today),
			("Last 3 months", today.AddMonths(-3), today),
			("This year", new DateTime(today.Year, 1, 1), today),
			("Last year", new DateTime(today.Year - 1, 1, 1), new DateTime(today.Year - 1, 12, 31)),
		];
		var choice = await SheetAsync("When", ["Any time", .. presets.Select(p => p.Label), "Choose dates…"]);
		if (choice is null or "Cancel") return;

		_suspendReload = true;
		ShowCustomDates = choice == "Choose dates…";
		if (choice == "Any time") UseDateRange = false;
		else if (presets.FirstOrDefault(p => p.Label == choice) is { Label: not null } preset)
		{
			(_datePreset, FromDate, ToDate) = preset;
			UseDateRange = true;
		}
		else UseDateRange = true; // custom: keep the current from/to, now editable below the chips
		_suspendReload = false;
		FilterChanged();
	}

	[RelayCommand]
	void ToggleFavorites() => FavoritesOnly = !FavoritesOnly;

	[RelayCommand] Task PickFolderAsync() => PickOptionAsync("Folder", Folders, o => SelectedFolder = o);
	[RelayCommand] Task PickAlbumAsync() => PickOptionAsync("Album", Albums, o => SelectedAlbum = o);
	[RelayCommand] Task PickTagAsync() => PickOptionAsync("Tag", Tags, o => SelectedTag = o);
	[RelayCommand] Task PickDeviceAsync() => PickOptionAsync("Uploaded from", Devices, o => SelectedDevice = o);

	[RelayCommand]
	async Task PickLocationAsync()
	{
		var choice = await SheetAsync("Location", GpsOptions.Select(o => o.Label));
		if (GpsOptions.FirstOrDefault(o => o.Label == choice) is { } option) SelectedGps = option;
	}

	async Task PickOptionAsync(string title, IReadOnlyList<FilterOption> options, Action<FilterOption> select)
	{
		await LoadFilterOptionsAsync(); // folders, albums and tags change in Organize
		if (options.Count <= 1)
		{
			await Shell.Current.DisplayAlertAsync(title, $"There are no {title.ToLowerInvariant()}s to filter by yet.", "OK");
			return;
		}
		var choice = await SheetAsync(title, options.Select(o => o.Label));
		if (options.FirstOrDefault(o => o.Label == choice) is { } option) select(option);
	}

	[RelayCommand]
	Task ClearFiltersAsync() => ShowOnlyAsync();

	MediaQuery Query => new(
		From: UseDateRange ? FromDate.Date : null,
		To: UseDateRange ? ToDate.Date.AddDays(1) : null, // inclusive end day; API "to" is exclusive
		OwnerId: SelectedOwner.Id,
		Type: TypeIndex switch { 1 => MediaTypes.Photo, 2 => MediaTypes.Video, _ => null },
		Favorite: FavoritesOnly,
		Deleted: IsBin,
		DeviceId: SelectedDevice?.Value as Guid?,
		FolderId: SelectedFolder?.Value as int?,
		AlbumId: SelectedAlbum?.Value as int?,
		Tag: SelectedTag?.Value as string,
		HasGps: SelectedGps?.Value as bool?,
		Area: Area);

	/// <summary>Opens the library showing one place (from Places).</summary>
	public async Task ShowAreaAsync(GeoArea area, string label)
	{
		await ShowOnlyAsync(reload: false);
		Area = area;
		AreaLabel = label;
		NotifyChips();
		await ReloadAsync();
	}

	[RelayCommand]
	async Task ClearAreaAsync()
	{
		Area = null;
		AreaLabel = null;
		NotifyChips();
		await ReloadAsync();
	}

	/// <summary>Opens the library showing one folder, album or tag (from Organize).</summary>
	public async Task ShowOnlyAsync(int? folderId = null, int? albumId = null, string? tag = null, bool reload = true)
	{
		StopSelecting();
		await LoadFilterOptionsAsync();
		_suspendReload = true;
		(Area, AreaLabel) = (null, null);
		SelectedOwner = Owners[0];
		TypeIndex = 0;
		UseDateRange = FavoritesOnly = ShowCustomDates = false;
		(SelectedDevice, SelectedGps) = (AnyDevice, AnyLocation);
		SelectedFolder = Folders.FirstOrDefault(f => Equals(f.Value, folderId)) ?? AnyFolder;
		SelectedAlbum = Albums.FirstOrDefault(a => Equals(a.Value, albumId)) ?? AnyAlbum;
		SelectedTag = Tags.FirstOrDefault(t => Equals(t.Value, tag)) ?? AnyTag;
		_suspendReload = false;
		NotifyChips();
		if (reload) await ReloadAsync();
	}

	public async Task LoadFilterOptionsAsync()
	{
		if (IsBin || _session.Api is not { } api) return;
		try
		{
			var folders = Organizer.Flatten(await api.GetFoldersAsync()).Select(f => new FilterOption(f.Path, f.Folder.Id));
			var albums = (await api.GetAlbumsAsync()).Select(a => new FilterOption(a.Name, a.Id));
			var tags = (await api.GetTagsAsync()).Select(t => new FilterOption(t.Name, t.Name));
			var devices = (await api.GetDevicesAsync()).Select(d => new FilterOption(d.Name, d.Id));
			_suspendReload = true;
			SelectedFolder = Refill(Folders, AnyFolder, folders, SelectedFolder);
			SelectedAlbum = Refill(Albums, AnyAlbum, albums, SelectedAlbum);
			SelectedTag = Refill(Tags, AnyTag, tags, SelectedTag);
			SelectedDevice = Refill(Devices, AnyDevice, devices, SelectedDevice);
		}
		catch (Exception e) when (e is HttpRequestException or TaskCanceledException or LoreApiException) { }
		finally
		{
			_suspendReload = false;
		}
	}

	static FilterOption Refill(ObservableCollection<FilterOption> list, FilterOption any, IEnumerable<FilterOption> items, FilterOption? selected)
	{
		list.Clear();
		list.Add(any);
		foreach (var i in items) list.Add(i);
		return list.FirstOrDefault(o => Equals(o.Value, selected?.Value)) ?? any;
	}

	[RelayCommand]
	public async Task ReloadAsync()
	{
		_generation++;
		_loading = false;
		Groups.Clear();
		SelectedCount = 0;
		_cursor = null;
		_end = false;
		await LoadMoreAsync();
		IsRefreshing = false;
	}

	[RelayCommand]
	async Task LoadMoreAsync()
	{
		if (_loading || _end || _session.Api is not { } api) return;
		_loading = true;
		var generation = _generation;
		try
		{
			var page = await api.GetMediaAsync(Query, _cursor, PageSize);
			if (generation != _generation) return; // filters changed while this page was loading
			foreach (var dto in page.Items)
			{
				var month = new DateTime(dto.DateTaken.Year, dto.DateTaken.Month, 1);
				if (Groups.LastOrDefault() is not { } group || group.Month != month) Groups.Add(group = new MonthGroup(month));
				group.Add(new MediaItem(dto, api) { IsSelecting = IsSelecting });
			}
			_cursor = page.NextCursor;
			_end = _cursor == null;
			Message = Groups.Count > 0 ? null
				: IsBin ? "The recycle bin is empty. Deleted photos and videos wait here until you empty it."
				: AnyActive ? "Nothing matches these filters. Tap Clear all to see everything."
				: "No photos yet. Add some from the Upload tab.";
			OnPageLoaded();
		}
		catch (Exception e) when (e is HttpRequestException or TaskCanceledException or LoreApiException)
		{
			if (generation == _generation) Message = "Can't reach Lore. Pull down to retry when you're on home Wi-Fi.";
		}
		finally
		{
			if (generation == _generation) _loading = false;
		}
	}

	protected virtual void OnPageLoaded() { }

	/// <summary>Tap: opens the viewer, or toggles the item while selecting.</summary>
	[RelayCommand]
	async Task OpenAsync(MediaItem item)
	{
		if (IsSelecting)
		{
			item.IsSelected = !item.IsSelected;
			SelectedCount += item.IsSelected ? 1 : -1;
			return;
		}
		var all = Groups.SelectMany(g => g).ToList();
		_viewer.Items = all;
		_viewer.Index = all.IndexOf(item);
		_viewer.FromBin = IsBin;
		await Shell.Current.GoToAsync("viewer");
	}

	public void StartSelecting()
	{
		IsSelecting = true;
		foreach (var item in Groups.SelectMany(g => g)) item.IsSelecting = true;
	}

	public void StopSelecting()
	{
		IsSelecting = false;
		foreach (var item in Groups.SelectMany(g => g)) (item.IsSelected, item.IsSelecting) = (false, false);
		SelectedCount = 0;
	}

	public void SelectAll()
	{
		foreach (var item in Groups.SelectMany(g => g)) item.IsSelected = true;
		SelectedCount = Groups.Sum(g => g.Count);
	}

	public async Task EditSelectedAsync(Page page)
	{
		var ids = Groups.SelectMany(g => g).Where(i => i.IsSelected).Select(i => i.Dto.Id).ToList();
		if (!await _organizer.EditAsync(page, ids)) return;
		_viewer.Version++;
		StopSelecting();
		await ReloadAsync();
	}
}

public sealed partial class BinViewModel(Session session, ViewerState viewer, Organizer organizer) : LibraryViewModel(session, viewer, organizer, bin: true)
{
	bool CanEmpty => Groups.Count > 0;

	// Grey out "Empty bin" when there is nothing to erase.
	protected override void OnPageLoaded() => EmptyCommand.NotifyCanExecuteChanged();

	[RelayCommand(CanExecute = nameof(CanEmpty))]
	async Task EmptyAsync()
	{
		if (Groups.Count == 0 || Session.Api is not { } api) return;
		var count = Groups.Sum(g => g.Count);
		if (!await Shell.Current.DisplayAlertAsync("Empty recycle bin?",
			$"{count} item(s) will be erased from the home PC for good. This can't be undone.", "Erase", "Cancel")) return;
		try
		{
			await api.EmptyBinAsync();
		}
		catch (Exception e) when (e is HttpRequestException or TaskCanceledException or LoreApiException)
		{
			Message = "Couldn't reach Lore. Nothing was erased.";
			return;
		}
		await ReloadAsync();
	}
}

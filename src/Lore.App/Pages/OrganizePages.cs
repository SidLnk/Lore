using Lore.App.Services;
using Lore.Client;
using Lore.Contracts;

namespace Lore.App.Pages;

/// <summary>Manage the folder tree, albums and tags; tap any of them to browse it in the library.</summary>
public sealed class OrganizePage : ContentPage
{
	readonly Session _session;
	readonly LibraryViewModel _library;
	readonly ViewerState _viewer;
	readonly VerticalStackLayout _list = new() { Spacing = 2, Padding = new Thickness(16, 8) };

	public OrganizePage(Session session, LibraryViewModel library, ViewerState viewer)
	{
		_session = session;
		_library = library;
		_viewer = viewer;
		Title = "Organize";
		ToolbarItems.Add(new ToolbarItem { Text = "New folder", Command = new Command(async () => await RunAsync(NewFolderAsync(null))) });
		ToolbarItems.Add(new ToolbarItem { Text = "New album", Command = new Command(async () => await RunAsync(NewAlbumAsync())) });
		Content = new ScrollView { Content = _list };
	}

	LoreApiClient Api => _session.Api!;

	protected override async void OnAppearing()
	{
		base.OnAppearing();
		await RunAsync(LoadAsync());
	}

	async Task LoadAsync()
	{
		var folders = Organizer.Flatten(await Api.GetFoldersAsync());
		var albums = await Api.GetAlbumsAsync();
		var tags = await Api.GetTagsAsync();

		_list.Clear();
		Heading("Folders", folders.Count == 0 ? "No folders yet. Folders only organize Lore; files on the PC never move." : null);
		foreach (var (folder, depth, path) in folders)
			Row(folder.Name, folder.Count, depth, () => FolderActionsAsync(folder, path));
		Heading("Albums", albums.Count == 0 ? "No albums yet." : null);
		foreach (var album in albums)
			Row(album.Name, album.Count, 0, () => AlbumActionsAsync(album));
		Heading("Tags", tags.Count == 0 ? "No tags yet. Add them from Library → Select → Edit." : null);
		foreach (var tag in tags)
			Row(tag.Name, tag.Count, 0, () => ShowAsync(tag: tag.Name));
	}

	void Heading(string text, string? empty)
	{
		_list.Add(new Label { Text = text, FontSize = 20, FontFamily = "OpenSansSemibold", Margin = new Thickness(0, 16, 0, 4) });
		if (empty != null) _list.Add(new Label { Text = empty, Opacity = 0.7 });
	}

	void Row(string name, int count, int depth, Func<Task> onTap)
	{
		var row = new Grid
		{
			ColumnDefinitions = [new(GridLength.Star), new(GridLength.Auto)],
			Padding = new Thickness(depth * 20, 10, 0, 10),
			Children =
			{
				new Label { Text = name, FontSize = 16, VerticalOptions = LayoutOptions.Center },
				new Label { Text = count.ToString("N0"), Style = (Style)Application.Current!.Resources["Quiet"], VerticalOptions = LayoutOptions.Center }.Column(1),
			},
		};
		row.GestureRecognizers.Add(new TapGestureRecognizer { Command = new Command(async () => await RunAsync(onTap())) });
		_list.Add(row);
	}

	async Task FolderActionsAsync(FolderDto folder, string path)
	{
		switch (await DisplayActionSheetAsync(path, "Cancel", "Delete", "Show", "New subfolder", "Rename", "Move"))
		{
			case "Show":
				await ShowAsync(folderId: folder.Id);
				return;
			case "New subfolder":
				await NewFolderAsync(folder.Id);
				break;
			case "Rename" when await Prompt("Rename folder", folder.Name) is { } name:
				await Api.UpdateFolderAsync(folder.Id, name, folder.ParentId);
				break;
			case "Move":
				var targets = Organizer.Flatten(await Api.GetFoldersAsync()).Where(f => f.Folder.Id != folder.Id).ToList();
				var choice = await DisplayActionSheetAsync($"Move \"{folder.Name}\" into", "Cancel", null, ["(Top level)", .. targets.Select(t => t.Path)]);
				if (choice == "(Top level)") await Api.UpdateFolderAsync(folder.Id, folder.Name, null);
				else if (targets.FirstOrDefault(t => t.Path == choice) is { Folder: { } target }) await Api.UpdateFolderAsync(folder.Id, folder.Name, target.Id);
				else return;
				break;
			case "Delete" when await DisplayAlertAsync("Delete folder?", $"Its {folder.Count} item(s) stay in the library, just without a folder.", "Delete", "Cancel"):
				await Api.DeleteFolderAsync(folder.Id);
				break;
			default:
				return;
		}
		_viewer.Version++;
		await LoadAsync();
	}

	async Task AlbumActionsAsync(AlbumDto album)
	{
		switch (await DisplayActionSheetAsync(album.Name, "Cancel", "Delete", "Show", "Rename"))
		{
			case "Show":
				await ShowAsync(albumId: album.Id);
				return;
			case "Rename" when await Prompt("Rename album", album.Name) is { } name:
				await Api.RenameAlbumAsync(album.Id, name);
				break;
			case "Delete" when await DisplayAlertAsync("Delete album?", "Only the album goes; its photos and videos stay in the library.", "Delete", "Cancel"):
				await Api.DeleteAlbumAsync(album.Id);
				break;
			default:
				return;
		}
		await LoadAsync();
	}

	async Task NewFolderAsync(int? parentId)
	{
		if (await Prompt("New folder", "") is not { } name) return;
		await Api.CreateFolderAsync(name, parentId);
		await LoadAsync();
	}

	async Task NewAlbumAsync()
	{
		if (await Prompt("New album", "") is not { } name) return;
		await Api.CreateAlbumAsync(name);
		await LoadAsync();
	}

	async Task<string?> Prompt(string title, string initial)
	{
		var text = await DisplayPromptAsync(title, null, "Save", "Cancel", initialValue: initial, maxLength: 100);
		return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
	}

	async Task ShowAsync(int? folderId = null, int? albumId = null, string? tag = null)
	{
		await Shell.Current.GoToAsync("..");
		await _library.ShowOnlyAsync(folderId, albumId, tag);
	}

	async Task RunAsync(Task task)
	{
		try
		{
			await task;
		}
		catch (Exception e) when (e is HttpRequestException or TaskCanceledException or LoreApiException)
		{
			await DisplayAlertAsync("Lore", e is LoreApiException { Status: System.Net.HttpStatusCode.Conflict or System.Net.HttpStatusCode.BadRequest } le
				? le.Message : "Couldn't reach Lore. Are you on home Wi-Fi?", "OK");
		}
	}
}

/// <summary>Everything Lore knows about one item, with caption editing and the same edit actions as bulk selection.</summary>
public sealed class InfoPage : ContentPage
{
	readonly Session _session;
	readonly Organizer _organizer;
	readonly ViewerState _viewer;
	readonly MediaItem _item;
	readonly VerticalStackLayout _facts = new() { Spacing = 4 };
	readonly Editor _caption = new() { Placeholder = "Add a caption", AutoSize = EditorAutoSizeOption.TextChanges, MaxLength = 2000 };
	readonly Label _status = new() { FontSize = 12 };
	readonly Button _maps = new() { Text = "Open in Maps", IsVisible = false };
	(double Lat, double Lon)? _location;

	public InfoPage(Session session, Organizer organizer, ViewerState viewer)
	{
		_session = session;
		_organizer = organizer;
		_viewer = viewer;
		_item = viewer.Items[viewer.Index];
		Title = "Info";

		var save = new Button { Text = "Save caption" };
		save.Clicked += async (_, _) => await SaveCaptionAsync();

		var actions = new FlexLayout { Wrap = Microsoft.Maui.Layouts.FlexWrap.Wrap };
		foreach (var action in new[] { "Add to album", "Remove from album", "Add tags", "Remove tags", "Move to folder", "Set owner" })
		{
			var b = new Button { Text = action, Style = (Style)Application.Current!.Resources["SecondaryButton"], Margin = new Thickness(0, 0, 8, 8) };
			b.Clicked += async (_, _) =>
			{
				if (!await _organizer.EditAsync(this, [_item.Dto.Id], action)) return;
				_viewer.Version++;
				await LoadAsync();
			};
			actions.Add(b);
		}

		// Hands off to the phone's maps app (needs internet there; Lore itself stays offline).
		_maps.Style = (Style)Application.Current!.Resources["SecondaryButton"];
		_maps.HorizontalOptions = LayoutOptions.Start;
		_maps.Clicked += async (_, _) =>
		{
			if (_location is var (lat, lon)) await Map.Default.TryOpenAsync(lat, lon, new MapLaunchOptions { Name = _item.Dto.OriginalFilename });
		};

		Content = new ScrollView
		{
			Content = new VerticalStackLayout
			{
				Padding = 16,
				Spacing = 12,
				Children = { _facts, _maps, _caption, save, _status, actions },
			},
		};
	}

	protected override async void OnAppearing()
	{
		base.OnAppearing();
		await LoadAsync();
	}

	async Task LoadAsync()
	{
		try
		{
			var api = _session.Api!;
			var d = await api.GetDetailsAsync(_item.Dto.Id);
			var albums = await api.GetAlbumsAsync();
			var folders = Organizer.Flatten(await api.GetFoldersAsync());
			var m = d.Media;

			_caption.Text = m.Caption;
			_location = m.Latitude is { } lat && m.Longitude is { } lon ? (lat, lon) : null;
			_maps.IsVisible = _location != null;
			var styles = Application.Current!.Resources;

			// Title block, then a label/value table so each fact scans in one column.
			_facts.Clear();
			_facts.Add(new Label { Text = m.OriginalFilename, Style = (Style)styles["SectionTitle"], LineBreakMode = LineBreakMode.MiddleTruncation });
			_facts.Add(new Label
			{
				Style = (Style)styles["Quiet"],
				Text = string.Join(" · ", new[]
				{
					$"{m.DateTaken:ddd d MMM yyyy, HH:mm}",
					m.MediaType == MediaTypes.Video ? "Video" : "Photo",
					m.Width is { } w && m.Height is { } h ? $"{w}×{h}" : null,
					_item.DurationText is { Length: > 0 } dur ? dur : null,
					Format.Bytes(m.FileSize),
				}.Where(s => s != null)),
			});
			var table = new Grid { ColumnDefinitions = [new(new GridLength(104)), new(GridLength.Star)], ColumnSpacing = 12, RowSpacing = 8, Margin = new Thickness(0, 12, 0, 0) };
			void Row(string label, string value)
			{
				var row = table.RowDefinitions.Count;
				table.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
				table.Add(new Label { Text = label, Style = (Style)styles["Quiet"], FontSize = 14 }.Row(row));
				table.Add(new Label { Text = value }.Row(row).Column(1));
			}
			Row("Belongs to", _session.MemberName(m.OwnerId) ?? "Unknown");
			Row("Uploaded by", $"{_session.MemberName(m.UploadedById)}{(d.DeviceName == null ? "" : " · " + d.DeviceName)}");
			Row("Location", _location is var (la, lo) ? Format.Coordinates(la, lo) : "None");
			if (m.CameraMake != null || m.CameraModel != null) Row("Camera", $"{m.CameraMake} {m.CameraModel}".Trim());
			Row("Folder", folders.FirstOrDefault(f => f.Folder.Id == m.FolderId).Path ?? "None");
			Row("Albums", d.AlbumIds.Count == 0 ? "None" : string.Join(", ", albums.Where(a => d.AlbumIds.Contains(a.Id)).Select(a => a.Name)));
			Row("Tags", d.Tags.Count == 0 ? "None" : string.Join(", ", d.Tags));
			_facts.Add(table);
		}
		catch (Exception e) when (e is HttpRequestException or TaskCanceledException or LoreApiException)
		{
			_status.Text = "Couldn't reach Lore. Are you on home Wi-Fi?";
		}
	}

	async Task SaveCaptionAsync()
	{
		try
		{
			await _session.Api!.SetCaptionAsync(_item.Dto.Id, _caption.Text);
			_status.Text = "Caption saved.";
		}
		catch (Exception e) when (e is HttpRequestException or TaskCanceledException or LoreApiException)
		{
			_status.Text = "Couldn't save. Are you on home Wi-Fi?";
		}
	}
}

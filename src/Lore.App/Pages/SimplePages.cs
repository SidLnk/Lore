using Lore.App.Services;
using Lore.Client;
using Lore.Contracts;
using Microsoft.Maui.Controls.Shapes;
using Path = Microsoft.Maui.Controls.Shapes.Path;

namespace Lore.App.Pages;

/// <summary>Recent media, upload queue status, storage use and the way into the recycle bin.</summary>
public sealed class HomePage : ContentPage
{
	readonly Session _session;
	readonly Uploads _uploads;
	readonly ViewerState _viewer;
	readonly Label _storage = new();
	readonly Label _queue = new() { IsVisible = false, FontFamily = "OpenSansSemibold" };
	readonly Button _bin = new() { Text = "Recycle bin", HorizontalOptions = LayoutOptions.Start, Margin = new Thickness(-12, 0, 0, 0) };
	readonly Button _updateHint = new() { IsVisible = false, HorizontalOptions = LayoutOptions.Start, Margin = new Thickness(-12, 0, 0, 0) };
	readonly AppUpdater _updater;
	readonly CollectionView _recent;
	List<MediaItem> _items = [], _shown = [];
	int _span = 3;

	public HomePage(Session session, Uploads uploads, ViewerState viewer, AppUpdater updater)
	{
		_updater = updater;
		_session = session;
		_uploads = uploads;
		_viewer = viewer;
		Title = "Home";
		_bin.Style = (Style)Application.Current!.Resources["TextButton"];
		_updateHint.Style = (Style)Application.Current!.Resources["TextButton"];
		_updateHint.Clicked += async (_, _) => await Shell.Current.GoToAsync("//family");

		_recent = new CollectionView
		{
			ItemsLayout = new GridItemsLayout(3, ItemsLayoutOrientation.Vertical) { HorizontalItemSpacing = 2, VerticalItemSpacing = 2 },
			SelectionMode = SelectionMode.Single,
			ItemTemplate = new DataTemplate(() =>
			{
				var image = new Image { Aspect = Aspect.AspectFill };
				image.SetAppThemeColor(BackgroundColorProperty, Color.FromArgb("#E6E6E6"), Color.FromArgb("#2A2A2A"));
				image.SetBinding(Image.SourceProperty, static (MediaItem m) => m.ThumbUrl);
				var duration = new Label { TextColor = Colors.White, FontSize = 12, VerticalOptions = LayoutOptions.Center };
				duration.SetBinding(Label.TextProperty, static (MediaItem m) => m.DurationText);
				duration.SetBinding(IsVisibleProperty, static (MediaItem m) => m.HasDuration);
				var video = new HorizontalStackLayout
				{
					Spacing = 4, Padding = new Thickness(6, 3), Margin = 4, BackgroundColor = Color.FromArgb("#80000000"),
					HorizontalOptions = LayoutOptions.End, VerticalOptions = LayoutOptions.End,
					Children =
					{
						new Path { Data = (Geometry)new PathGeometryConverter().ConvertFromInvariantString("M0,0 L7,4.5 L0,9 Z")!, Fill = Colors.White, WidthRequest = 7, HeightRequest = 9, VerticalOptions = LayoutOptions.Center },
						duration,
					},
				};
				video.SetBinding(IsVisibleProperty, static (MediaItem m) => m.IsVideo);
				return new SquareGrid { Children = { image, video } };
			}),
		};
		_recent.SelectionChanged += async (_, e) =>
		{
			if (e.CurrentSelection.FirstOrDefault() is not MediaItem item) return;
			_recent.SelectedItem = null;
			_viewer.Items = [.. _shown];
			_viewer.Index = _shown.IndexOf(item);
			_viewer.FromBin = false;
			await Shell.Current.GoToAsync("viewer");
		};
		_bin.Clicked += async (_, _) => await Shell.Current.GoToAsync("bin");
		Adaptive.FitColumns(_recent, 3, 130, span => { _span = span; ShowRecent(); });

		var seeAll = new Button { Text = "See all", Style = (Style)Application.Current.Resources["TextButton"] };
		seeAll.Clicked += async (_, _) => await Shell.Current.GoToAsync("//library");

		// Status reads as one quiet block; the photos below run edge to edge like the Library.
		Content = new Grid
		{
			RowDefinitions = [new(GridLength.Auto), new(GridLength.Auto), new(GridLength.Star)],
			Children =
			{
				new VerticalStackLayout { Padding = new Thickness(16, 8, 16, 0), Spacing = 4, Children = { _queue, _storage, _bin, _updateHint } },
				new Grid
				{
					Padding = new Thickness(16, 16, 4, 4),
					ColumnDefinitions = [new(GridLength.Star), new(GridLength.Auto)],
					Children =
					{
						new Label { Text = "Recent", Style = (Style)Application.Current.Resources["SectionTitle"], VerticalOptions = LayoutOptions.Center },
						seeAll.Column(1),
					},
				}.Row(1),
				_recent.Row(2),
			},
		};
	}

	void ShowRecent()
	{
		_shown = _items.Take(_span * 4).ToList();
		_recent.ItemsSource = _shown;
	}

	protected override async void OnAppearing()
	{
		base.OnAppearing();
		var pending = (await _uploads.Queue.AllAsync()).Count(i => i.Status is UploadStatus.Pending or UploadStatus.Uploading);
		_queue.Text = $"{Format.Plural(pending, "item")} waiting to upload";
		_queue.IsVisible = pending > 0;

		if (_session.Api is not { } api) return;
		try
		{
			var stats = await api.GetStorageAsync();
			var used = stats.Library.Sum(b => b.Bytes);
			var count = stats.Library.Sum(b => b.Count);
			_storage.Text = $"{Format.Plural(count, "item")} · {Format.Bytes(used)} in Lore · {Format.Bytes(stats.VolumeFreeBytes)} free on the home PC";
			_bin.Text = stats.BinCount == 0 ? "Recycle bin" : $"Recycle bin · {Format.Plural(stats.BinCount, "item")}";
			// Enough for four full rows at the widest grid (9 columns); each width shows four rows.
			_items = (await api.GetMediaAsync(new MediaQuery(), limit: 36)).Items.Select(m => new MediaItem(m, api)).ToList();
			if (await _updater.CheckAsync() is { } update) _updateHint.Text = $"Update available: Lore {update.VersionName}";
			_updateHint.IsVisible = _updater.Available != null;
			ShowRecent();
		}
		catch (Exception e) when (e is HttpRequestException or TaskCanceledException or LoreApiException)
		{
			_storage.Text = "Can't reach Lore. Connect to home Wi-Fi.";
		}
	}
}

/// <summary>Family members with their totals, who this phone belongs to, and unpairing.</summary>
public sealed class FamilyPage : ContentPage
{
	readonly Session _session;
	readonly AppUpdater _updater;
	readonly VerticalStackLayout _members = new() { Spacing = 16 };
	readonly Label _version = new();
	readonly Button _update = new() { IsVisible = false, HorizontalOptions = LayoutOptions.Start, Margin = new Thickness(0, 4, 0, 0) };

	public FamilyPage(Session session, AppUpdater updater)
	{
		_session = session;
		_updater = updater;
		Title = "Family";
		Adaptive.CapWidth(this);
		var styles = Application.Current!.Resources;
		var owners = session.Family.Select(f => new OwnerOption(f.Id, f.Name)).ToList();
		var picker = new Picker { Title = "This phone belongs to", ItemsSource = owners, SelectedItem = owners.FirstOrDefault(o => o.Id == session.DefaultOwnerId) };
		var status = new Label { Style = (Style)styles["Quiet"], IsVisible = false };
		_version.Style = (Style)styles["Quiet"];
		_update.Style = (Style)styles["SecondaryButton"];
		_update.Clicked += async (_, _) => await UpdateAsync();
		picker.SelectedIndexChanged += async (_, _) =>
		{
			if (picker.SelectedItem is not OwnerOption { Id: { } id } || id == session.DefaultOwnerId) return;
			try
			{
				await session.SetDefaultOwnerAsync(id);
				status.IsVisible = true;
				status.Text = "Saved. New uploads from this phone belong to " + session.MemberName(id) + ".";
			}
			catch (Exception e) when (e is HttpRequestException or TaskCanceledException or LoreApiException)
			{
				status.IsVisible = true;
				status.Text = "Couldn't save. Are you on home Wi-Fi?";
			}
		};

		var unpair = new Button { Text = "Unpair this phone", Style = (Style)styles["DangerTextButton"], HorizontalOptions = LayoutOptions.Start, Margin = new Thickness(-12, 0, 0, 0) };
		unpair.Clicked += async (_, _) =>
		{
			if (!await DisplayAlertAsync("Unpair?", "This phone will stop uploading and browsing until it is paired again.", "Unpair", "Cancel")) return;
			session.Unpair();
			((App)Application.Current!).ShowPairing(Window);
		};

		// Three groups, separated by space rather than lines: who's in the family, this phone, leaving.
		Content = new ScrollView
		{
			Content = new VerticalStackLayout
			{
				Padding = new Thickness(16, 16, 16, 24),
				Spacing = 32,
				Children =
				{
					_members,
					new VerticalStackLayout
					{
						Spacing = 4,
						Children =
						{
							new Label { Text = "This phone", Style = (Style)styles["SectionTitle"] },
							picker,
							status,
							new Label { Text = "Server " + session.Api?.BaseAddress, Style = (Style)styles["Quiet"] },
							_version,
							_update,
						},
					},
					unpair,
				},
			},
		};
	}

	async Task UpdateAsync()
	{
#if ANDROID
		if (_updater.Available is not { } v) return;
		_update.IsEnabled = false;
		try
		{
			var started = await _updater.InstallAsync(v, p => MainThread.BeginInvokeOnMainThread(() => _update.Text = $"Downloading… {p:P0}"), CancellationToken.None);
			_version.Text = started
				? $"Lore {AppUpdater.CurrentVersion}. Confirm the update in Android's prompt."
				: "Allow Lore to install apps in the settings that just opened, then come back and tap Update again.";
		}
		catch (Exception e) when (e is HttpRequestException or TaskCanceledException or LoreApiException or IOException)
		{
			_version.Text = "The update didn't download. Are you on home Wi-Fi? Try again.";
		}
		finally
		{
			_update.Text = $"Update Lore to {v.VersionName}";
			_update.IsEnabled = true;
		}
#else
		await Task.CompletedTask;
#endif
	}

	protected override async void OnAppearing()
	{
		base.OnAppearing();
		_version.Text = $"Lore {AppUpdater.CurrentVersion}";
		if (await _updater.CheckAsync() is { } update)
		{
			_version.Text = $"Lore {AppUpdater.CurrentVersion}. Version {update.VersionName} is ready on the home PC ({Format.Bytes(update.Size)}).";
			_update.Text = $"Update Lore to {update.VersionName}";
		}
		_update.IsVisible = _updater.Available != null;
		StorageStatsDto? stats = null;
		try { stats = await _session.Api!.GetStorageAsync(); }
		catch (Exception e) when (e is HttpRequestException or TaskCanceledException or LoreApiException) { }

		_members.Clear();
		foreach (var m in _session.Family)
		{
			var mine = stats?.Library.Where(b => b.OwnerId == m.Id).ToList() ?? [];
			int Count(string type) => mine.Where(b => b.MediaType == type).Sum(b => b.Count);
			_members.Add(new VerticalStackLayout
			{
				Spacing = 2,
				Children =
				{
					new Label { Text = m.Name, Style = (Style)Application.Current!.Resources["SectionTitle"] },
					new Label
					{
						Text = stats == null ? "Totals unavailable offline"
							: $"{Format.Plural(Count(MediaTypes.Photo), "photo")} · {Format.Plural(Count(MediaTypes.Video), "video")} · {Format.Bytes(mine.Sum(b => b.Bytes))}",
						Style = (Style)Application.Current!.Resources["Quiet"],
					},
				},
			});
		}
	}
}

static class ViewExtensions
{
	public static T Row<T>(this T view, int row) where T : BindableObject
	{
		Grid.SetRow(view, row);
		return view;
	}

	public static T Column<T>(this T view, int column) where T : BindableObject
	{
		Grid.SetColumn(view, column);
		return view;
	}
}

using Lore.App.Services;
using Lore.Client;
using Lore.Contracts;

namespace Lore.App.Pages;

/// <summary>Recent media, upload queue status, storage use and the way into the recycle bin.</summary>
public sealed class HomePage : ContentPage
{
	readonly Session _session;
	readonly Uploads _uploads;
	readonly ViewerState _viewer;
	readonly Label _storage = new();
	readonly Label _queue = new() { IsVisible = false };
	readonly Button _bin = new() { Text = "Recycle bin" };
	readonly CollectionView _recent;
	List<MediaItem> _items = [];

	public HomePage(Session session, Uploads uploads, ViewerState viewer)
	{
		_session = session;
		_uploads = uploads;
		_viewer = viewer;
		Title = "Home";

		_recent = new CollectionView
		{
			ItemsLayout = new GridItemsLayout(3, ItemsLayoutOrientation.Vertical) { HorizontalItemSpacing = 2, VerticalItemSpacing = 2 },
			SelectionMode = SelectionMode.Single,
			ItemTemplate = new DataTemplate(() =>
			{
				var image = new Image { Aspect = Aspect.AspectFill, BackgroundColor = Application.Current!.RequestedTheme == AppTheme.Dark ? Color.FromArgb("#2A2A2A") : Color.FromArgb("#E6E6E6"), HeightRequest = DeviceDisplay.Current.MainDisplayInfo.Width / DeviceDisplay.Current.MainDisplayInfo.Density / 3 - 2 };
				image.SetBinding(Image.SourceProperty, static (MediaItem m) => m.ThumbUrl);
				return image;
			}),
		};
		_recent.SelectionChanged += async (_, e) =>
		{
			if (e.CurrentSelection.FirstOrDefault() is not MediaItem item) return;
			_recent.SelectedItem = null;
			_viewer.Items = [.. _items];
			_viewer.Index = _items.IndexOf(item);
			_viewer.FromBin = false;
			await Shell.Current.GoToAsync("viewer");
		};
		_bin.Clicked += async (_, _) => await Shell.Current.GoToAsync("bin");

		Content = new Grid
		{
			Padding = new Thickness(16, 8),
			RowSpacing = 8,
			RowDefinitions = [new(GridLength.Auto), new(GridLength.Auto), new(GridLength.Auto), new(GridLength.Auto), new(GridLength.Star)],
			Children =
			{
				_storage,
				_queue.Row(1),
				_bin.Row(2),
				new Label { Text = "Recent", FontSize = 18, FontFamily = "OpenSansSemibold", Margin = new Thickness(0, 8, 0, 0) }.Row(3),
				_recent.Row(4),
			},
		};
	}

	protected override async void OnAppearing()
	{
		base.OnAppearing();
		var pending = (await _uploads.Queue.AllAsync()).Count(i => i.Status is UploadStatus.Pending or UploadStatus.Uploading);
		_queue.Text = $"{pending} waiting to upload";
		_queue.IsVisible = pending > 0;

		if (_session.Api is not { } api) return;
		try
		{
			var stats = await api.GetStorageAsync();
			var used = stats.Library.Sum(b => b.Bytes);
			var count = stats.Library.Sum(b => b.Count);
			_storage.Text = $"{count:N0} items · {Format.Bytes(used)} in Lore · {Format.Bytes(stats.VolumeFreeBytes)} free on the home PC";
			_bin.Text = stats.BinCount == 0 ? "Recycle bin" : $"Recycle bin ({stats.BinCount} · {Format.Bytes(stats.BinBytes)})";
			_items = (await api.GetMediaAsync(new MediaQuery(), limit: 12)).Items.Select(m => new MediaItem(m, api)).ToList();
			_recent.ItemsSource = _items;
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
	readonly VerticalStackLayout _members = new() { Spacing = 6 };

	public FamilyPage(Session session)
	{
		_session = session;
		Title = "Family";
		var owners = session.Family.Select(f => new OwnerOption(f.Id, f.Name)).ToList();
		var picker = new Picker { Title = "This phone belongs to", ItemsSource = owners, SelectedItem = owners.FirstOrDefault(o => o.Id == session.DefaultOwnerId) };
		var status = new Label { FontSize = 12 };
		picker.SelectedIndexChanged += async (_, _) =>
		{
			if (picker.SelectedItem is not OwnerOption { Id: { } id } || id == session.DefaultOwnerId) return;
			try
			{
				await session.SetDefaultOwnerAsync(id);
				status.Text = "Saved.";
			}
			catch (Exception e) when (e is HttpRequestException or TaskCanceledException or LoreApiException)
			{
				status.Text = "Couldn't save. Are you on home Wi-Fi?";
			}
		};

		var unpair = new Button { Text = "Unpair this phone" };
		unpair.Clicked += async (_, _) =>
		{
			if (!await DisplayAlertAsync("Unpair?", "This phone will stop uploading and browsing until it is paired again.", "Unpair", "Cancel")) return;
			session.Unpair();
			((App)Application.Current!).ShowPairing(Window);
		};

		Content = new ScrollView
		{
			Content = new VerticalStackLayout
			{
				Padding = 24,
				Spacing = 12,
				Children =
				{
					_members,
					new Label { Text = "This phone belongs to", Margin = new Thickness(0, 16, 0, 0) },
					picker,
					status,
					new Label { Text = "Server: " + session.Api?.BaseAddress, FontSize = 12, Opacity = 0.7 },
					unpair,
				},
			},
		};
	}

	protected override async void OnAppearing()
	{
		base.OnAppearing();
		StorageStatsDto? stats = null;
		try { stats = await _session.Api!.GetStorageAsync(); }
		catch (Exception e) when (e is HttpRequestException or TaskCanceledException or LoreApiException) { }

		_members.Clear();
		foreach (var m in _session.Family)
		{
			var mine = stats?.Library.Where(b => b.OwnerId == m.Id).ToList() ?? [];
			int Count(string type) => mine.Where(b => b.MediaType == type).Sum(b => b.Count);
			_members.Add(new Label { Text = m.Name, FontSize = 18, FontFamily = "OpenSansSemibold" });
			_members.Add(new Label
			{
				Text = stats == null ? "Totals unavailable offline"
					: $"{Count(MediaTypes.Photo):N0} photos · {Count(MediaTypes.Video):N0} videos · {Format.Bytes(mine.Sum(b => b.Bytes))}",
				Opacity = 0.7,
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

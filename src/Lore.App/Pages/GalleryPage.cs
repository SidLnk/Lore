using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Lore.App.Droid;
using Lore.App.Services;

namespace Lore.App.Pages;

public sealed partial class GalleryItem(DeviceMedia media) : ObservableObject
{
	public DeviceMedia Media { get; } = media;
	public bool IsVideo => Media.IsVideo;
	// Loaded lazily when the tile is first drawn.
	public ImageSource Thumb { get; } = ImageSource.FromStream(() => DeviceGallery.Thumbnail(media, 256) is { } jpeg ? new MemoryStream(jpeg) : Stream.Null);
	[ObservableProperty] public partial bool IsSelected { get; set; }
}

/// <summary>Lore's own photo/video chooser, reading originals from MediaStore so location data is kept.</summary>
public sealed class GalleryPage : ContentPage
{
	const int PageSize = 300;
	readonly UploadViewModel _upload;
	readonly Uploads _uploads;
	readonly ObservableCollection<GalleryItem> _items = [];
	readonly CollectionView _grid;
	readonly ToolbarItem _send = new() { Text = "Upload (0)" };
	readonly Label _message = new() { Padding = 16, IsVisible = false };
	readonly Button _settings = new() { Text = "Open settings", IsVisible = false, Margin = new Thickness(16, 0) };
	bool _end, _loading;

	public GalleryPage(UploadViewModel upload, Uploads uploads)
	{
		_upload = upload;
		_uploads = uploads;
		Title = "Choose";
		ToolbarItems.Add(_send);
		_send.Clicked += async (_, _) => await SendAsync();
		_settings.Clicked += (_, _) => AppInfo.Current.ShowSettingsUI();

		var size = DeviceDisplay.Current.MainDisplayInfo.Width / DeviceDisplay.Current.MainDisplayInfo.Density / 4 - 2;
		_grid = new CollectionView
		{
			ItemsLayout = new GridItemsLayout(4, ItemsLayoutOrientation.Vertical) { HorizontalItemSpacing = 2, VerticalItemSpacing = 2 },
			RemainingItemsThreshold = 60,
			ItemsSource = _items,
			ItemTemplate = new DataTemplate(() =>
			{
				var image = new Image { Aspect = Aspect.AspectFill, HeightRequest = size, BackgroundColor = Color.FromArgb("#E6E6E6") };
				image.SetBinding(Image.SourceProperty, static (GalleryItem i) => i.Thumb);
				var video = new Label { Text = "▶", TextColor = Colors.White, FontSize = 14, Margin = 4, HorizontalOptions = LayoutOptions.End, VerticalOptions = LayoutOptions.End };
				video.SetBinding(IsVisibleProperty, static (GalleryItem i) => i.IsVideo);
				var shade = new BoxView { Color = Color.FromArgb("#2B4C3F"), Opacity = 0.35 };
				shade.SetBinding(IsVisibleProperty, static (GalleryItem i) => i.IsSelected);
				var check = new Label
				{
					Text = "✓", TextColor = Colors.White, BackgroundColor = Color.FromArgb("#2B4C3F"), FontSize = 14, Margin = 4,
					WidthRequest = 24, HeightRequest = 24, HorizontalTextAlignment = TextAlignment.Center, VerticalTextAlignment = TextAlignment.Center,
					HorizontalOptions = LayoutOptions.Start, VerticalOptions = LayoutOptions.Start,
				};
				check.SetBinding(IsVisibleProperty, static (GalleryItem i) => i.IsSelected);
				var tile = new Grid { Children = { image, video, shade, check } };
				var tap = new TapGestureRecognizer();
				tap.Tapped += (s, _) =>
				{
					if (((View)s!).BindingContext is not GalleryItem item) return;
					item.IsSelected = !item.IsSelected;
					_send.Text = $"Upload ({_items.Count(i => i.IsSelected)})";
				};
				tile.GestureRecognizers.Add(tap);
				return tile;
			}),
		};
		_grid.RemainingItemsThresholdReached += (_, _) => LoadMore();

		Content = new Grid
		{
			RowDefinitions = [new(GridLength.Auto), new(GridLength.Auto), new(GridLength.Star)],
			Children = { _message, _settings.Row(1), _grid.Row(2) },
		};
	}

	protected override async void OnAppearing()
	{
		base.OnAppearing();
		if (_items.Count > 0) return;
		if (await Permissions.RequestAsync<DeviceGallery.MediaPermission>() != PermissionStatus.Granted)
		{
			_message.Text = "Lore needs access to photos and videos to upload originals with their location. " +
				"You can still use the system picker on the Upload tab, but Android removes location from those copies.";
			_message.IsVisible = _settings.IsVisible = true;
			return;
		}
		_message.IsVisible = _settings.IsVisible = false;
		LoadMore();
	}

	void LoadMore()
	{
		if (_loading || _end) return;
		_loading = true;
		var page = DeviceGallery.Query(_items.Count, PageSize);
		foreach (var m in page) _items.Add(new GalleryItem(m));
		_end = page.Count < PageSize;
		_loading = false;
	}

	async Task SendAsync()
	{
		var picked = _items.Where(i => i.IsSelected).Select(i => i.Media).ToList();
		if (picked.Count == 0 || _upload.SelectedOwner?.Id is not { } owner) return;
		// Progress notification on Android 13+; uploads still run if the user says no.
		await Permissions.RequestAsync<Permissions.PostNotifications>();
		await _uploads.EnqueueAsync(picked, owner);
		await Shell.Current.GoToAsync("..");
	}
}

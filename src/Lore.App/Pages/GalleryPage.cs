using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Maui.Controls.Shapes;
using Path = Microsoft.Maui.Controls.Shapes.Path;
using Lore.App.Droid;
using Lore.App.Services;

namespace Lore.App.Pages;

public sealed partial class GalleryItem(DeviceMedia media, bool inLore) : ObservableObject
{
	public DeviceMedia Media { get; } = media;
	/// <summary>This exact file is already in Lore (uploaded, or found by Check this phone).</summary>
	public bool InLore { get; } = inLore;
	public bool IsVideo => Media.IsVideo;
	// Loaded lazily when the tile is first drawn.
	public ImageSource Thumb { get; } = ImageSource.FromStream(() => DeviceGallery.Thumbnail(media, 256) is { } jpeg ? new MemoryStream(jpeg) : Stream.Null);
	[ObservableProperty] public partial bool IsSelected { get; set; }
}

public sealed class GalleryMonth(DateTime month) : ObservableCollection<GalleryItem>
{
	public DateTime Month { get; } = month;
	public string Title => Month.ToString("MMMM yyyy");
}

/// <summary>Lore's own photo/video chooser, reading originals from MediaStore so location data is kept.</summary>
public sealed class GalleryPage : ContentPage
{
	const int PageSize = 300;
	readonly UploadViewModel _upload;
	readonly Uploads _uploads;
	static readonly (string Label, bool? Videos)[] Kinds = [("All", null), ("Photos", false), ("Videos", true)];
	readonly ObservableCollection<GalleryMonth> _months = [];
	// Picks survive switching between All, Photos and Videos.
	readonly Dictionary<long, DeviceMedia> _picked = [];
	readonly HorizontalStackLayout _kinds = new() { Spacing = 8, Padding = new Thickness(12, 4, 12, 0) };
	int _loaded, _kind;
	readonly CollectionView _grid;
	readonly ToolbarItem _send = new() { Text = "Upload (0)" };
	readonly Label _message = new() { Padding = 16, IsVisible = false };
	readonly Button _settings = new() { Text = "Open settings", IsVisible = false, Margin = new Thickness(16, 0) };
	bool _end, _loading;
	Dictionary<string, long> _inLore = [];

	public GalleryPage(UploadViewModel upload, Uploads uploads)
	{
		_upload = upload;
		_uploads = uploads;
		Title = "Choose";
		ToolbarItems.Add(_send);
		_send.Clicked += async (_, _) => await SendAsync();
		_settings.Clicked += (_, _) => AppInfo.Current.ShowSettingsUI();

		_grid = new CollectionView
		{
			ItemsLayout = new GridItemsLayout(4, ItemsLayoutOrientation.Vertical) { HorizontalItemSpacing = 2, VerticalItemSpacing = 2 },
			RemainingItemsThreshold = 60,
			ItemsSource = _months,
			IsGrouped = true,
			// Month breaks, as in the Library, so a long camera roll is easy to scan.
			GroupHeaderTemplate = new DataTemplate(() =>
			{
				var header = new Label { FontFamily = "OpenSansSemibold", FontSize = 16, Padding = new Thickness(8, 16, 8, 6) };
				header.SetBinding(Label.TextProperty, static (GalleryMonth m) => m.Title);
				return header;
			}),
			ItemTemplate = new DataTemplate(() =>
			{
				// Same selection language as the Library: inset photo on a tonal tile, drawn ring and check.
				var image = new Image { Aspect = Aspect.AspectFill };
				image.SetBinding(Image.SourceProperty, static (GalleryItem i) => i.Thumb);
				image.SetBinding(View.MarginProperty, static (GalleryItem i) => i.IsSelected, converter: new SelectedInset());
				var video = new Grid
				{
					Padding = new Thickness(6, 3), Margin = 4, BackgroundColor = Color.FromArgb("#80000000"),
					HorizontalOptions = LayoutOptions.End, VerticalOptions = LayoutOptions.End,
					Children = { new Path { Data = (Geometry)new PathGeometryConverter().ConvertFromInvariantString("M0,0 L7,4.5 L0,9 Z")!, Fill = Colors.White, WidthRequest = 7, HeightRequest = 9 } },
				};
				video.SetBinding(IsVisibleProperty, static (GalleryItem i) => i.IsVideo);
				var check = new Grid
				{
					Children =
					{
						new Ellipse { Fill = Color.FromArgb("#2B4C3F"), Stroke = Colors.White, StrokeThickness = 2 },
						new Path
						{
							Data = (Geometry)new PathGeometryConverter().ConvertFromInvariantString("M7,12.5 L10.5,16 L17,9")!,
							Stroke = Colors.White, StrokeThickness = 2.25, StrokeLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round, Aspect = Stretch.None,
						},
					},
				};
				check.SetBinding(IsVisibleProperty, static (GalleryItem i) => i.IsSelected);
				var mark = new Grid
				{
					WidthRequest = 24, HeightRequest = 24, Margin = 6, HorizontalOptions = LayoutOptions.Start, VerticalOptions = LayoutOptions.Start,
					Children = { new Ellipse { Stroke = Colors.White, StrokeThickness = 2, Fill = Color.FromArgb("#33000000") }, check },
				};
				// "In Lore": same badge language as video (50% black, drawn glyph, 12px label), bottom left.
				var inLore = new HorizontalStackLayout
				{
					Spacing = 3, Padding = new Thickness(5, 2, 6, 2), Margin = 4, BackgroundColor = Color.FromArgb("#80000000"),
					HorizontalOptions = LayoutOptions.Start, VerticalOptions = LayoutOptions.End,
					Children =
					{
						new Path
						{
							Data = (Geometry)new PathGeometryConverter().ConvertFromInvariantString("M1,5 L4,8 L10,1")!,
							Stroke = Colors.White, StrokeThickness = 1.75, StrokeLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round,
							WidthRequest = 11, HeightRequest = 9, VerticalOptions = LayoutOptions.Center, Aspect = Stretch.None,
						},
						new Label { Text = "In Lore", TextColor = Colors.White, FontSize = 12, VerticalOptions = LayoutOptions.Center },
					},
				};
				inLore.SetBinding(IsVisibleProperty, static (GalleryItem i) => i.InLore);
				var tile = new SquareGrid { Children = { image, video, inLore, mark } };
				tile.SetBinding(BackgroundColorProperty, static (GalleryItem i) => i.IsSelected, converter: new SelectedTile());
				var tap = new TapGestureRecognizer();
				tap.Tapped += (s, _) =>
				{
					if (((View)s!).BindingContext is not GalleryItem item) return;
					item.IsSelected = !item.IsSelected;
					if (item.IsSelected) _picked[item.Media.Id] = item.Media;
					else _picked.Remove(item.Media.Id);
					_send.Text = $"Upload ({_picked.Count})";
				};
				tile.GestureRecognizers.Add(tap);
				return tile;
			}),
		};
		_grid.RemainingItemsThresholdReached += (_, _) => LoadMore();
		Adaptive.FitColumns(_grid, 4, 100);

		for (var i = 0; i < Kinds.Length; i++)
		{
			var index = i;
			_kinds.Add(new Chip { Text = Kinds[i].Label, IsActive = i == 0, Command = new Command(() => SelectKind(index)) });
		}

		Content = new Grid
		{
			RowDefinitions = [new(GridLength.Auto), new(GridLength.Auto), new(GridLength.Auto), new(GridLength.Star)],
			Children = { _message, _settings.Row(1), _kinds.Row(2), _grid.Row(3) },
		};
	}

	protected override async void OnAppearing()
	{
		base.OnAppearing();
		if (_loaded > 0) return;
		if (await Permissions.RequestAsync<DeviceGallery.MediaPermission>() != PermissionStatus.Granted)
		{
			_message.Text = "Lore needs access to photos and videos to upload originals with their location. " +
				"You can still use the system picker on the Upload tab, but Android removes location from those copies.";
			_message.IsVisible = _settings.IsVisible = true;
			return;
		}
		_message.IsVisible = _settings.IsVisible = false;
		_inLore = (await _uploads.Queue.InLoreAsync()).ToDictionary(r => r.ContentUri, r => r.Size);
		LoadMore();
	}

	void LoadMore()
	{
		if (_loading || _end) return;
		_loading = true;
		var page = DeviceGallery.Query(_loaded, PageSize, Kinds[_kind].Videos);
		_loaded += page.Count;
		foreach (var m in page)
		{
			var month = new DateTime(m.When.Year, m.When.Month, 1);
			if (_months.LastOrDefault() is not { } group || group.Month != month) _months.Add(group = new GalleryMonth(month));
			group.Add(new GalleryItem(m, _inLore.TryGetValue(m.ContentUri.ToString()!, out var size) && size == m.Size)
				{ IsSelected = _picked.ContainsKey(m.Id) });
		}
		_end = page.Count < PageSize;
		_loading = false;
	}

	void SelectKind(int index)
	{
		if (index == _kind) return;
		_kind = index;
		for (var i = 0; i < _kinds.Count; i++) ((Chip)_kinds[i]).IsActive = i == index;
		_months.Clear();
		_loaded = 0;
		_end = false;
		LoadMore();
	}

	async Task SendAsync()
	{
		var picked = _picked.Values.OrderBy(m => m.When).ToList();
		if (picked.Count == 0 || _upload.SelectedOwner?.Id is not { } owner) return;
		// Progress notification on Android 13+; uploads still run if the user says no.
		await Permissions.RequestAsync<Permissions.PostNotifications>();
		await _uploads.EnqueueAsync(picked, owner);
		await Shell.Current.GoToAsync("..");
	}
}

sealed class SelectedInset : IValueConverter
{
	public object Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) =>
		value is true ? new Thickness(10) : new Thickness(0);
	public object ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) => throw new NotSupportedException();
}

sealed class SelectedTile : IValueConverter
{
	public object Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
	{
		var dark = Application.Current?.RequestedTheme == AppTheme.Dark;
		return value is true ? Color.FromArgb(dark ? "#1B3329" : "#DCEBE3") : Color.FromArgb(dark ? "#2A2A2A" : "#E6E6E6");
	}
	public object ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) => throw new NotSupportedException();
}

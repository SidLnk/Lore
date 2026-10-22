using Lore.App.Services;
using Lore.Client;
using Lore.Contracts;

namespace Lore.App.Pages;

/// <summary>
/// Where photos were taken, without map tiles (V1 is offline): a dot plot of places plus a list.
/// Tapping a dot or row opens the library filtered to that area.
/// </summary>
public sealed class PlacesPage : ContentPage
{
	static readonly (string Label, double Cell)[] Sizes = [("City", 0.1), ("Neighbourhood", 0.01)];

	readonly Session _session;
	readonly LibraryViewModel _library;
	readonly Picker _size = new() { Title = "Group by", ItemsSource = Sizes.Select(s => s.Label).ToList(), SelectedIndex = 0 };
	readonly Label _summary = new() { Opacity = 0.7 };
	readonly PlotDrawable _plot = new();
	readonly GraphicsView _canvas;
	readonly CollectionView _list;
	List<PlaceDto> _places = [];

	public PlacesPage(Session session, LibraryViewModel library)
	{
		_session = session;
		_library = library;
		Title = "Places";

		_canvas = new GraphicsView { Drawable = _plot, HeightRequest = 240 };
		_canvas.EndInteraction += async (_, e) =>
		{
			if (_plot.HitTest(e.Touches[0]) is { } place) await OpenAsync(place);
		};

		_list = new CollectionView
		{
			SelectionMode = SelectionMode.Single,
			ItemTemplate = new DataTemplate(BuildRow),
			EmptyView = "No photos with a location yet. Photos keep their GPS position when the camera records one.",
		};
		_list.SelectionChanged += async (_, e) =>
		{
			if (e.CurrentSelection.FirstOrDefault() is not PlaceRow row) return;
			_list.SelectedItem = null;
			await OpenAsync(row.Place);
		};
		_size.SelectedIndexChanged += async (_, _) => await LoadAsync();

		Content = new Grid
		{
			Padding = new Thickness(12, 4),
			RowSpacing = 6,
			RowDefinitions = [new(GridLength.Auto), new(GridLength.Auto), new(GridLength.Auto), new(GridLength.Star)],
			Children = { _size, _summary.Row(1), _canvas.Row(2), _list.Row(3) },
		};
	}

	protected override async void OnAppearing()
	{
		base.OnAppearing();
		await LoadAsync();
	}

	async Task LoadAsync()
	{
		if (_session.Api is not { } api) return;
		try
		{
			_places = await api.GetPlacesAsync(Sizes[Math.Max(0, _size.SelectedIndex)].Cell);
			_summary.Text = _places.Count == 0 ? "" : $"{Format.Plural(_places.Sum(p => p.Count), "item")} in {Format.Plural(_places.Count, "place")}";
			_plot.Places = _places;
			_canvas.Invalidate();
			_list.ItemsSource = _places.Select(p => new PlaceRow(p, api.ThumbUrl(p.SampleMediaId))).ToList();
		}
		catch (Exception e) when (e is HttpRequestException or TaskCanceledException or LoreApiException)
		{
			_summary.Text = "Can't reach Lore. Connect to home Wi-Fi.";
		}
	}

	async Task OpenAsync(PlaceDto place)
	{
		await Shell.Current.GoToAsync("//library");
		await _library.ShowAreaAsync(GeoArea.Of(place), PlaceRow.Coordinates(place));
	}

	View BuildRow()
	{
		var thumb = new Image { Aspect = Aspect.AspectFill, WidthRequest = 64, HeightRequest = 64, BackgroundColor = Color.FromArgb("#E6E6E6") };
		thumb.SetBinding(Image.SourceProperty, static (PlaceRow r) => r.Thumb);
		var title = new Label { FontSize = 16 };
		title.SetBinding(Label.TextProperty, static (PlaceRow r) => r.Title);
		var detail = new Label { FontSize = 13, Opacity = 0.7 };
		detail.SetBinding(Label.TextProperty, static (PlaceRow r) => r.Detail);
		var maps = new Button { Text = "Maps", Padding = new Thickness(12, 0), HeightRequest = 40, VerticalOptions = LayoutOptions.Center };
		maps.SetBinding(Button.CommandParameterProperty, static (PlaceRow r) => r.Place);
		// Hands off to the phone's maps app (uses the internet there; Lore itself stays offline).
		maps.Clicked += async (s, _) =>
		{
			if (((Button)s!).CommandParameter is PlaceDto p) await Map.Default.TryOpenAsync(p.Lat, p.Lon, new MapLaunchOptions { Name = "Lore photos" });
		};
		return new Grid
		{
			Padding = new Thickness(0, 6),
			ColumnSpacing = 12,
			ColumnDefinitions = [new(GridLength.Auto), new(GridLength.Star), new(GridLength.Auto)],
			Children =
			{
				thumb,
				new VerticalStackLayout { VerticalOptions = LayoutOptions.Center, Children = { title, detail } }.Column(1),
				maps.Column(2),
			},
		};
	}

	internal sealed class PlaceRow(PlaceDto place, Uri thumb)
	{
		public PlaceDto Place { get; } = place;
		public Uri Thumb { get; } = thumb;
		public string Title => Coordinates(Place);
		public string Detail => $"{Format.Plural(Place.Count, "item")} · {Range(Place.FirstTaken, Place.LastTaken)}";

		// shortcut: coordinates instead of place names; naming needs reverse geocoding (online) or an offline gazetteer.
		public static string Coordinates(PlaceDto p) => $"{Math.Abs(p.Lat):0.###}°{(p.Lat >= 0 ? "N" : "S")}, {Math.Abs(p.Lon):0.###}°{(p.Lon >= 0 ? "E" : "W")}";

		static string Range(DateTime a, DateTime b) =>
			a.Year == b.Year && a.Month == b.Month ? $"{a:MMM yyyy}" : $"{a:MMM yyyy} – {b:MMM yyyy}";
	}

	/// <summary>Equirectangular dot plot fitted to the places' bounding box; dot size grows with item count.</summary>
	sealed class PlotDrawable : IDrawable
	{
		const float Pad = 28;
		public List<PlaceDto> Places { get; set; } = [];
		readonly List<(PointF At, PlaceDto Place)> _dots = [];

		public void Draw(ICanvas canvas, RectF rect)
		{
			_dots.Clear();
			canvas.FillColor = Application.Current?.RequestedTheme == AppTheme.Dark ? Color.FromArgb("#1E2A25") : Color.FromArgb("#EEF3F0");
			canvas.FillRoundedRectangle(rect, 12);
			if (Places.Count == 0) return;

			double minLat = Places.Min(p => p.Lat), maxLat = Places.Max(p => p.Lat), minLon = Places.Min(p => p.Lon), maxLon = Places.Max(p => p.Lon);
			// Shrink longitude by cos(latitude) so distances look right away from the equator.
			var lonScale = Math.Cos((minLat + maxLat) / 2 * Math.PI / 180);
			var spanX = Math.Max((maxLon - minLon) * lonScale, 1e-6);
			var spanY = Math.Max(maxLat - minLat, 1e-6);
			var scale = Math.Min((rect.Width - 2 * Pad) / spanX, (rect.Height - 2 * Pad) / spanY);
			var offX = rect.X + (rect.Width - spanX * scale) / 2;
			var offY = rect.Y + (rect.Height - spanY * scale) / 2;

			canvas.FillColor = Color.FromArgb("#2B4C3F").WithAlpha(0.75f);
			foreach (var p in Places.OrderBy(p => p.Count))
			{
				var at = new PointF((float)(offX + (p.Lon - minLon) * lonScale * scale), (float)(offY + (maxLat - p.Lat) * scale));
				canvas.FillCircle(at, 4 + 3 * MathF.Log2(p.Count));
				_dots.Add((at, p));
			}
		}

		public PlaceDto? HitTest(PointF touch) =>
			_dots.Select(d => (d.Place, Distance: Math.Sqrt(Math.Pow(d.At.X - touch.X, 2) + Math.Pow(d.At.Y - touch.Y, 2))))
				.Where(d => d.Distance < 28)
				.OrderBy(d => d.Distance)
				.Select(d => d.Place)
				.FirstOrDefault();
	}
}

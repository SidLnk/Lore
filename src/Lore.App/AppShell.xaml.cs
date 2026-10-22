using Lore.App.Pages;

namespace Lore.App;

public partial class AppShell : Shell
{
	bool _rail;

	public AppShell()
	{
		InitializeComponent();
		Routing.RegisterRoute("viewer", typeof(ViewerPage));
		Routing.RegisterRoute("video", typeof(VideoPage));
		Routing.RegisterRoute("bin", typeof(BinPage));
		Routing.RegisterRoute("organize", typeof(OrganizePage));
		Routing.RegisterRoute("info", typeof(InfoPage));
		Routing.RegisterRoute("gallery", typeof(GalleryPage));
		// Window width, not device model, picks the navigation: tablets in split screen get phone tabs.
		// Shell itself never raises SizeChanged on Android, so watch the window.
		Loaded += (_, _) =>
		{
			Window.SizeChanged += (_, _) => FitNavigation();
			Application.Current!.RequestedThemeChanged += (_, _) => FitRail();
			FitNavigation();
		};
	}

	protected override void OnNavigated(ShellNavigatedEventArgs args)
	{
		base.OnNavigated(args);
		UpdateNavigation();
	}

	void FitNavigation()
	{
		if (Window is not { Width: > 0 } window) return;
		if (_rail != window.Width >= Adaptive.RailWidth)
		{
			_rail = !_rail;
			UpdateNavigation();
		}
		// Window insets update after SizeChanged (rotation moves the cutout), so size the rail once they settle.
		Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(50), FitRail);
	}

	void UpdateNavigation()
	{
		// The viewer and video player are fullscreen at every size, so they hide the rail as they hide the tabs.
		var fullscreen = CurrentPage is ViewerPage or VideoPage;
		FlyoutBehavior = _rail && !fullscreen ? FlyoutBehavior.Locked : FlyoutBehavior.Disabled;
		if (CurrentPage != null && !fullscreen) SetTabBarIsVisible(CurrentPage, !_rail);
	}

	/// <summary>
	/// The locked flyout runs up under the status bar, so its top carries the app bar's green band. In landscape a
	/// camera cutout can sit on the rail's side; the flyout pads it out, so the rail widens by that inset.
	/// </summary>
	void FitRail()
	{
		if (!_rail || Window is not { Height: > 0 } window) return;
		var (top, left) = SystemInsets();
		FlyoutWidth = 80 + left;
		var resources = Application.Current!.Resources;
		var page = Application.Current.RequestedTheme == AppTheme.Dark ? (Color)resources["OffBlack"] : Colors.White;
		var band = (Color)resources["Primary"];
		var stop = (float)(top / window.Height);
		FlyoutBackground = new LinearGradientBrush([new(band, 0), new(band, stop), new(page, stop), new(page, 1)], new Point(0, 0), new Point(0, 1));
	}

	/// <summary>Status bar height and the start-side system inset (cutout or bars), in dp.</summary>
	static (double Top, double Left) SystemInsets()
	{
#if ANDROID
		var insets = Platform.CurrentActivity?.Window?.DecorView.RootWindowInsets;
		if (insets == null) return (0, 0);
		var density = DeviceDisplay.Current.MainDisplayInfo.Density;
		if (!OperatingSystem.IsAndroidVersionAtLeast(30)) return (insets.StableInsetTop / density, insets.StableInsetLeft / density);
		var top = insets.GetInsets(Android.Views.WindowInsets.Type.StatusBars()).Top;
		var left = insets.GetInsets(Android.Views.WindowInsets.Type.SystemBars() | Android.Views.WindowInsets.Type.DisplayCutout()).Left;
		return (top / density, left / density);
#else
		return (0, 0);
#endif
	}
}

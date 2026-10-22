using Lore.App.Pages;
using Lore.App.Services;

namespace Lore.App;

public partial class App : Application
{
	readonly IServiceProvider _services;

	public App(IServiceProvider services)
	{
		InitializeComponent();
		_services = services;
	}

	protected override Window CreateWindow(IActivationState? activationState)
	{
		var window = new Window(new ContentPage { Content = new ActivityIndicator { IsRunning = true, VerticalOptions = LayoutOptions.Center } });
		window.Created += async (_, _) =>
		{
			var session = _services.GetRequiredService<Session>();
			await session.RestoreAsync();
			if (session.IsPaired) ShowMain(window);
			else ShowPairing(window);
		};
		return window;
	}

	public void ShowMain(Window window)
	{
		window.Page = _services.GetRequiredService<AppShell>();
		StatusBarIcons(onPaper: false);
		// Resume anything left in the queue by a previous run.
		_services.GetRequiredService<Uploads>().Start();
	}

	public void ShowPairing(Window window)
	{
		window.Page = _services.GetRequiredService<PairingPage>();
		StatusBarIcons(onPaper: true);
	}

	/// <summary>
	/// The Shell pages sit under a green status band and need light icons; pairing has no app bar, so its
	/// icons sit on the page itself and must be dark on the light theme.
	/// </summary>
	void StatusBarIcons(bool onPaper)
	{
#if ANDROID
		if (Platform.CurrentActivity?.Window is { } w && AndroidX.Core.View.WindowCompat.GetInsetsController(w, w.DecorView) is { } bars)
			bars.AppearanceLightStatusBars = onPaper && RequestedTheme != AppTheme.Dark;
#endif
	}
}

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
			else window.Page = _services.GetRequiredService<PairingPage>();
		};
		return window;
	}

	public void ShowMain(Window window)
	{
		window.Page = _services.GetRequiredService<AppShell>();
		// Resume anything left in the queue by a previous run.
		_services.GetRequiredService<Uploads>().Start();
	}

	public void ShowPairing(Window window) => window.Page = _services.GetRequiredService<PairingPage>();
}

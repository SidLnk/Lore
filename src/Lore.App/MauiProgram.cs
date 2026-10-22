using CommunityToolkit.Maui;
using Lore.App.Pages;
using Lore.App.Services;
using Microsoft.Extensions.Logging;

namespace Lore.App;

public static class MauiProgram
{
	public static MauiApp CreateMauiApp()
	{
		var builder = MauiApp.CreateBuilder();
		builder
			.UseMauiApp<App>()
			.UseMauiCommunityToolkit()
			.UseMauiCommunityToolkitMediaElement(isAndroidForegroundServiceEnabled: false)
			.ConfigureFonts(fonts =>
			{
				fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
				fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
			});

		builder.Services.AddSingleton<Session>();
		builder.Services.AddSingleton<Uploads>();
		builder.Services.AddSingleton<Saves>();
		builder.Services.AddSingleton<ViewerState>();
		builder.Services.AddSingleton<Organizer>();
		builder.Services.AddTransient<OrganizePage>();
		builder.Services.AddTransient<InfoPage>();
		builder.Services.AddTransient<GalleryPage>();
		builder.Services.AddTransient<PairingPage>();
		builder.Services.AddTransient<AppShell>();
		builder.Services.AddTransient<HomePage>();
		builder.Services.AddSingleton<LibraryViewModel>();
		builder.Services.AddTransient<LibraryPage>();
		builder.Services.AddSingleton<BinViewModel>();
		builder.Services.AddTransient<BinPage>();
		builder.Services.AddTransient<ViewerPage>();
		builder.Services.AddTransient<VideoPage>();
		builder.Services.AddSingleton<UploadViewModel>();
		builder.Services.AddTransient<UploadPage>();
		builder.Services.AddTransient<PlacesPage>();
		builder.Services.AddTransient<FamilyPage>();

#if DEBUG
		builder.Logging.AddDebug();
#endif

		return builder.Build();
	}
}

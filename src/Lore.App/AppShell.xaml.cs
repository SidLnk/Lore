using Lore.App.Pages;

namespace Lore.App;

public partial class AppShell : Shell
{
	public AppShell()
	{
		InitializeComponent();
		Routing.RegisterRoute("viewer", typeof(ViewerPage));
		Routing.RegisterRoute("video", typeof(VideoPage));
		Routing.RegisterRoute("bin", typeof(BinPage));
		Routing.RegisterRoute("organize", typeof(OrganizePage));
		Routing.RegisterRoute("info", typeof(InfoPage));
		Routing.RegisterRoute("gallery", typeof(GalleryPage));
	}
}

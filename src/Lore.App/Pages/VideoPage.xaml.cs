using CommunityToolkit.Maui.Views;

namespace Lore.App.Pages;

/// <summary>Streams the original over HTTP range requests; no transcoding in V1, so playback depends on the phone's codecs.</summary>
public partial class VideoPage : ContentPage
{
	public VideoPage(ViewerState state)
	{
		InitializeComponent();
		Player.Source = MediaSource.FromUri(state.Items[state.Index].OriginalUrl);
	}

	protected override void OnDisappearing()
	{
		base.OnDisappearing();
		Player.Stop();
		Player.Handler?.DisconnectHandler();
	}
}

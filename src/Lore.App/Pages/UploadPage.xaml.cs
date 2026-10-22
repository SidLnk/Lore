namespace Lore.App.Pages;

public partial class UploadPage : ContentPage
{
	readonly UploadViewModel _vm;
	bool _visible;

	public UploadPage(UploadViewModel vm)
	{
		InitializeComponent();
		BindingContext = _vm = vm;
	}

	protected override void OnAppearing()
	{
		base.OnAppearing();
		_visible = true;
		_ = _vm.RefreshAsync();
		// The queue is written by the background service; poll it while the page is on screen.
		Dispatcher.StartTimer(TimeSpan.FromSeconds(1), () =>
		{
			if (_visible) _ = _vm.RefreshAsync();
			return _visible;
		});
	}

	protected override void OnDisappearing()
	{
		_visible = false;
		base.OnDisappearing();
	}
}

namespace Lore.App.Pages;

public partial class UploadPage : ContentPage
{
	readonly UploadViewModel _vm;
	bool _visible;

	public UploadPage(UploadViewModel vm)
	{
		InitializeComponent();
		Adaptive.CapWidth(this);
		BindingContext = _vm = vm;
	}

	// New uploads are inserted at the top; without this they land above the visible rows.
	void ScrollToTop() => Queue.ScrollTo(0, position: ScrollToPosition.Start, animate: false);

	protected override void OnAppearing()
	{
		base.OnAppearing();
		_visible = true;
		_vm.RowsAddedAtTop += ScrollToTop;
		_ = _vm.RefreshAsync();
		_ = _vm.RefreshFreeUpAsync();
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
		_vm.RowsAddedAtTop -= ScrollToTop;
		base.OnDisappearing();
	}
}

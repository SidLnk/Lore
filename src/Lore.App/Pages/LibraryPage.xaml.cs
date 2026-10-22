using System.ComponentModel;

namespace Lore.App.Pages;

public partial class LibraryPage : ContentPage
{
	readonly LibraryViewModel _vm;
	readonly ViewerState _viewer;
	readonly ToolbarItem _select = new() { Text = "Select" };
	readonly ToolbarItem _organize = new() { Text = "Organize" };
	readonly ToolbarItem _all = new() { Text = "All" };
	readonly ToolbarItem _edit = new();
	readonly ToolbarItem _cancel = new() { Text = "Cancel" };
	int _seenVersion;
	public LibraryViewModel Vm => _vm;

	public LibraryPage(LibraryViewModel vm, ViewerState viewer)
	{
		InitializeComponent();
		BindingContext = _vm = vm;
		_viewer = viewer;
		Adaptive.FitColumns(Media, 3, 130);
		if (vm.IsBin) return;

		_select.Clicked += (_, _) => vm.StartSelecting();
		_organize.Clicked += async (_, _) => await Shell.Current.GoToAsync("organize");
		_all.Clicked += (_, _) => vm.SelectAll();
		_edit.Clicked += async (_, _) => await vm.EditSelectedAsync(this);
		_cancel.Clicked += (_, _) => vm.StopSelecting();
		vm.PropertyChanged += OnVmChanged;
		UpdateToolbar();
	}

	void OnVmChanged(object? sender, PropertyChangedEventArgs e)
	{
		if (e.PropertyName is nameof(LibraryViewModel.IsSelecting) or nameof(LibraryViewModel.SelectedCount)) UpdateToolbar();
	}

	void UpdateToolbar()
	{
		_edit.Text = $"Edit ({_vm.SelectedCount})";
		ToolbarItem[] wanted = _vm.IsSelecting ? [_all, _edit, _cancel] : [_select, _organize];
		if (ToolbarItems.SequenceEqual(wanted)) return;
		ToolbarItems.Clear();
		foreach (var t in wanted) ToolbarItems.Add(t);
	}

	protected override async void OnAppearing()
	{
		base.OnAppearing();
		if (_vm.Groups.Count == 0 || _seenVersion != _viewer.Version)
		{
			_seenVersion = _viewer.Version;
			await _vm.ReloadAsync();
		}
	}
}

public sealed class BinPage : LibraryPage
{
	public BinPage(BinViewModel vm, ViewerState viewer) : base(vm, viewer)
	{
		Title = "Recycle bin";
		ToolbarItems.Add(new ToolbarItem { Text = "Empty bin", Command = vm.EmptyCommand });
	}
}

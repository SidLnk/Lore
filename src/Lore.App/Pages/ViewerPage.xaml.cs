using Lore.App.Services;
using Lore.Client;

namespace Lore.App.Pages;

/// <summary>Fullscreen preview; swipe left/right to move through the list it was opened from.</summary>
public partial class ViewerPage : ContentPage
{
	readonly ViewerState _state;
	readonly Session _session;
	readonly ToolbarItem _favorite = new();
	readonly ToolbarItem _remove = new();

	public ViewerPage(ViewerState state, Session session)
	{
		InitializeComponent();
		_state = state;
		_session = session;
		_favorite.Clicked += OnFavorite;
		_remove.Text = state.FromBin ? "Restore" : "Delete";
		_remove.Clicked += OnRemove;
		if (!state.FromBin)
		{
			ToolbarItems.Add(_favorite);
			ToolbarItems.Add(new ToolbarItem { Text = "Info", Command = new Command(async () => await Shell.Current.GoToAsync("info")) });
		}
		ToolbarItems.Add(_remove);
		Show();
	}

	MediaItem? Current => _state.Items.ElementAtOrDefault(_state.Index);

	void OnNext(object? sender, SwipedEventArgs e) => Move(1);
	void OnPrevious(object? sender, SwipedEventArgs e) => Move(-1);

	void Move(int delta)
	{
		var next = _state.Index + delta;
		if (next < 0 || next >= _state.Items.Count) return;
		_state.Index = next;
		Show();
	}

	void Show()
	{
		if (Current is not { } item) return;
		Photo.Source = item.PreviewUrl;
		PlayButton.IsVisible = item.IsVideo;
		_favorite.Text = item.Favorite ? "♥ Favorite" : "♡ Favorite";
		var owner = _session.MemberName(item.Dto.OwnerId);
		Title = $"{item.Dto.DateTaken:d MMM yyyy, HH:mm}{(owner == null ? "" : " · " + owner)}";
	}

	async void OnFavorite(object? sender, EventArgs e)
	{
		if (Current is not { } item) return;
		if (!await TryAsync(() => _session.Api!.SetFavoriteAsync(item.Dto.Id, !item.Favorite))) return;
		item.Favorite = !item.Favorite;
		_state.Version++;
		Show();
	}

	/// <summary>Delete (to recycle bin) or restore from it; either way the item leaves this list.</summary>
	async void OnRemove(object? sender, EventArgs e)
	{
		if (Current is not { } item) return;
		var api = _session.Api!;
		if (!await TryAsync(() => _state.FromBin ? api.RestoreAsync(item.Dto.Id) : api.DeleteAsync(item.Dto.Id))) return;
		_state.Version++;
		_state.Items.RemoveAt(_state.Index);
		if (_state.Items.Count == 0)
		{
			await Shell.Current.GoToAsync("..");
			return;
		}
		_state.Index = Math.Min(_state.Index, _state.Items.Count - 1);
		Show();
	}

	async Task<bool> TryAsync(Func<Task> action)
	{
		try
		{
			await action();
			return true;
		}
		catch (Exception e) when (e is HttpRequestException or TaskCanceledException or LoreApiException)
		{
			await DisplayAlertAsync("Lore", "Couldn't reach Lore. Are you on home Wi-Fi?", "OK");
			return false;
		}
	}

	async void OnPlay(object? sender, EventArgs e) => await Shell.Current.GoToAsync("video");
}

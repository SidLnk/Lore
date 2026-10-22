using Lore.App.Services;

namespace Lore.App.Pages;

public partial class PairingPage : ContentPage
{
	readonly Session _session;

	public PairingPage(Session session)
	{
		InitializeComponent();
		Adaptive.CapWidth(this, 560);
		_session = session;
		NameEntry.Text = DeviceInfo.Current.Name;
	}

	async void OnPair(object? sender, EventArgs e)
	{
		if (string.IsNullOrWhiteSpace(ServerEntry.Text) || CodeEntry.Text?.Trim().Length != 6 || string.IsNullOrWhiteSpace(NameEntry.Text))
		{
			ShowError("Enter the server, the 6-digit code and a phone name.");
			return;
		}

		await RunAsync(async () =>
		{
			await _session.PairAsync(ServerEntry.Text, CodeEntry.Text, NameEntry.Text);
			PairForm.IsVisible = false;
			OwnerForm.IsVisible = true;
			OwnerButtons.Clear();
			foreach (var member in _session.Family)
			{
				// Equal choices, so none is the filled primary action.
				var b = new Button { Text = member.Name, Style = (Style)Application.Current!.Resources["SecondaryButton"] };
				b.Clicked += async (_, _) => await RunAsync(async () =>
				{
					await _session.SetDefaultOwnerAsync(member.Id);
					((App)Application.Current!).ShowMain(Window);
				});
				OwnerButtons.Add(b);
			}
		});
	}

	async Task RunAsync(Func<Task> action)
	{
		ErrorLabel.IsVisible = false;
		Busy.IsRunning = true;
		PairButton.IsEnabled = false;
		try
		{
			await action();
		}
		catch (Exception ex)
		{
			ShowError(ex is HttpRequestException or TaskCanceledException
				? "Can't reach the server. Check you're on home Wi-Fi and the address is right (try the PC's IP address)."
				: ex.Message);
		}
		finally
		{
			Busy.IsRunning = false;
			PairButton.IsEnabled = true;
		}
	}

	void ShowError(string message)
	{
		ErrorLabel.Text = message;
		ErrorLabel.IsVisible = true;
	}
}

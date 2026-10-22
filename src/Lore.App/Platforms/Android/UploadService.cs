using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Net;
using Android.Net.Wifi;
using Android.OS;
using AndroidX.Core.App;
using Lore.App.Services;
using Lore.Client;

namespace Lore.App.Droid;

/// <summary>
/// Foreground service that drains the upload queue, then any Save to phone downloads, so both continue with the app
/// in the background or the screen off. Stops itself when there is nothing left to do.
/// </summary>
[Service(Exported = false, ForegroundServiceType = ForegroundService.TypeDataSync)]
public sealed class UploadService : Service
{
	const string Channel = "uploads";
	const int NotificationId = 1, SummaryId = 2;
	const string UploadTitle = "Uploading to Lore", SaveTitle = "Saving to phone", CheckTitle = "Checking this phone";

	static int _running;
	CancellationTokenSource? _cts;
	PowerManager.WakeLock? _wake;
	WifiManager.WifiLock? _wifi;

	public static void Start()
	{
		var ctx = Platform.AppContext;
		var intent = new Intent(ctx, typeof(UploadService));
		if (OperatingSystem.IsAndroidVersionAtLeast(26)) ctx.StartForegroundService(intent);
		else ctx.StartService(intent);
	}

	public override IBinder? OnBind(Intent? intent) => null;

	public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
	{
		EnsureChannel();
		var notification = Build("Lore", "Starting…", 0, 0);
		if (OperatingSystem.IsAndroidVersionAtLeast(29)) StartForeground(NotificationId, notification, ForegroundService.TypeDataSync);
		else StartForeground(NotificationId, notification);

		// Already draining: the running loop re-reads the queue, so new items get picked up.
		if (Interlocked.Exchange(ref _running, 1) == 1) return StartCommandResult.NotSticky;

		_cts = new CancellationTokenSource();
		AcquireLocks();
		_ = Task.Run(() => DrainAsync(_cts.Token));
		return StartCommandResult.NotSticky;
	}

	async Task DrainAsync(CancellationToken ct)
	{
		try
		{
			var services = IPlatformApplication.Current!.Services;
			var uploads = services.GetRequiredService<Uploads>();
			var saves = services.GetRequiredService<Saves>();
			var freeUp = services.GetRequiredService<FreeUp>();
			var engine = uploads.CreateEngine();
			engine.ItemChanged += Notify;
			do
			{
				if (await uploads.Queue.NextAsync() != null)
				{
					if (!Uploads.OnLan) Update(Build(UploadTitle, "Waiting for home Wi-Fi…", 0, 0));
					await engine.RunAsync(ct);
				}
				var summary = await saves.DrainAsync((name, done, total) => Update(name == null
					? Build(SaveTitle, "Waiting for home Wi-Fi…", 0, 0)
					: Build(SaveTitle, $"{name} · {done + 1} of {total}", total, done)), ct);
				if (!string.IsNullOrEmpty(summary)) ShowSummary(SaveTitle, summary);
				var checkedPhone = await freeUp.CheckPhoneAsync((done, total) =>
					Update(Build(CheckTitle, $"{done + 1:N0} of {total:N0}", total, done)), ct);
				if (checkedPhone != null) ShowSummary(CheckTitle, checkedPhone);
			} while (!ct.IsCancellationRequested && (await uploads.Queue.NextAsync() != null || saves.HasWork || freeUp.HasWork));
		}
		catch (System.OperationCanceledException) { }
		catch (Exception e)
		{
			Android.Util.Log.Error("Lore", $"Upload service failed: {e}");
		}
		finally
		{
			ReleaseLocks();
			Interlocked.Exchange(ref _running, 0);
			StopForeground(StopForegroundFlags.Remove);
			StopSelf();
		}
	}

	void Notify(QueueItem item)
	{
		var pct = item.Size == 0 ? 0 : (int)(100 * item.Offset / item.Size);
		var text = item.Status switch
		{
			UploadStatus.Uploading => $"{item.FileName} · {pct}%",
			UploadStatus.Failed => $"{item.FileName} failed: {item.Error}",
			UploadStatus.Pending when item.Error != null => $"Retrying {item.FileName}…",
			_ => item.FileName,
		};
		Update(Build(UploadTitle, text, 100, pct));
	}

	Notification Build(string title, string text, int max, int progress) =>
		new NotificationCompat.Builder(this, Channel)
			.SetContentTitle(title)!
			.SetContentText(text)!
			.SetSmallIcon(Resource.Mipmap.appicon)!
			.SetOngoing(true)!
			.SetOnlyAlertOnce(true)!
			.SetProgress(max, progress, max == 0)!
			.Build()!;

	void Update(Notification n) => NotificationManagerCompat.From(this)!.Notify(NotificationId, n);

	/// <summary>Stays after the service stops, so the family can see what a save did.</summary>
	void ShowSummary(string title, string text) => NotificationManagerCompat.From(this)!.Notify(SummaryId,
		new NotificationCompat.Builder(this, Channel)
			.SetContentTitle(title)!
			.SetContentText(text)!
			.SetStyle(new NotificationCompat.BigTextStyle().BigText(text))!
			.SetSmallIcon(Resource.Mipmap.appicon)!
			.SetAutoCancel(true)!
			.Build()!);

	void EnsureChannel()
	{
		if (!OperatingSystem.IsAndroidVersionAtLeast(26)) return;
		var nm = (NotificationManager)GetSystemService(NotificationService)!;
		nm.CreateNotificationChannel(new NotificationChannel(Channel, "Uploads and saves", NotificationImportance.Low));
	}

	void AcquireLocks()
	{
		_wake = ((PowerManager)GetSystemService(PowerService)!).NewWakeLock(WakeLockFlags.Partial, "lore:upload");
		_wake!.Acquire();
		var mode = OperatingSystem.IsAndroidVersionAtLeast(29) ? WifiMode.FullLowLatency : WifiMode.FullHighPerf;
		_wifi = ((WifiManager)GetSystemService(WifiService)!).CreateWifiLock(mode, "lore:upload");
		_wifi!.Acquire();
	}

	void ReleaseLocks()
	{
		if (_wake?.IsHeld == true) _wake.Release();
		if (_wifi?.IsHeld == true) _wifi.Release();
	}

	public override void OnDestroy()
	{
		_cts?.Cancel();
		base.OnDestroy();
	}
}

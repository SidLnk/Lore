using System.Security.Cryptography;
using Lore.Client;
using Lore.Contracts;

namespace Lore.App.Services;

/// <summary>
/// In-app updates from the home server (never the internet): the admin uploads a signed APK on the admin page,
/// phones on home Wi-Fi see it, download it, check its SHA-256 and hand it to Android's installer.
/// </summary>
public sealed class AppUpdater(Session session)
{
	/// <summary>A newer version than this one, as of the last check.</summary>
	public AppVersionDto? Available { get; private set; }

	public static string CurrentVersion => AppInfo.Current.VersionString;
	static int CurrentCode => int.TryParse(AppInfo.Current.BuildString, out var v) ? v : 0;

	public async Task<AppVersionDto?> CheckAsync()
	{
		if (!Uploads.OnLan || session.Api is not { } api) return Available;
		try
		{
			var offered = await api.GetAppVersionAsync();
			Available = offered is { } v && v.VersionCode > CurrentCode ? v : null;
		}
		catch (Exception e) when (e is HttpRequestException or TaskCanceledException or LoreApiException)
		{
			// Off the home network or server down: keep the last answer.
		}
		return Available;
	}

#if ANDROID
	/// <summary>Downloads and verifies the update, then opens Android's install prompt.</summary>
	/// <returns>False when Android first needs "Install unknown apps" allowed for Lore (its settings page is opened).</returns>
	public async Task<bool> InstallAsync(AppVersionDto v, Action<double> progress, CancellationToken ct)
	{
		if (!Droid.Installer.CanInstall())
		{
			Droid.Installer.OpenInstallPermissionSettings();
			return false;
		}
		var api = session.Api ?? throw new InvalidOperationException("This phone isn't paired");
		var dir = Directory.CreateDirectory(Path.Combine(FileSystem.CacheDirectory, "update")).FullName;
		var path = Path.Combine(dir, "lore.apk");
		try
		{
			await using (var source = await api.OpenAppApkAsync(ct))
			await using (var file = File.Create(path))
			using (var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
			{
				var buffer = new byte[81920];
				long done = 0;
				int n;
				while ((n = await source.ReadAsync(buffer, ct)) > 0)
				{
					hash.AppendData(buffer, 0, n);
					await file.WriteAsync(buffer.AsMemory(0, n), ct);
					progress(v.Size == 0 ? 0 : (double)(done += n) / v.Size);
				}
				if (!Convert.ToHexStringLower(hash.GetHashAndReset()).Equals(v.Sha256, StringComparison.OrdinalIgnoreCase))
					throw new IOException("The download didn't match the version on the home PC");
			}
		}
		catch
		{
			File.Delete(path);
			throw;
		}
		Droid.Installer.Install(path);
		return true;
	}
#endif
}

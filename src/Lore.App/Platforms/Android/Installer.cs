using Android.Content;
using Android.Provider;
using Uri = Android.Net.Uri;

namespace Lore.App.Droid;

/// <summary>Hands a downloaded APK to Android's package installer, which asks the user to confirm the update.</summary>
public static class Installer
{
	static Context Ctx => Platform.AppContext;

	/// <summary>Whether the user has allowed Lore to install apps ("Install unknown apps").</summary>
	public static bool CanInstall() => Ctx.PackageManager!.CanRequestPackageInstalls();

	public static void OpenInstallPermissionSettings() =>
		Ctx.StartActivity(new Intent(Settings.ActionManageUnknownAppSources, Uri.Parse("package:" + Ctx.PackageName))
			.AddFlags(ActivityFlags.NewTask));

	public static void Install(string apkPath)
	{
		// MAUI Essentials already declares this FileProvider, with the cache directory shared.
		var uri = AndroidX.Core.Content.FileProvider.GetUriForFile(Ctx, Ctx.PackageName + ".fileProvider", new Java.IO.File(apkPath));
		var intent = new Intent(Intent.ActionView)
			.SetDataAndType(uri, "application/vnd.android.package-archive")
			.AddFlags(ActivityFlags.GrantReadUriPermission | ActivityFlags.NewTask);
		Ctx.StartActivity(intent);
	}
}

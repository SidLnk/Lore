using Android.App;
using Android.Content.PM;
using Android.OS;

namespace Lore.App;

[Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, LaunchMode = LaunchMode.SingleTop, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity
{
	protected override void OnActivityResult(int requestCode, Result resultCode, Android.Content.Intent? data)
	{
		base.OnActivityResult(requestCode, resultCode, data);
		Droid.DeviceGallery.OnActivityResult(requestCode, resultCode);
	}
}

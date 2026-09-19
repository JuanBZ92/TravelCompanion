using Android.App;
using Android.Content.PM;
using Android.OS;
using Android.Views;
using AndroidX.AppCompat.App;

namespace TravelCompanion.Mobile;

[Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, LaunchMode = LaunchMode.SingleTop,
	WindowSoftInputMode = SoftInput.AdjustResize,
	ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity
{
	protected override void OnCreate(Bundle? savedInstanceState)
	{
		AppCompatDelegate.DefaultNightMode = AppCompatDelegate.ModeNightNo;
		base.OnCreate(savedInstanceState);
	}
}

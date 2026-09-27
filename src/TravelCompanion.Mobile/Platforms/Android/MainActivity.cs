using Android.App;
using Android.Content.PM;
using Android.OS;
using Android.Views;
using AndroidX.AppCompat.App;
using AndroidX.Activity;
using TravelCompanion.Mobile.Services;

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
        Platforms.Android.JournalFileSaver.Register(this);
		OnBackPressedDispatcher.AddCallback(this, new NavigationBackCallback(this));
	}

    private sealed class NavigationBackCallback(MainActivity activity) : OnBackPressedCallback(true)
    {
        private readonly ExitBackPressGuard _exit = new();
        private bool _handling;
        public override async void HandleOnBackPressed()
        {
            if (_handling) return;
            _handling = true;
            try
            {
                var window = Microsoft.Maui.Controls.Application.Current?.Windows.FirstOrDefault();
                if (window is Microsoft.Maui.IWindow mauiWindow && mauiWindow.BackButtonClicked())
                {
                    _exit.Reset();
                    return;
                }
                if (Shell.Current is AppShell shell && await shell.TryPreviousTabAsync())
                {
                    _exit.Reset();
                    return;
                }
                if (_exit.ShouldExit(DateTimeOffset.UtcNow)) activity.Finish();
                else Android.Widget.Toast.MakeText(activity,
                    LocalizationResourceManager.Instance["BackAgainToClose"], Android.Widget.ToastLength.Short)?.Show();
            }
            catch (Exception exception)
            {
                _exit.Reset();
                System.Diagnostics.Trace.TraceError($"Back navigation failed: {exception}");
            }
            finally { _handling = false; }
        }
    }
}

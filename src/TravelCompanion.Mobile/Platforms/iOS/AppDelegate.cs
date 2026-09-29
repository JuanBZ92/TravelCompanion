using Foundation;

namespace TravelCompanion.Mobile;

[Register("AppDelegate")]
public class AppDelegate : MauiUIApplicationDelegate
{
    private NSObject? memoryWarningObserver;

    protected override MauiApp CreateMauiApp()
    {
        var app = MauiProgram.CreateMauiApp();
        memoryWarningObserver ??= UIKit.UIApplication.Notifications.ObserveDidReceiveMemoryWarning(
            (_, _) => TravelCompanion.Mobile.Services.ClientDiagnostics.Record("ios_memory_warning"));
        return app;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { memoryWarningObserver?.Dispose(); memoryWarningObserver = null; }
        base.Dispose(disposing);
    }
}

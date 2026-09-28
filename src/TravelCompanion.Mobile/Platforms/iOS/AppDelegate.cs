using Foundation;

namespace TravelCompanion.Mobile;

[Register("AppDelegate")]
public class AppDelegate : MauiUIApplicationDelegate
{
	protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();

    public override void ReceiveMemoryWarning(UIKit.UIApplication application)
    {
        Services.ClientDiagnostics.Record("ios_memory_warning");
        base.ReceiveMemoryWarning(application);
    }
}

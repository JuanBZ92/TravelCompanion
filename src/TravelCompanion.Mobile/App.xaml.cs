namespace TravelCompanion.Mobile;

public partial class App : Application
{
	public App()
	{
		TravelCompanion.Mobile.Services.LocalizationResourceManager.Instance.Initialize();
        try { TravelCompanion.Mobile.Services.TripDocumentStore.ClearPreviewsAsync().GetAwaiter().GetResult(); }
        catch (IOException) { /* A viewer can still hold a temporary file after an app restart. */ }
        InitializeComponent();
		UserAppTheme = AppTheme.Light;
	}

	protected override Window CreateWindow(IActivationState? activationState)
	{
		var window = new Window(new AppShell());
        window.Created += (_, _) => Services.ClientDiagnostics.Record("window_created");
        window.Deactivated += (_, _) => Services.ClientDiagnostics.Record("window_deactivated");
        window.Stopped += (_, _) => Services.ClientDiagnostics.Record("window_stopped");
        window.Resumed += (_, _) => Services.ClientDiagnostics.Record("window_resumed");
        window.Destroying += (_, _) => Services.ClientDiagnostics.Record("window_destroying");
        PageAppearing += (_, page) => Services.ClientDiagnostics.Record("page_appearing", new() { Page = page.GetType().Name });
		var syncCoordinator = MauiProgram.Services.GetRequiredService<TravelCompanion.Mobile.Services.OfflineSyncCoordinator>();
		var purchaseRecovery = MauiProgram.Services.GetRequiredService<TravelCompanion.Mobile.Services.StorePurchaseRecoveryService>();
		syncCoordinator.Start();
		var reminders = MauiProgram.Services.GetRequiredService<TravelCompanion.Mobile.Services.ReservationReminderService>();
		reminders.Start();
		window.Activated += async (_, _) =>
		{
            Services.ClientDiagnostics.Record("window_activated");
			syncCoordinator.TriggerSynchronize();
			await purchaseRecovery.RecoverAsync();
			reminders.Refresh();
		};
		return window;
	}
}

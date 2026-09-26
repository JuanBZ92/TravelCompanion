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
		var syncCoordinator = MauiProgram.Services.GetRequiredService<TravelCompanion.Mobile.Services.OfflineSyncCoordinator>();
		var purchaseRecovery = MauiProgram.Services.GetRequiredService<TravelCompanion.Mobile.Services.StorePurchaseRecoveryService>();
		syncCoordinator.Start();
		var reminders = MauiProgram.Services.GetRequiredService<TravelCompanion.Mobile.Services.ReservationReminderService>();
		reminders.Start();
		window.Activated += async (_, _) =>
		{
			syncCoordinator.TriggerSynchronize();
			await purchaseRecovery.RecoverAsync();
			reminders.Refresh();
		};
		return window;
	}
}

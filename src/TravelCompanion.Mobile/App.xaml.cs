namespace TravelCompanion.Mobile;

public partial class App : Application
{
	public App()
	{
		TravelCompanion.Mobile.Services.LocalizationResourceManager.Instance.Initialize();
        var cleanupStart = System.Diagnostics.Stopwatch.GetTimestamp();
        var cleanupAllocation = GC.GetAllocatedBytesForCurrentThread();
        try { TravelCompanion.Mobile.Services.TripDocumentStore.ClearPreviewsAsync().GetAwaiter().GetResult(); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Services.ClientDiagnostics.Record("preview_cleanup_failed", exception: exception);
        }
        finally
        {
            var cleanupTicks = System.Diagnostics.Stopwatch.GetTimestamp() - cleanupStart;
            Services.ClientDiagnostics.Record("preview_cleanup_measured", new() { ElapsedTicks = cleanupTicks,
                TickFrequency = System.Diagnostics.Stopwatch.Frequency,
                ElapsedMs = (long)System.Diagnostics.Stopwatch.GetElapsedTime(cleanupStart).TotalMilliseconds,
                AllocatedBytes = GC.GetAllocatedBytesForCurrentThread() - cleanupAllocation });
        }
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
            try
            {
			    syncCoordinator.TriggerSynchronize();
			    await purchaseRecovery.RecoverAsync();
			    reminders.Refresh();
            }
            catch (OperationCanceledException exception)
            {
                // A network timeout or lifecycle cancellation must not escape async void.
                Services.ClientDiagnostics.Record("activation_canceled", exception: exception);
            }
            catch (Exception exception)
            {
                Services.ClientDiagnostics.Record("activation_failed", exception: exception);
            }
		};
		return window;
	}
}

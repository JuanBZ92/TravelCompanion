namespace TravelCompanion.Mobile;

public partial class App : Application
{
	public App()
	{
		TravelCompanion.Mobile.Services.LocalizationResourceManager.Instance.Initialize();
		InitializeComponent();
		UserAppTheme = AppTheme.Light;
	}

	protected override Window CreateWindow(IActivationState? activationState)
	{
		var window = new Window(new AppShell());
		var syncCoordinator = MauiProgram.Services.GetRequiredService<TravelCompanion.Mobile.Services.OfflineSyncCoordinator>();
		syncCoordinator.Start();
		window.Activated += (_, _) => syncCoordinator.TriggerSynchronize();
		return window;
	}
}

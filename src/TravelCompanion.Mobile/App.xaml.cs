namespace TravelCompanion.Mobile;

public partial class App : Application
{
	public App()
	{
		TravelCompanion.Mobile.Services.LocalizationResourceManager.Instance.Initialize();
		InitializeComponent();
	}

	protected override Window CreateWindow(IActivationState? activationState)
	{
		return new Window(new AppShell());
	}
}

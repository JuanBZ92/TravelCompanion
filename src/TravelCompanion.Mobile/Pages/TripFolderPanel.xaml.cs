using TravelCompanion.Mobile.Services;
using TravelCompanion.Mobile.ViewModels;

namespace TravelCompanion.Mobile.Pages;

public partial class TripFolderPanel : ContentView
{
    private readonly AuthSessionService sessions = MauiProgram.Services.GetRequiredService<AuthSessionService>();
    private TripPreparationViewModel? viewModel;
    private long contextVersion;
    public TripFolderPanel() { InitializeComponent(); BindingContext = null; }
    public async Task ActivateAsync()
    {
        Deactivate();
        contextVersion = sessions.ContextVersion;
        sessions.StateChanged += SessionChanged;
        BindingContext = viewModel = MauiProgram.Services.GetRequiredService<TripPreparationViewModel>();
        await viewModel.LoadPreparationCommand.ExecuteAsync(null);
    }
    public void Deactivate()
    {
        sessions.StateChanged -= SessionChanged;
        viewModel?.CancelLoading();
        viewModel = null;
        BindingContext = null;
    }
    private void SessionChanged(object? sender, EventArgs e)
    {
        if (contextVersion != sessions.ContextVersion || !sessions.HasSession)
            Dispatcher.Dispatch(Deactivate);
    }
}

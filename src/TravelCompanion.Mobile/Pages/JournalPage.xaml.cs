using TravelCompanion.Mobile.ViewModels;

namespace TravelCompanion.Mobile.Pages;

public partial class JournalPage : ContentPage
{
    private readonly JournalViewModel _viewModel;
    private bool _openingSearch;
    private async void OnSearchClicked(object? sender, EventArgs e)
    {
        var session = MauiProgram.Services.GetRequiredService<Services.AuthSessionService>();
        if (_openingSearch || !session.HasSession || session.CurrentTripId is null) return;
        var context = session.ContextVersion;
        _openingSearch = true;
        try { await Navigation.PushModalAsync(new TripSearchPage(Services.TripSearchKind.Memory)); }
        catch (OperationCanceledException) { }
        catch (Exception error)
        {
            Services.ClientDiagnostics.Record("journal_search_open_failed", exception: error);
            if (session.ContextVersion == context && session.HasSession)
                await DisplayAlertAsync("YUKU", Services.LocalizationResourceManager.Instance["UxActionFailed"], "OK");
        }
        finally { _openingSearch = false; }
    }
    public JournalPage()
    {
        InitializeComponent();
        BindingContext = _viewModel = MauiProgram.Services.GetRequiredService<JournalViewModel>();
    }
    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.LoadAsync();
    }
    protected override void OnDisappearing()
    {
        _viewModel.CancelLoading();
        base.OnDisappearing();
    }
    private void OnJournalScrolled(object? sender, ItemsViewScrolledEventArgs e) =>
        _viewModel.SetVisibleRange(e.FirstVisibleItemIndex, e.LastVisibleItemIndex);
}

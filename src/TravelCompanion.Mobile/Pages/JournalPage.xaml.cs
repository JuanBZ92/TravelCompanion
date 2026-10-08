using TravelCompanion.Mobile.ViewModels;
using TravelCompanion.Mobile.Services;

namespace TravelCompanion.Mobile.Pages;

public partial class JournalPage : ContentPage
{
    private readonly JournalViewModel _viewModel;
    private readonly JournalStore _store;
    private CancellationTokenSource? _synchronizationObservation;
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
        _store = MauiProgram.Services.GetRequiredService<JournalStore>();
    }
    protected override async void OnAppearing()
    {
        base.OnAppearing();
        _synchronizationObservation?.Cancel();
        _synchronizationObservation?.Dispose();
        _synchronizationObservation = new();
        _store.SynchronizationChanged -= OnSynchronizationChanged;
        _store.SynchronizationChanged += OnSynchronizationChanged;
        await _viewModel.LoadAsync();
    }
    protected override void OnDisappearing()
    {
        _store.SynchronizationChanged -= OnSynchronizationChanged;
        _synchronizationObservation?.Cancel();
        _viewModel.CancelLoading();
        base.OnDisappearing();
    }
    private void OnSynchronizationChanged(object? sender, JournalScope scope)
    {
        if (!_store.IsCurrent(scope) || _store.GetSynchronizationState(scope) == JournalSynchronizationState.Synchronizing) return;
        Dispatcher.Dispatch(async () =>
        {
            if (_synchronizationObservation is not { } observation || observation.IsCancellationRequested) return;
            var ct = observation.Token;
            try
            {
                if (!ct.IsCancellationRequested && _store.IsCurrent(scope))
                    await _viewModel.RefreshLocalSynchronizationAsync(scope, ct);
            }
            catch (OperationCanceledException) { }
            catch (Exception error) { ClientDiagnostics.Record("journal_sync_refresh_failed", exception: error); }
        });
    }
    private void OnJournalScrolled(object? sender, ItemsViewScrolledEventArgs e) =>
        _viewModel.SetVisibleRange(e.FirstVisibleItemIndex, e.LastVisibleItemIndex);
}

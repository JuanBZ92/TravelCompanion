using System.ComponentModel;

namespace TravelCompanion.Mobile.Pages;

public partial class DocsPage : ContentPage, IQueryAttributable
{
    private readonly ViewModels.DocsViewModel _viewModel;
    private bool _openingSearch;

    private async void OnSearchClicked(object? sender, EventArgs e)
    {
        var sessions = MauiProgram.Services.GetRequiredService<Services.AuthSessionService>();
        if (_openingSearch || !sessions.HasSession || sessions.CurrentUserId is null || sessions.CurrentTripId is null) return;
        var user = sessions.CurrentUserId;
        var trip = sessions.CurrentTripId;
        var version = sessions.ContextVersion;
        bool IsCurrent() => sessions.HasSession && sessions.CurrentUserId == user
            && sessions.CurrentTripId == trip && sessions.ContextVersion == version;
        _openingSearch = true;
        try
        {
            var search = new TripSearchPage(Services.TripSearchKind.Document);
            if (!IsCurrent()) return;
            // TripScopedPage also hides and dismisses the modal if this context changes after navigation.
            await Navigation.PushModalAsync(search);
        }
        catch (OperationCanceledException) { }
        catch (Exception error)
        {
            Services.ClientDiagnostics.Record("documents_search_open_failed", exception: error);
            if (IsCurrent())
                await DisplayAlertAsync("YUKU", Services.LocalizationResourceManager.Instance["UxActionFailed"], "OK");
        }
        finally { _openingSearch = false; }
    }

    public DocsPage()
        : this(MauiProgram.Services.GetRequiredService<ViewModels.DocsViewModel>())
    {
    }

    public DocsPage(ViewModels.DocsViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
        _viewModel.SetCategory(null);
    }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        _viewModel.SetCategory(query.TryGetValue("DocumentCategory", out var value)
            && value is Services.LocalDocumentCategory category ? category : null);
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        _viewModel.PropertyChanged -= OnDocumentStateChanged;
        _viewModel.PropertyChanged += OnDocumentStateChanged;
        if (_viewModel.HasError) EditorialUi.RevealError(DocumentError);
        if (!_viewModel.IsBusy)
        {
            await _viewModel.LoadCommand.ExecuteAsync(null);
        }
    }

    protected override void OnDisappearing()
    {
        _viewModel.PropertyChanged -= OnDocumentStateChanged;
        _viewModel.CancelLoading();
        base.OnDisappearing();
    }

    private void OnDocumentStateChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(_viewModel.HasError) && _viewModel.HasError)
            EditorialUi.RevealError(DocumentError);
    }
}

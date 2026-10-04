using TravelCompanion.Mobile.ViewModels;

namespace TravelCompanion.Mobile.Pages;

public partial class ImproveDayPage : TripScopedPage, IQueryAttributable
{
    private readonly DayPlannerViewModel viewModel;
    private DateOnly? date;
    private bool loaded;
    public ImproveDayPage(DayPlannerViewModel viewModel)
    { InitializeComponent(); BindingContext = this.viewModel = viewModel; }
    public void ApplyQueryAttributes(IDictionary<string, object> query)
    { if (query.TryGetValue("Date", out var value) && value is DateOnly selected) date = selected; }
    protected override async void OnAppearing()
    {
        base.OnAppearing();
        Connectivity.Current.ConnectivityChanged += OnConnectivityChanged;
        if (!loaded) { loaded = true; await viewModel.InitializeAsync(date); }
        else await viewModel.RefreshAsync();
    }
    protected override async void OnDisappearing()
    {
        Connectivity.Current.ConnectivityChanged -= OnConnectivityChanged;
        viewModel.CancelLoadingOperation(); await viewModel.SaveDraftAsync(); base.OnDisappearing();
    }
    private void OnConnectivityChanged(object? sender, ConnectivityChangedEventArgs e) =>
        Dispatcher.Dispatch(viewModel.NotifyNetworkState);
    private async void OnBackClicked(object? sender, EventArgs e) => await Shell.Current.GoToAsync("..");
}

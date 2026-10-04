using TravelCompanion.Mobile.ViewModels;

namespace TravelCompanion.Mobile.Pages;

public partial class DayPlanProposalPage : TripScopedPage, IQueryAttributable
{
    private DayPlannerViewModel? viewModel;
    public DayPlanProposalPage() => InitializeComponent();
    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("Planner", out var value) && value is DayPlannerViewModel planner)
            BindingContext = viewModel = planner;
    }
    protected override void OnAppearing()
    {
        base.OnAppearing();
        Connectivity.Current.ConnectivityChanged += OnConnectivityChanged;
        viewModel?.NotifyNetworkState();
    }
    protected override async void OnDisappearing()
    {
        Connectivity.Current.ConnectivityChanged -= OnConnectivityChanged;
        viewModel?.CancelLoadingOperation();
        if (viewModel is not null) await viewModel.SaveDraftAsync();
        base.OnDisappearing();
    }
    private void OnConnectivityChanged(object? sender, ConnectivityChangedEventArgs e) =>
        Dispatcher.Dispatch(() => viewModel?.NotifyNetworkState());
    private async void OnBackClicked(object? sender, EventArgs e) => await Shell.Current.GoToAsync("..");
}

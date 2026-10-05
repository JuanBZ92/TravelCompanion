using System.ComponentModel;
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
        if (viewModel is not null)
        {
            viewModel.PropertyChanged += OnPlannerStateChanged;
            if (viewModel.HasError) EditorialUi.RevealError(PlannerError);
        }
        Connectivity.Current.ConnectivityChanged += OnConnectivityChanged;
        viewModel?.NotifyNetworkState();
    }
    protected override async void OnDisappearing()
    {
        Connectivity.Current.ConnectivityChanged -= OnConnectivityChanged;
        if (viewModel is not null) viewModel.PropertyChanged -= OnPlannerStateChanged;
        viewModel?.CancelLoadingOperation();
        if (viewModel is not null) await viewModel.SaveDraftAsync();
        base.OnDisappearing();
    }
    private void OnConnectivityChanged(object? sender, ConnectivityChangedEventArgs e) =>
        Dispatcher.Dispatch(() => viewModel?.NotifyNetworkState());
    private void OnPlannerStateChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DayPlannerViewModel.HasError) && viewModel?.HasError == true)
            EditorialUi.RevealError(PlannerError);
    }
    private async void OnBackClicked(object? sender, EventArgs e) => await Shell.Current.GoToAsync("..");
}

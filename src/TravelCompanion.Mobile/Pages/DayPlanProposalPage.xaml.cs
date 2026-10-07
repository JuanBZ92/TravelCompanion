using System.ComponentModel;
using TravelCompanion.Mobile.Services;
using TravelCompanion.Mobile.ViewModels;

namespace TravelCompanion.Mobile.Pages;

public partial class DayPlanProposalPage : TripScopedPage, IQueryAttributable
{
    private DayPlannerViewModel? viewModel;
    private int appearanceVersion;
    public DayPlanProposalPage() => InitializeComponent();
    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("Planner", out var value) && value is DayPlannerViewModel planner)
            BindingContext = viewModel = planner;
    }
    protected override void OnAppearing()
    {
        appearanceVersion++;
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
        appearanceVersion++;
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
    private async void OnMenuClicked(object? sender, EventArgs e)
    {
        if (viewModel is not { IsBusy: false } planner) return;
        var sessions = MauiProgram.Services.GetRequiredService<AuthSessionService>();
        var context = sessions.ContextVersion;
        var appearance = appearanceVersion;
        var actions = planner.CanCompare ? new[] { planner.CompareLabel, planner.NewProposalLabel } : new[] { planner.NewProposalLabel };
        var choice = await DisplayActionSheetAsync(DayPlannerViewModel.Text("UxMoreActions"), planner.CancelLabel, null, actions);
        if (!sessions.HasSession || context != sessions.ContextVersion || appearance != appearanceVersion
            || !ReferenceEquals(viewModel, planner) || Shell.Current.CurrentPage != this || planner.IsBusy) return;
        if (choice == planner.CompareLabel) planner.ToggleComparisonCommand.Execute(null);
        else if (choice == planner.NewProposalLabel) await Shell.Current.GoToAsync("..");
    }
}

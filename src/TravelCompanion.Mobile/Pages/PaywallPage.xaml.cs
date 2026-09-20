using TravelCompanion.Mobile.ViewModels;

namespace TravelCompanion.Mobile.Pages;

[QueryProperty(nameof(EntryPoint), "EntryPoint")]
public partial class PaywallPage : ContentPage
{
    private readonly PaywallViewModel _viewModel;
    public string? EntryPoint { set => _viewModel.SetEntryPoint(value); }
    public PaywallPage(PaywallViewModel viewModel) { InitializeComponent(); BindingContext = _viewModel = viewModel; }
    protected override async void OnAppearing() { base.OnAppearing(); if (!_viewModel.HasLoaded) await _viewModel.LoadOfferCommand.ExecuteAsync(null); }
}

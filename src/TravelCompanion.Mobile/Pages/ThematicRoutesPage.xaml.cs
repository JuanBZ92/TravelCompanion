using TravelCompanion.Mobile.ViewModels;
namespace TravelCompanion.Mobile.Pages;
public partial class ThematicRoutesPage : ContentPage
{
    private readonly ThematicRoutesViewModel _viewModel;
    public ThematicRoutesPage(ThematicRoutesViewModel viewModel) { InitializeComponent(); BindingContext = _viewModel = viewModel; }
    protected override async void OnAppearing() { base.OnAppearing(); if (!_viewModel.HasLoaded) await _viewModel.LoadRoutesCommand.ExecuteAsync(null); }
}

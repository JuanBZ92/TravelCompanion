using TravelCompanion.Mobile.ViewModels;

namespace TravelCompanion.Mobile.Pages;

public partial class BuilderSetupPage : ContentPage
{
    private readonly BuilderSetupViewModel _viewModel;
    public BuilderSetupPage(BuilderSetupViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (!_viewModel.HasLoaded) await _viewModel.LoadSetupCommand.ExecuteAsync(null);
    }
}

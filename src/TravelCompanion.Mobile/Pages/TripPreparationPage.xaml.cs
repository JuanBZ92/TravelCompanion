using TravelCompanion.Mobile.ViewModels;

namespace TravelCompanion.Mobile.Pages;

public partial class TripPreparationPage : TripScopedPage
{
    private readonly TripPreparationViewModel viewModel;
    public TripPreparationPage(TripPreparationViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = this.viewModel = viewModel;
    }
    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await viewModel.LoadPreparationCommand.ExecuteAsync(null);
    }
    protected override void OnDisappearing()
    {
        viewModel.CancelLoading();
        base.OnDisappearing();
    }
    private async void OnBackClicked(object? sender, EventArgs e) => await Shell.Current.GoToAsync("..");
}

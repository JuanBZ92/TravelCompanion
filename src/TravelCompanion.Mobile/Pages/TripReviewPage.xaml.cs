using TravelCompanion.Mobile.ViewModels;

namespace TravelCompanion.Mobile.Pages;

public partial class TripReviewPage : ContentPage
{
    private readonly TripReviewViewModel _viewModel;
    public TripReviewPage(TripReviewViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
    }
    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.LoadReviewCommand.ExecuteAsync(null);
    }
    protected override void OnDisappearing()
    {
        _viewModel.CancelLoading();
        base.OnDisappearing();
    }
}

using TravelCompanion.Mobile.ViewModels;

namespace TravelCompanion.Mobile.Pages;

public partial class TripReviewPage : TripScopedPage, IQueryAttributable
{
    private readonly TripReviewViewModel _viewModel;
    public TripReviewPage(TripReviewViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
    }
    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("ReviewStartDate", out var start) && start is DateOnly first
            && query.TryGetValue("ReviewEndDate", out var end) && end is DateOnly last)
            _viewModel.SetRange(first, last);
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

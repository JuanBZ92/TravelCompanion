using TravelCompanion.Mobile.ViewModels;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Mobile.Pages;

public partial class ItineraryItemEditorPage : ContentPage, IQueryAttributable
{
    private readonly ItineraryItemEditorViewModel _viewModel;
    public ItineraryItemEditorPage(ItineraryItemEditorViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
    }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("Recommendation", out var value) && value is RecommendationDto recommendation)
        {
            MainThread.BeginInvokeOnMainThread(async () => await _viewModel.InitializeAsync(recommendation));
        }
        else if (query.TryGetValue("Date", out var dateValue) && dateValue is DateOnly date
            && query.TryGetValue("PeriodKey", out var periodValue) && periodValue is string periodKey)
        {
            MainThread.BeginInvokeOnMainThread(async () => await _viewModel.InitializeManualAsync(date, periodKey));
        }
        else if (query.TryGetValue("ScheduleItem", out var itemValue) && itemValue is ScheduleItemDto item)
        {
            MainThread.BeginInvokeOnMainThread(async () => await _viewModel.InitializeExistingAsync(item));
        }
    }
}

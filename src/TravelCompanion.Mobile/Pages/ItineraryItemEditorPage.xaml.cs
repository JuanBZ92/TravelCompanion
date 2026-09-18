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
            var initialDate = query.TryGetValue("Date", out var dateValue) && dateValue is DateOnly date
                ? date
                : (DateOnly?)null;
            var suggestedStartTime = query.TryGetValue("SuggestedStartTime", out var timeValue) && timeValue is TimeOnly time
                ? time
                : (TimeOnly?)null;
            MainThread.BeginInvokeOnMainThread(
                async () => await _viewModel.InitializeAsync(recommendation, initialDate, suggestedStartTime));
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

    private async void OnPlaceTextChanged(object? sender, TextChangedEventArgs e)
    {
        if (sender is Entry { IsFocused: true })
        {
            await _viewModel.SearchPlaceSuggestionsAsync();
        }
    }

    private async void OnPlaceSuggestionTapped(object? sender, TappedEventArgs e)
    {
        var suggestion = e.Parameter as PlaceSuggestionDto
            ?? (sender as BindableObject)?.BindingContext as PlaceSuggestionDto;
        if (suggestion is null) return;
        await _viewModel.SelectPlaceAsync(suggestion);
        PlaceEntry.Unfocus();
    }

    protected override void OnDisappearing()
    {
        _viewModel.CancelPlaceSearches();
        base.OnDisappearing();
    }
}

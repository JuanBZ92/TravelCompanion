using TravelCompanion.Mobile.ViewModels;
using TravelCompanion.Shared.Dtos;

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

    private async void OnHotelTextChanged(object? sender, TextChangedEventArgs e)
    {
        await Task.Yield();
        if ((sender as BindableObject)?.BindingContext is BuilderSegmentViewModel segment)
            await _viewModel.SearchHotelSuggestionsAsync(segment);
    }

    private void OnCityFocused(object? sender, FocusEventArgs e) => UpdateCities(sender);
    private void OnCityTextChanged(object? sender, TextChangedEventArgs e) => UpdateCities(sender);
    private void UpdateCities(object? sender)
    {
        if (sender is not Entry { IsFocused: true, BindingContext: BuilderSegmentViewModel segment }) return;
        segment.CitySuggestions.Clear();
        foreach (var city in _viewModel.SuggestedCities.Where(city => System.Globalization.CultureInfo.InvariantCulture.CompareInfo.IndexOf(
                     city, segment.City.Trim(), System.Globalization.CompareOptions.IgnoreCase | System.Globalization.CompareOptions.IgnoreNonSpace) >= 0).Take(8))
            segment.CitySuggestions.Add(city);
    }

    private async void OnCityUnfocused(object? sender, FocusEventArgs e)
    {
        // Allow a suggestion tap to finish before hiding its row.
        await Task.Delay(180);
        if (sender is Entry { IsFocused: false, BindingContext: BuilderSegmentViewModel segment }) segment.CitySuggestions.Clear();
    }

    private void OnCitySuggestionTapped(object? sender, TappedEventArgs e)
    {
        if (sender is not Element { BindingContext: string city } element) return;
        var parent = element.Parent;
        while (parent is not null && parent.BindingContext is not BuilderSegmentViewModel) parent = parent.Parent;
        if (parent?.BindingContext is not BuilderSegmentViewModel segment) return;
        segment.City = city;
        segment.CitySuggestions.Clear();
        if (parent.Parent is Grid grid)
            foreach (var entry in grid.Children.OfType<Entry>()) entry.Unfocus();
    }

    private async void OnHotelSuggestionTapped(object? sender, TappedEventArgs e)
    {
        var suggestion = e.Parameter as PlaceSuggestionDto
            ?? (sender as BindableObject)?.BindingContext as PlaceSuggestionDto;
        if (suggestion is null) return;

        var segment = _viewModel.Segments.FirstOrDefault(candidate =>
            candidate.HotelSuggestions.Any(item => item.PlaceId == suggestion.PlaceId));
        if (segment is null) return;

        await _viewModel.SelectHotelAsync(segment, suggestion);
    }

    protected override void OnDisappearing()
    {
        foreach (var segment in _viewModel.Segments) segment.CitySuggestions.Clear();
        _viewModel.CancelHotelSearches();
        base.OnDisappearing();
    }
}

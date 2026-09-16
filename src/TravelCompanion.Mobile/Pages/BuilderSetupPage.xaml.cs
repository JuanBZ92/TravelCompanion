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
        if ((sender as BindableObject)?.BindingContext is BuilderSegmentViewModel segment)
            await _viewModel.SearchHotelSuggestionsAsync(segment);
    }

    private async void OnHotelSuggestionTapped(object? sender, TappedEventArgs e)
    {
        if (sender is not Element element || element.BindingContext is not PlaceSuggestionDto suggestion) return;
        var parent = element.Parent;
        while (parent is not null && parent.BindingContext is not BuilderSegmentViewModel) parent = parent.Parent;
        if (parent?.BindingContext is BuilderSegmentViewModel segment) await _viewModel.SelectHotelAsync(segment, suggestion);
    }

    protected override void OnDisappearing()
    {
        _viewModel.CancelHotelSearches();
        base.OnDisappearing();
    }
}

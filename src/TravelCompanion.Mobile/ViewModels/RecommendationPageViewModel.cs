using CommunityToolkit.Mvvm.ComponentModel;

namespace TravelCompanion.Mobile.ViewModels;

public sealed class RecommendationPageViewModel(
    int pageNumber,
    IReadOnlyList<RecommendationListItemViewModel> items,
    bool isVisible) : ObservableObject
{
    private bool _isVisible = isVisible;

    public int PageNumber { get; } = pageNumber;
    public IReadOnlyList<RecommendationListItemViewModel> Items { get; } = items;

    public bool IsVisible
    {
        get => _isVisible;
        set => SetProperty(ref _isVisible, value);
    }
}

using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using TravelCompanion.Mobile.Pages;
using TravelCompanion.Mobile.Services;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Mobile.ViewModels;

public sealed partial class MapViewModel(TravelCompanionApiClient apiClient) : ViewModelBase
{
    private RecommendationDto? _selectedRecommendation;

    public ObservableCollection<RecommendationDto> NearbyRecommendations { get; } = [];

    public RecommendationDto? SelectedRecommendation
    {
        get => _selectedRecommendation;
        set => SetProperty(ref _selectedRecommendation, value);
    }

    [RelayCommand]
    private Task LoadNearbyRecommendationsAsync()
    {
        return LoadAsync(async () =>
        {
            NearbyRecommendations.Clear();
            var recommendations = await apiClient.GetRecommendationsAsync(35.681236m, 139.767125m);
            foreach (var recommendation in recommendations)
            {
                NearbyRecommendations.Add(recommendation);
            }
        });
    }

    [RelayCommand]
    private async Task OpenRecommendationAsync(RecommendationDto? recommendation)
    {
        if (recommendation is null)
        {
            return;
        }

        SelectedRecommendation = null;
        await Shell.Current.GoToAsync(
            nameof(RecommendationDetailPage),
            new Dictionary<string, object>
            {
                ["Recommendation"] = recommendation
            });
    }
}

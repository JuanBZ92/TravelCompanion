using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using TravelCompanion.Mobile.Services;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Mobile.ViewModels;

public sealed partial class MapViewModel(TravelCompanionApiClient apiClient) : ViewModelBase
{
    public ObservableCollection<RecommendationDto> NearbyRecommendations { get; } = [];

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
}

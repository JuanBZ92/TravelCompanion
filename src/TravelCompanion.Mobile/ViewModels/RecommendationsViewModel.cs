using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TravelCompanion.Mobile.Pages;
using TravelCompanion.Mobile.Services;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Mobile.ViewModels;

public sealed partial class RecommendationsViewModel(TravelCompanionApiClient apiClient) : ViewModelBase
{
    private RecommendationDto? _selectedRecommendation;

    public ObservableCollection<RecommendationDto> Recommendations { get; } = [];

    public RecommendationDto? SelectedRecommendation
    {
        get => _selectedRecommendation;
        set => SetProperty(ref _selectedRecommendation, value);
    }

    [RelayCommand]
    private Task LoadRecommendationsAsync()
    {
        return LoadAsync(async () =>
        {
            Recommendations.Clear();
            var recommendations = await apiClient.GetRecommendationsAsync();
            foreach (var recommendation in recommendations)
            {
                Recommendations.Add(recommendation);
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

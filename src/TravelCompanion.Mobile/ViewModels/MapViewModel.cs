using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using TravelCompanion.Mobile.Pages;
using TravelCompanion.Mobile.Services;
using TravelCompanion.Shared;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Mobile.ViewModels;

public sealed partial class MapViewModel(
    AuthSessionService sessionService,
    MobileBootstrapStore bootstrapStore) : ViewModelBase
{
    private const decimal TokyoStationLatitude = 35.681236m;
    private const decimal TokyoStationLongitude = 139.767125m;
    private UserEntitlementsDto? _entitlements;
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
            await LoadNearbyRecommendationsLocalFirstAsync();
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
                ["Recommendation"] = recommendation,
                ["IsUnlocked"] = IsUnlocked(recommendation)
            });
    }

    private async Task LoadNearbyRecommendationsLocalFirstAsync()
    {
        var token = await sessionService.GetTokenAsync();
        if (string.IsNullOrWhiteSpace(token))
        {
            sessionService.Clear();
            await Shell.Current.GoToAsync("//login");
            return;
        }

        var cached = await bootstrapStore.GetCachedAsync();
        if (cached is not null)
        {
            ApplyBootstrap(cached.Value);
            StatusMessage = OfflineCacheService.FormatSavedAt(cached.SavedAt);
        }

        try
        {
            var bootstrap = await bootstrapStore.RefreshAsync(token);
            if (bootstrap is null)
            {
                sessionService.Clear();
                await Shell.Current.GoToAsync("//login");
                return;
            }

            ApplyBootstrap(bootstrap);
            StatusMessage = null;
        }
        catch
        {
            if (cached is null)
            {
                throw;
            }

            StatusMessage = $"Modo offline. {OfflineCacheService.FormatSavedAt(cached.SavedAt)}";
        }
    }

    private void ApplyBootstrap(MobileBootstrapDto bootstrap)
    {
        _entitlements = bootstrap.Entitlements;
        var recommendations = bootstrap.Recommendations
            .Select(recommendation => recommendation with
            {
                DistanceKm = CalculateDistanceKm(
                    TokyoStationLatitude,
                    TokyoStationLongitude,
                    recommendation.Latitude,
                    recommendation.Longitude)
            })
            .OrderBy(recommendation => recommendation.DistanceKm ?? decimal.MaxValue)
            .ThenBy(recommendation => recommendation.Title)
            .ToList();

        ApplyRecommendations(recommendations);
    }

    private void ApplyRecommendations(IReadOnlyList<RecommendationDto> recommendations)
    {
        NearbyRecommendations.Clear();
        foreach (var recommendation in recommendations)
        {
            NearbyRecommendations.Add(recommendation);
        }
    }

    private bool IsUnlocked(RecommendationDto recommendation)
    {
        return ContentAccessPolicy.IsUnlocked(
            recommendation.AccessLevel,
            _entitlements?.AccessLevels ?? [],
            _entitlements?.DestinationIds.Contains(recommendation.DestinationId) ?? false);
    }

    private static decimal CalculateDistanceKm(
        decimal originLatitude,
        decimal originLongitude,
        decimal targetLatitude,
        decimal targetLongitude)
    {
        const double earthRadiusKm = 6371;

        static double ToRadians(decimal degrees) => (double)degrees * Math.PI / 180;

        var latitudeDelta = ToRadians(targetLatitude - originLatitude);
        var longitudeDelta = ToRadians(targetLongitude - originLongitude);
        var originLatitudeRadians = ToRadians(originLatitude);
        var targetLatitudeRadians = ToRadians(targetLatitude);

        var a = Math.Sin(latitudeDelta / 2) * Math.Sin(latitudeDelta / 2)
            + Math.Cos(originLatitudeRadians) * Math.Cos(targetLatitudeRadians)
            * Math.Sin(longitudeDelta / 2) * Math.Sin(longitudeDelta / 2);

        var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));

        return Math.Round((decimal)(earthRadiusKm * c), 2);
    }
}

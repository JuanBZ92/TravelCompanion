using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using TravelCompanion.Mobile.Pages;
using TravelCompanion.Mobile.Services;
using TravelCompanion.Shared;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Mobile.ViewModels;

public sealed partial class RecommendationsViewModel(
    FavoritesService favoritesService,
    AuthSessionService sessionService,
    MobileBootstrapStore bootstrapStore) : ViewModelBase
{
    private const string AllCategories = "Todas";
    private const string FavoritesCategory = "Favoritos";
    private readonly List<RecommendationListItemViewModel> _allRecommendations = [];
    private RecommendationListItemViewModel? _selectedRecommendation;
    private string _selectedCategory = AllCategories;

    public ObservableCollection<RecommendationListItemViewModel> Recommendations { get; } = [];
    public ObservableCollection<string> Categories { get; } = [AllCategories, FavoritesCategory];

    public RecommendationListItemViewModel? SelectedRecommendation
    {
        get => _selectedRecommendation;
        set => SetProperty(ref _selectedRecommendation, value);
    }

    public string SelectedCategory
    {
        get => _selectedCategory;
        set
        {
            if (SetProperty(ref _selectedCategory, value))
            {
                ApplyFilters();
            }
        }
    }

    [RelayCommand]
    private Task LoadRecommendationsAsync()
    {
        return LoadAsync(async () =>
        {
            var token = await sessionService.GetTokenAsync();
            if (string.IsNullOrWhiteSpace(token))
            {
                sessionService.Clear();
                await Shell.Current.GoToAsync("//login");
                return;
            }

            await LoadBootstrapLocalFirstAsync(token);
        });
    }

    public void RefreshFavoriteState()
    {
        foreach (var recommendation in _allRecommendations)
        {
            recommendation.IsFavorite = favoritesService.IsFavorite(recommendation.Id);
        }

        ApplyFilters();
    }

    [RelayCommand]
    private async Task OpenRecommendationAsync(RecommendationListItemViewModel? recommendation)
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
                ["Recommendation"] = recommendation.Recommendation,
                ["IsUnlocked"] = recommendation.IsUnlocked
            });
    }

    [RelayCommand]
    private void ToggleFavorite(RecommendationListItemViewModel? recommendation)
    {
        if (recommendation is null)
        {
            return;
        }

        recommendation.IsFavorite = favoritesService.ToggleFavorite(recommendation.Id);
        ApplyFilters();
    }

    private void UpdateCategories()
    {
        var currentCategory = SelectedCategory;
        Categories.Clear();
        Categories.Add(AllCategories);
        Categories.Add(FavoritesCategory);

        foreach (var category in _allRecommendations.Select(recommendation => recommendation.Category).Distinct().Order())
        {
            Categories.Add(category);
        }

        SelectedCategory = Categories.Contains(currentCategory) ? currentCategory : AllCategories;
    }

    private void ApplyFilters()
    {
        Recommendations.Clear();

        var filtered = SelectedCategory switch
        {
            FavoritesCategory => _allRecommendations.Where(recommendation => recommendation.IsFavorite),
            AllCategories => _allRecommendations,
            _ => _allRecommendations.Where(recommendation => recommendation.Category == SelectedCategory)
        };

        foreach (var recommendation in filtered)
        {
            Recommendations.Add(recommendation);
        }
    }

    private static bool IsUnlocked(RecommendationDto recommendation, UserEntitlementsDto? entitlements)
    {
        return ContentAccessPolicy.IsUnlocked(
            recommendation.AccessLevel,
            entitlements?.AccessLevels ?? [],
            entitlements?.DestinationIds.Contains(recommendation.DestinationId) ?? false);
    }

    private async Task LoadBootstrapLocalFirstAsync(string token)
    {
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
        _allRecommendations.Clear();
        foreach (var recommendation in bootstrap.Recommendations)
        {
            _allRecommendations.Add(new RecommendationListItemViewModel(recommendation)
            {
                IsFavorite = favoritesService.IsFavorite(recommendation.Id),
                IsUnlocked = IsUnlocked(recommendation, bootstrap.Entitlements)
            });
        }

        UpdateCategories();
        ApplyFilters();
    }
}

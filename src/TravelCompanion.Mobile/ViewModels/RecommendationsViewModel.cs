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
    private int _selectedPageSize = 10;
    private int _currentPage = 1;
    private int _totalPages = 1;
    private int _totalItems;

    public ObservableCollection<RecommendationListItemViewModel> Recommendations { get; } = [];
    public ObservableCollection<string> Categories { get; } = [AllCategories, FavoritesCategory];
    public ObservableCollection<int> PageSizeOptions { get; } = [10, 20, 50];

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
                ApplyFilters(resetPage: true);
            }
        }
    }

    public int SelectedPageSize
    {
        get => _selectedPageSize;
        set
        {
            if (value <= 0)
            {
                return;
            }

            if (SetProperty(ref _selectedPageSize, value))
            {
                ApplyFilters(resetPage: true);
            }
        }
    }

    public int CurrentPage
    {
        get => _currentPage;
        private set
        {
            if (SetProperty(ref _currentPage, value))
            {
                OnPaginationChanged();
            }
        }
    }

    public int TotalPages
    {
        get => _totalPages;
        private set
        {
            if (SetProperty(ref _totalPages, value))
            {
                OnPaginationChanged();
            }
        }
    }

    public int TotalItems
    {
        get => _totalItems;
        private set
        {
            if (SetProperty(ref _totalItems, value))
            {
                OnPropertyChanged(nameof(PageSummary));
            }
        }
    }

    public bool CanGoPrevious => CurrentPage > 1;
    public bool CanGoNext => CurrentPage < TotalPages;
    public string PageSummary => TotalItems == 0
        ? "0 resultados"
        : $"Pagina {CurrentPage} de {TotalPages} · {TotalItems} resultados";

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

        ApplyFilters(resetPage: false);
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
        ApplyFilters(resetPage: false);
    }

    [RelayCommand]
    private void PreviousPage()
    {
        if (!CanGoPrevious)
        {
            return;
        }

        CurrentPage--;
        ApplyFilters(resetPage: false);
    }

    [RelayCommand]
    private void NextPage()
    {
        if (!CanGoNext)
        {
            return;
        }

        CurrentPage++;
        ApplyFilters(resetPage: false);
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

    private void ApplyFilters(bool resetPage)
    {
        Recommendations.Clear();

        var filtered = (SelectedCategory switch
        {
            FavoritesCategory => _allRecommendations.Where(recommendation => recommendation.IsFavorite),
            AllCategories => _allRecommendations,
            _ => _allRecommendations.Where(recommendation => recommendation.Category == SelectedCategory)
        }).ToList();

        TotalItems = filtered.Count;
        TotalPages = Math.Max(1, (int)Math.Ceiling(TotalItems / (double)SelectedPageSize));

        if (resetPage)
        {
            CurrentPage = 1;
        }
        else if (CurrentPage > TotalPages)
        {
            CurrentPage = TotalPages;
        }

        foreach (var recommendation in filtered
            .Skip((CurrentPage - 1) * SelectedPageSize)
            .Take(SelectedPageSize))
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
        var resetPage = _allRecommendations.Count == 0;
        var cached = await bootstrapStore.GetCachedAsync();
        if (cached is not null)
        {
            ApplyBootstrap(cached.Value, resetPage);
            StatusMessage = OfflineCacheService.FormatSavedAt(cached.SavedAt);
            resetPage = false;
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

            ApplyBootstrap(bootstrap, resetPage);
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

    private void ApplyBootstrap(MobileBootstrapDto bootstrap, bool resetPage)
    {
        _allRecommendations.Clear();
        foreach (var recommendation in bootstrap.Recommendations)
        {
            var isUnlocked = IsUnlocked(recommendation, bootstrap.Entitlements);
            if (!isUnlocked)
            {
                continue;
            }

            _allRecommendations.Add(new RecommendationListItemViewModel(recommendation)
            {
                IsFavorite = favoritesService.IsFavorite(recommendation.Id),
                IsUnlocked = true
            });
        }

        UpdateCategories();
        ApplyFilters(resetPage);
    }

    private void OnPaginationChanged()
    {
        OnPropertyChanged(nameof(CanGoPrevious));
        OnPropertyChanged(nameof(CanGoNext));
        OnPropertyChanged(nameof(PageSummary));
    }
}

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
    MobileBootstrapStore bootstrapStore) : ViewModelBase, ISessionStateResettable
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
    private bool _isPaging;

    public ObservableCollection<RecommendationListItemViewModel> Recommendations { get; } = [];
    public ObservableCollection<RecommendationPageViewModel> RecommendationPages { get; } = [];
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
                OnPropertyChanged(nameof(HasRecommendations));
                OnPropertyChanged(nameof(ShowInitialLoading));
                OnPropertyChanged(nameof(ShowEmptyState));
            }
        }
    }

    public bool CanGoPrevious => CurrentPage > 1;
    public bool CanGoNext => CurrentPage < TotalPages;
    public bool HasRecommendations => TotalItems > 0;
    public bool ShowInitialLoading => IsBusy && !HasRecommendations;
    public bool ShowEmptyState => HasLoaded && !IsBusy && !HasRecommendations;
    public bool IsPaging
    {
        get => _isPaging;
        private set => SetProperty(ref _isPaging, value);
    }

    public string PageSummary => TotalItems == 0
        ? "0 resultados"
        : $"Pagina {CurrentPage} de {TotalPages} · {TotalItems} resultados";

    [RelayCommand]
    private Task LoadRecommendationsAsync()
    {
        return LoadAsync(async ct =>
        {
            var token = await sessionService.GetTokenAsync();
            if (string.IsNullOrWhiteSpace(token))
            {
                sessionService.Clear();
                await Shell.Current.GoToAsync("//login");
                return;
            }

            await LoadBootstrapLocalFirstAsync(token, ct);
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

    public void ResetForNewSession()
    {
        ResetLoadState();
        _allRecommendations.Clear();
        Recommendations.Clear();
        RecommendationPages.Clear();
        Categories.Clear();
        Categories.Add(AllCategories);
        Categories.Add(FavoritesCategory);
        SelectedCategory = AllCategories;
        CurrentPage = 1;
        TotalPages = 1;
        TotalItems = 0;
        IsPaging = false;
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
    private async Task PreviousPageAsync()
    {
        if (!CanGoPrevious)
        {
            return;
        }

        await ChangePageAsync(CurrentPage - 1);
    }

    [RelayCommand]
    private async Task NextPageAsync()
    {
        if (!CanGoNext)
        {
            return;
        }

        await ChangePageAsync(CurrentPage + 1);
    }

    private void UpdateCategories()
    {
        var currentCategory = SelectedCategory;

        // Build category list first to minimize CollectionChanged events
        var newCategories = new List<string> { AllCategories, FavoritesCategory };
        newCategories.AddRange(_allRecommendations
            .Select(recommendation => recommendation.Category)
            .Distinct()
            .Order());

        // Clear and rebuild in one pass
        Categories.Clear();
        foreach (var category in newCategories)
        {
            Categories.Add(category);
        }

        SelectedCategory = Categories.Contains(currentCategory) ? currentCategory : AllCategories;
    }

    private void ApplyFilters(bool resetPage)
    {
        // Get filtered items without intermediate ToList() allocation
        var filtered = SelectedCategory switch
        {
            FavoritesCategory => (IEnumerable<RecommendationListItemViewModel>)_allRecommendations.Where(r => r.IsFavorite),
            AllCategories => _allRecommendations,
            _ => _allRecommendations.Where(r => r.Category == SelectedCategory)
        };

        var filteredItems = filtered.ToList();
        TotalItems = filteredItems.Count;
        TotalPages = Math.Max(1, (int)Math.Ceiling(TotalItems / (double)SelectedPageSize));

        if (resetPage)
        {
            CurrentPage = 1;
        }
        else if (CurrentPage > TotalPages)
        {
            CurrentPage = TotalPages;
        }

        RecommendationPages.Clear();
        Recommendations.Clear();
        for (var pageNumber = 1; pageNumber <= TotalPages; pageNumber++)
        {
            var pageItems = filteredItems
                .Skip((pageNumber - 1) * SelectedPageSize)
                .Take(SelectedPageSize)
                .ToList();

            RecommendationPages.Add(new RecommendationPageViewModel(
                pageNumber,
                pageItems,
                pageNumber == CurrentPage));
        }

        foreach (var recommendation in GetCurrentPageItems())
        {
            Recommendations.Add(recommendation);
        }
    }

    private async Task ChangePageAsync(int pageNumber)
    {
        if (pageNumber < 1 || pageNumber > TotalPages || pageNumber == CurrentPage)
        {
            return;
        }

        IsPaging = true;
        CurrentPage = pageNumber;
        await Task.Yield();
        SetVisiblePage(pageNumber);
        IsPaging = false;
    }

    private void SetVisiblePage(int pageNumber)
    {
        foreach (var page in RecommendationPages)
        {
            page.IsVisible = page.PageNumber == pageNumber;
        }

        Recommendations.Clear();
        foreach (var recommendation in GetCurrentPageItems())
        {
            Recommendations.Add(recommendation);
        }
    }

    private IReadOnlyList<RecommendationListItemViewModel> GetCurrentPageItems()
    {
        return RecommendationPages.FirstOrDefault(page => page.PageNumber == CurrentPage)?.Items ?? [];
    }

    private static bool IsUnlocked(RecommendationDto recommendation, UserEntitlementsDto? entitlements)
    {
        return ContentAccessPolicy.IsUnlocked(
            recommendation.AccessLevel,
            entitlements?.AccessLevels ?? [],
            entitlements?.DestinationIds.Contains(recommendation.DestinationId) ?? false);
    }

    private async Task LoadBootstrapLocalFirstAsync(string token, CancellationToken cancellationToken = default)
    {
        var resetPage = _allRecommendations.Count == 0;
        var cached = await bootstrapStore.GetCachedAsync(cancellationToken: cancellationToken);
        if (cached is not null)
        {
            ApplyBootstrap(cached.Value, resetPage);
            StatusMessage = OfflineCacheService.FormatSavedAt(cached.SavedAt);
            resetPage = false;
        }

        try
        {
            var bootstrap = await bootstrapStore.RefreshAsync(token, cancellationToken: cancellationToken);
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

    protected override void OnLoadStateChanged()
    {
        OnPropertyChanged(nameof(ShowInitialLoading));
        OnPropertyChanged(nameof(ShowEmptyState));
    }
}

using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using TravelCompanion.Mobile.Pages;
using TravelCompanion.Mobile.Services;
using TravelCompanion.Shared;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Mobile.ViewModels;

public sealed partial class MapViewModel(
    AuthSessionService sessionService,
    MobileBootstrapStore bootstrapStore) : ViewModelBase, ISessionStateResettable
{
    private const decimal TokyoStationLatitude = 35.681236m;
    private const decimal TokyoStationLongitude = 139.767125m;
    private readonly List<RecommendationDto> _allNearbyRecommendations = [];
    private UserEntitlementsDto? _entitlements;
    private RecommendationDto? _selectedRecommendation;
    private int _selectedPageSize = 10;
    private int _currentPage = 1;
    private int _totalPages = 1;
    private int _totalItems;

    public ObservableCollection<RecommendationDto> NearbyRecommendations { get; } = [];
    public ObservableCollection<int> PageSizeOptions { get; } = [10, 20, 50];

    public RecommendationDto? SelectedRecommendation
    {
        get => _selectedRecommendation;
        set => SetProperty(ref _selectedRecommendation, value);
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
                CurrentPage = 1;
                ApplyCurrentPage();
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
                OnPropertyChanged(nameof(HasNearbyRecommendations));
                OnPropertyChanged(nameof(ShowInitialLoading));
                OnPropertyChanged(nameof(ShowEmptyState));
            }
        }
    }

    public bool CanGoPrevious => CurrentPage > 1;
    public bool CanGoNext => CurrentPage < TotalPages;
    public bool HasNearbyRecommendations => TotalItems > 0;
    public bool ShowInitialLoading => IsBusy && !HasNearbyRecommendations;
    public bool ShowEmptyState => HasLoaded && !IsBusy && !HasNearbyRecommendations;
    public string PageSummary => TotalItems == 0
        ? "0 lugares"
        : $"Pagina {CurrentPage} de {TotalPages} · {TotalItems} lugares";

    public void ResetForNewSession()
    {
        ResetLoadState();
        _allNearbyRecommendations.Clear();
        NearbyRecommendations.Clear();
        _entitlements = null;
        SelectedRecommendation = null;
        CurrentPage = 1;
        TotalPages = 1;
        TotalItems = 0;
    }

    [RelayCommand]
    private Task LoadNearbyRecommendationsAsync()
    {
        return LoadAsync(async ct =>
        {
            await LoadNearbyRecommendationsLocalFirstAsync(ct);
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

    [RelayCommand]
    private void PreviousPage()
    {
        if (!CanGoPrevious)
        {
            return;
        }

        CurrentPage--;
        ApplyCurrentPage();
    }

    [RelayCommand]
    private void NextPage()
    {
        if (!CanGoNext)
        {
            return;
        }

        CurrentPage++;
        ApplyCurrentPage();
    }

    private async Task LoadNearbyRecommendationsLocalFirstAsync(CancellationToken cancellationToken = default)
    {
        var token = await sessionService.GetTokenAsync();
        if (string.IsNullOrWhiteSpace(token))
        {
            sessionService.Clear();
            await Shell.Current.GoToAsync("//login");
            return;
        }

        var resetPage = _allNearbyRecommendations.Count == 0;
        var cached = await bootstrapStore.GetCachedAsync(cancellationToken: cancellationToken);
        if (cached is not null)
        {
            ApplyBootstrap(cached.Value, resetPage);
            resetPage = false;

            if (bootstrapStore.HasFreshSnapshot())
            {
                StatusMessage = null;
                return;
            }

            StatusMessage = OfflineCacheService.FormatSavedAt(cached.SavedAt);
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
        _entitlements = bootstrap.Entitlements;
        var recommendations = bootstrap.Recommendations
            .Where(IsUnlocked)
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

        ApplyRecommendations(recommendations, resetPage);
    }

    private void ApplyRecommendations(IReadOnlyList<RecommendationDto> recommendations, bool resetPage)
    {
        _allNearbyRecommendations.Clear();
        _allNearbyRecommendations.AddRange(recommendations);
        if (resetPage)
        {
            CurrentPage = 1;
        }

        TotalItems = _allNearbyRecommendations.Count;
        TotalPages = Math.Max(1, (int)Math.Ceiling(TotalItems / (double)SelectedPageSize));
        ApplyCurrentPage();
    }

    private void ApplyCurrentPage()
    {
        TotalItems = _allNearbyRecommendations.Count;
        TotalPages = Math.Max(1, (int)Math.Ceiling(TotalItems / (double)SelectedPageSize));
        if (CurrentPage > TotalPages)
        {
            CurrentPage = TotalPages;
        }

        NearbyRecommendations.Clear();
        foreach (var recommendation in _allNearbyRecommendations
            .Skip((CurrentPage - 1) * SelectedPageSize)
            .Take(SelectedPageSize))
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

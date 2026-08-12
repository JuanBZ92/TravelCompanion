using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using TravelCompanion.Mobile.Pages;
using TravelCompanion.Mobile.Services;
using TravelCompanion.Shared;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Mobile.ViewModels;

public sealed partial class MapViewModel(
    AuthSessionService sessionService,
    MobileBootstrapStore bootstrapStore,
    PendingItineraryActionStore pendingStore,
    TravelCompanionApiClient apiClient) : ViewModelBase, ISessionStateResettable
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
    private IReadOnlyList<RecommendationDto> _visibleNearbyRecommendations = [];
    private string _searchText = string.Empty;

    public ObservableCollection<int> PageSizeOptions { get; } = [10, 20, 50];
    public string SearchText { get => _searchText; set => SetProperty(ref _searchText, value); }

    public IReadOnlyList<RecommendationDto> VisibleNearbyRecommendations
    {
        get => _visibleNearbyRecommendations;
        private set => SetProperty(ref _visibleNearbyRecommendations, value);
    }

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
    public bool CanAddToItinerary => sessionService.CanEditItinerary;
    public string PageSummary => TotalItems == 0
        ? "0 lugares"
        : $"Pagina {CurrentPage} de {TotalPages} · {TotalItems} lugares";

    public void ResetForNewSession()
    {
        ResetLoadState();
        _allNearbyRecommendations.Clear();
        VisibleNearbyRecommendations = [];
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
    private async Task AddToItineraryAsync(RecommendationDto? recommendation)
    {
        if (recommendation is null || !sessionService.CanEditItinerary)
        {
            return;
        }

        if (recommendation.Id == Guid.Empty)
        {
            StatusMessage = recommendation.Attribution is null
                ? "Detalle externo no disponible."
                : $"Información provista por {recommendation.Attribution}. Puedes agregarla a tu itinerario.";
            return;
        }

        if (sessionService.RequiresTripSetup)
        {
            pendingStore.Set(recommendation);
            await Shell.Current.GoToAsync(nameof(BuilderSetupPage));
            return;
        }

        await Shell.Current.GoToAsync(
            nameof(ItineraryItemEditorPage),
            new Dictionary<string, object> { ["Recommendation"] = recommendation });
    }

    [RelayCommand]
    private Task SearchAsync() => LoadAsync(async ct =>
    {
        if (string.IsNullOrWhiteSpace(SearchText))
        {
            await LoadNearbyRecommendationsLocalFirstAsync(ct);
            return;
        }
        var token = await sessionService.GetTokenAsync();
        if (string.IsNullOrWhiteSpace(token)) return;
        var results = await apiClient.SearchPlacesAsync(token, new PlaceSearchRequest(SearchText.Trim()), ct);
        ApplyRecommendations(results.Select(item => item with
        {
            DistanceKm = item.DistanceKm ?? CalculateDistanceKm(TokyoStationLatitude, TokyoStationLongitude, item.Latitude, item.Longitude)
        }).OrderBy(item => item.DistanceKm).ToList(), true);
    });


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
            MarkLastUpdated(cached.SavedAt);
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
            MarkLastUpdated(DateTimeOffset.UtcNow);
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
        var recommendations = (bootstrap.Recommendations ?? [])
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

        VisibleNearbyRecommendations = _allNearbyRecommendations
            .Skip((CurrentPage - 1) * SelectedPageSize)
            .Take(SelectedPageSize)
            .ToList();
    }

    private bool IsUnlocked(RecommendationDto recommendation)
    {
        return ContentAccessPolicy.IsRecommendationUnlocked(
            _entitlements,
            recommendation.AccessLevel,
            recommendation.DestinationId,
            recommendation.PackageIds);
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

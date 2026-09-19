using System.Diagnostics;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using TravelCompanion.Mobile.Pages;
using TravelCompanion.Mobile.Services;
using TravelCompanion.Shared;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Mobile.ViewModels;

public sealed partial class MapViewModel(
    AuthSessionService sessionService,
    MobileBootstrapStore bootstrapStore,
    PendingItineraryActionStore pendingStore,
    TravelCompanionApiClient apiClient,
    ILogger<MapViewModel> logger) : ViewModelBase, ISessionStateResettable
{
    private const int PageSize = 10;
    private const decimal TokyoStationLatitude = 35.681236m;
    private const decimal TokyoStationLongitude = 139.767125m;
    private readonly List<RecommendationDto> _allNearbyRecommendations = [];
    private readonly List<RecommendationDto> _mapRecommendations = [];
    private readonly List<CatalogSearchEntry> _catalogSearchIndex = [];
    private string? _temporaryMapRecommendationKey;
    private UserEntitlementsDto? _entitlements;
    private RecommendationDto? _selectedRecommendation;
    private int _currentPage = 1;
    private int _totalPages = 1;
    private int _totalItems;
    private bool _isSelectedRecommendationLoading;
    private IReadOnlyList<RecommendationDto> _visibleNearbyRecommendations = [];
    private string _searchText = string.Empty;
    private string? _activeSearchQuery;
    private IReadOnlyList<RecommendationDto> _searchResults = [];
    private readonly Dictionary<string, IReadOnlyList<RecommendationDto>> _externalSearchCache = new(StringComparer.Ordinal);
    private CancellationTokenSource? _searchCancellation;

    public string SearchText { get => _searchText; set => SetProperty(ref _searchText, value); }
    public bool CanSearchGoogle => sessionService.CanSearchGooglePlaces;

    public IReadOnlyList<RecommendationDto> VisibleNearbyRecommendations
    {
        get => _visibleNearbyRecommendations;
        private set => SetProperty(ref _visibleNearbyRecommendations, value);
    }

    public RecommendationDto? SelectedRecommendation
    {
        get => _selectedRecommendation;
        private set
        {
            if (SetProperty(ref _selectedRecommendation, value))
            {
                OnPropertyChanged(nameof(HasSelectedRecommendation));
                OnPropertyChanged(nameof(ShowRecommendationBrowser));
                OnPropertyChanged(nameof(ShowSelectedRecommendationDescription));
                OnPropertyChanged(nameof(SelectedRecommendationPosition));
                OnPropertyChanged(nameof(SelectedRecommendationMeta));
                OnPropertyChanged(nameof(SelectedRecommendationType));
                OnPropertyChanged(nameof(CanBrowseSelectedRecommendations));
            }
        }
    }

    public IReadOnlyList<RecommendationDto> MapRecommendations => _mapRecommendations;

    public bool HasSelectedRecommendation => SelectedRecommendation is not null;
    public bool ShowRecommendationBrowser => !HasSelectedRecommendation;
    public bool IsSelectedRecommendationLoading
    {
        get => _isSelectedRecommendationLoading;
        private set
        {
            if (SetProperty(ref _isSelectedRecommendationLoading, value))
            {
                OnPropertyChanged(nameof(ShowSelectedRecommendationDescription));
            }
        }
    }

    public bool ShowSelectedRecommendationDescription =>
        HasSelectedRecommendation && !IsSelectedRecommendationLoading;
    public bool CanBrowseSelectedRecommendations => HasSelectedRecommendation && VisibleNearbyRecommendations.Count > 1;
    public string SelectedRecommendationType =>
        SelectedRecommendation?.RefinedType ?? SelectedRecommendation?.Category ?? string.Empty;
    public string SelectedRecommendationMeta
    {
        get
        {
            if (SelectedRecommendation is null)
            {
                return string.Empty;
            }

            var values = new List<string> { SelectedRecommendation.RefinedType ?? SelectedRecommendation.Category };
            if (!string.IsNullOrWhiteSpace(SelectedRecommendation.Neighborhood))
            {
                values.Add(SelectedRecommendation.Neighborhood);
            }

            if (SelectedRecommendation.DistanceKm.HasValue)
            {
                values.Add($"{SelectedRecommendation.DistanceKm.Value:F1} km");
            }

            return string.Join(" · ", values);
        }
    }

    public string SelectedRecommendationPosition
    {
        get
        {
            if (SelectedRecommendation is null)
            {
                return string.Empty;
            }

            var index = FindSelectedRecommendationIndex();
            return index < 0 ? string.Empty : $"{index + 1} / {VisibleNearbyRecommendations.Count}";
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
        _mapRecommendations.Clear();
        _catalogSearchIndex.Clear();
        _temporaryMapRecommendationKey = null;
        VisibleNearbyRecommendations = [];
        _entitlements = null;
        SelectedRecommendation = null;
        IsSelectedRecommendationLoading = false;
        CurrentPage = 1;
        TotalPages = 1;
        TotalItems = 0;
        SearchText = string.Empty;
        _activeSearchQuery = null;
        _searchResults = [];
        CancelSearch();
        _externalSearchCache.Clear();
        OnPropertyChanged(nameof(MapRecommendations));
        OnPropertyChanged(nameof(CanAddToItinerary));
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
    private async Task SelectRecommendationAsync(RecommendationDto? recommendation)
    {
        if (recommendation is null)
        {
            return;
        }

        var browseRecommendations = _activeSearchQuery is null
            ? _allNearbyRecommendations
            : _searchResults;
        var mapIndex = browseRecommendations
            .Select((item, index) => new { item.SelectionKey, Index = index })
            .FirstOrDefault(item => item.SelectionKey == recommendation.SelectionKey)?.Index ?? -1;
        if (mapIndex < 0) return;

        var targetPage = mapIndex / PageSize + 1;
        if (targetPage != CurrentPage)
        {
            if (_activeSearchQuery is null)
            {
                CurrentPage = targetPage;
                ApplyCurrentPage();
            }
            else
            {
                ApplySearchPage(targetPage);
            }
        }

        SetTemporaryMapRecommendation(recommendation);
        IsSelectedRecommendationLoading = recommendation.Id != Guid.Empty;
        SelectedRecommendation = recommendation;
        await LoadSelectedRecommendationDetailAsync(recommendation);
    }

    [RelayCommand]
    private void ClearRecommendationSelection()
    {
        ResetSelection();
    }

    public void ResetSelection()
    {
        SelectedRecommendation = null;
        IsSelectedRecommendationLoading = false;
        SetTemporaryMapRecommendation(null);
    }

    [RelayCommand]
    private Task SelectPreviousRecommendationAsync() => SelectAdjacentRecommendationAsync(-1);

    [RelayCommand]
    private Task SelectNextRecommendationAsync() => SelectAdjacentRecommendationAsync(1);

    [RelayCommand]
    private async Task AddToItineraryAsync(RecommendationDto? recommendation)
    {
        if (recommendation is null || !sessionService.CanEditItinerary)
        {
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
    private async Task OpenInGoogleMapsAsync(RecommendationDto? recommendation)
    {
        if (recommendation is null)
        {
            return;
        }

        var opened = await GoogleMapsLauncher.OpenAsync(recommendation);
        if (!opened)
        {
            StatusMessage = "No se pudo abrir Google Maps.";
        }
    }

    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task SearchAsync()
    {
        CancelSearch();
        if (string.IsNullOrWhiteSpace(SearchText))
        {
            _activeSearchQuery = null;
            _searchResults = [];
            ApplyCurrentPage();
            return;
        }

        var searchQuery = SearchText.Trim();
        _activeSearchQuery = searchQuery;
        _searchResults = SearchCatalog(searchQuery);
        StatusMessage = null;
        ApplySearchPage(1);

        if (_searchResults.Count > 0 || !CanSearchGoogle || searchQuery.Length < 3)
        {
            return;
        }

        var searchKey = $"google:{sessionService.ContextVersion}:{sessionService.CurrentUserId}:{sessionService.CurrentTripId}:{System.Globalization.CultureInfo.CurrentUICulture.Name}:{searchQuery}";
        if (_externalSearchCache.TryGetValue(searchKey, out var cachedResults))
        {
            _searchResults = cachedResults;
            ApplySearchPage(1);
            return;
        }

        var operation = new CancellationTokenSource();
        _searchCancellation = operation;
        var contextVersion = sessionService.ContextVersion;
        try
        {
            var token = await sessionService.GetTokenAsync();
            if (string.IsNullOrWhiteSpace(token)) return;
            var result = await apiClient.SearchPlacesResultAsync(
                token,
                new PlaceSearchRequest(searchQuery, IncludeGoogle: true),
                operation.Token);
            if (result.IsUnauthorized)
            {
                sessionService.Clear();
                await Shell.Current.GoToAsync("//login");
                return;
            }
            if (result.Value is not { } results)
            {
                StatusMessage = "Google no está disponible. Conservamos los resultados del catálogo.";
                return;
            }
            if (operation.IsCancellationRequested
                || !ReferenceEquals(_searchCancellation, operation)
                || sessionService.ContextVersion != contextVersion
                || !string.Equals(SearchText.Trim(), searchQuery, StringComparison.Ordinal)) return;
            _searchResults = results;
            if (_externalSearchCache.Count >= 20)
            {
                _externalSearchCache.Remove(_externalSearchCache.Keys.First());
            }
            _externalSearchCache[searchKey] = results;
            StatusMessage = results.Count == 0 ? "No encontramos lugares con esa búsqueda." : null;
            ApplySearchPage(1);
        }
        catch (OperationCanceledException)
        {
            // Expected while the user keeps typing or leaves the page.
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Map place search failed for query {Query}.", searchQuery);
            StatusMessage = "No pudimos completar la búsqueda. Inténtalo nuevamente.";
        }
        finally
        {
            if (ReferenceEquals(_searchCancellation, operation))
            {
                _searchCancellation = null;
            }
            operation.Dispose();
        }
    }

    public void CancelSearch()
    {
        var operation = Interlocked.Exchange(ref _searchCancellation, null);
        operation?.Cancel();
    }

    private IReadOnlyList<RecommendationDto> SearchCatalog(string query)
    {
        var score = CatalogSearch.CreateNormalizedFieldScorer(query);
        return _catalogSearchIndex
            .Select(entry => new
            {
                entry.Item,
                Score = score(
                    entry.NormalizedTitle,
                    entry.NormalizedMetadata,
                    entry.NormalizedDescription)
            })
            .Where(candidate => candidate.Score > 0)
            .OrderByDescending(candidate => candidate.Score)
            .ThenBy(candidate => candidate.Item.Title)
            .Select(candidate => candidate.Item)
            .ToList();
    }

    private void ApplySearchPage(int page)
    {
        SetTemporaryMapRecommendation(null);
        SelectedRecommendation = null;
        TotalItems = _searchResults.Count;
        TotalPages = Math.Max(1, (int)Math.Ceiling(TotalItems / (double)PageSize));
        CurrentPage = Math.Clamp(page, 1, TotalPages);
        VisibleNearbyRecommendations = _searchResults
            .Skip((CurrentPage - 1) * PageSize)
            .Take(PageSize)
            .ToList();
        OnPropertyChanged(nameof(CanBrowseSelectedRecommendations));
        OnPropertyChanged(nameof(SelectedRecommendationPosition));
    }


    [RelayCommand]
    private void PreviousPage()
    {
        if (!CanGoPrevious)
        {
            return;
        }

        SelectedRecommendation = null;
        if (_activeSearchQuery is not null) { ApplySearchPage(CurrentPage - 1); return; }
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

        SelectedRecommendation = null;
        if (_activeSearchQuery is not null) { ApplySearchPage(CurrentPage + 1); return; }
        CurrentPage++;
        ApplyCurrentPage();
    }

    private async Task LoadNearbyRecommendationsLocalFirstAsync(CancellationToken cancellationToken = default)
    {
        var usableContentStopwatch = Stopwatch.StartNew();
        var usableContentLogged = false;
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
            logger.LogInformation(
                "Map usable content available in {ElapsedMs}ms. Source=cache; Items={ItemCount}.",
                usableContentStopwatch.Elapsed.TotalMilliseconds,
                VisibleNearbyRecommendations.Count);
            usableContentLogged = true;
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
            var result = await bootstrapStore.RefreshResultAsync(token, cancellationToken: cancellationToken);
            if (result.IsUnauthorized)
            {
                sessionService.Clear();
                await Shell.Current.GoToAsync("//login");
                return;
            }

            if (result.Value is { } bootstrap)
            {
                ApplyBootstrap(bootstrap, resetPage);
                if (!usableContentLogged)
                {
                    logger.LogInformation(
                        "Map usable content available in {ElapsedMs}ms. Source=network; Items={ItemCount}.",
                        usableContentStopwatch.Elapsed.TotalMilliseconds,
                        VisibleNearbyRecommendations.Count);
                }
                MarkLastUpdated(DateTimeOffset.UtcNow);
                StatusMessage = null;
            }
            else if (cached is null)
            {
                throw new HttpRequestException("No pudimos cargar el mapa. Comprueba la conexión e inténtalo de nuevo.");
            }
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
        var selectedKey = SelectedRecommendation?.SelectionKey;
        _allNearbyRecommendations.Clear();
        _allNearbyRecommendations.AddRange(recommendations);
        _mapRecommendations.Clear();
        _mapRecommendations.AddRange(recommendations);
        _temporaryMapRecommendationKey = null;
        if (SelectedRecommendation is { Id: var selectedId } selected && selectedId == Guid.Empty)
        {
            _mapRecommendations.Add(selected);
            _temporaryMapRecommendationKey = selected.SelectionKey;
        }
        _catalogSearchIndex.Clear();
        _catalogSearchIndex.AddRange(recommendations.Select(recommendation => new CatalogSearchEntry(
            recommendation,
            CatalogSearch.Normalize(recommendation.Title),
            CatalogSearch.Normalize($"{recommendation.Neighborhood} {recommendation.Category} {recommendation.RefinedType} {string.Join(' ', recommendation.Tags)}"),
            CatalogSearch.Normalize(recommendation.Description))));
        OnPropertyChanged(nameof(MapRecommendations));
        if (resetPage)
        {
            CurrentPage = 1;
        }

        if (_activeSearchQuery is null)
        {
            TotalItems = _allNearbyRecommendations.Count;
            TotalPages = Math.Max(1, (int)Math.Ceiling(TotalItems / (double)PageSize));
            ApplyCurrentPage();
        }
        else
        {
            ApplySearchPage(resetPage ? 1 : CurrentPage);
        }
        SelectedRecommendation = selectedKey is null
            ? null
            : VisibleNearbyRecommendations.FirstOrDefault(item => item.SelectionKey == selectedKey);
    }

    private void ApplyCurrentPage()
    {
        TotalItems = _allNearbyRecommendations.Count;
        TotalPages = Math.Max(1, (int)Math.Ceiling(TotalItems / (double)PageSize));
        if (CurrentPage > TotalPages)
        {
            CurrentPage = TotalPages;
        }

        VisibleNearbyRecommendations = _allNearbyRecommendations
            .Skip((CurrentPage - 1) * PageSize)
            .Take(PageSize)
            .ToList();
        OnPropertyChanged(nameof(CanBrowseSelectedRecommendations));
        OnPropertyChanged(nameof(SelectedRecommendationPosition));
    }

    private void SetTemporaryMapRecommendation(RecommendationDto? recommendation)
    {
        var changed = false;
        if (_temporaryMapRecommendationKey is not null)
        {
            changed = _mapRecommendations.RemoveAll(item =>
                string.Equals(item.SelectionKey, _temporaryMapRecommendationKey, StringComparison.Ordinal)) > 0;
            _temporaryMapRecommendationKey = null;
        }

        if (recommendation is { Id: var id } && id == Guid.Empty
            && _mapRecommendations.All(item => item.SelectionKey != recommendation.SelectionKey))
        {
            _mapRecommendations.Add(recommendation);
            _temporaryMapRecommendationKey = recommendation.SelectionKey;
            changed = true;
        }

        if (changed)
        {
            OnPropertyChanged(nameof(MapRecommendations));
        }
    }

    private async Task SelectAdjacentRecommendationAsync(int offset)
    {
        if (SelectedRecommendation is null || VisibleNearbyRecommendations.Count < 2)
        {
            return;
        }

        var currentIndex = FindSelectedRecommendationIndex();
        if (currentIndex < 0)
        {
            await SelectRecommendationAsync(VisibleNearbyRecommendations[0]);
            return;
        }

        var nextIndex = (currentIndex + offset + VisibleNearbyRecommendations.Count)
            % VisibleNearbyRecommendations.Count;
        await SelectRecommendationAsync(VisibleNearbyRecommendations[nextIndex]);
    }

    private async Task LoadSelectedRecommendationDetailAsync(RecommendationDto recommendation)
    {
        if (recommendation.Id == Guid.Empty)
        {
            IsSelectedRecommendationLoading = false;
            return;
        }

        try
        {
            var token = await sessionService.GetTokenAsync();
            if (string.IsNullOrWhiteSpace(token))
            {
                return;
            }

            var detail = await apiClient.GetMobileRecommendationDetailAsync(token, recommendation.Id);
            if (detail is null || SelectedRecommendation?.Id != recommendation.Id)
            {
                return;
            }

            SelectedRecommendation = detail with { DistanceKm = recommendation.DistanceKm };
        }
        catch
        {
            // Keep the cached summary available when the detail endpoint is offline.
        }
        finally
        {
            if (SelectedRecommendation?.Id == recommendation.Id)
            {
                IsSelectedRecommendationLoading = false;
            }
        }
    }

    private int FindSelectedRecommendationIndex()
    {
        if (SelectedRecommendation is null)
        {
            return -1;
        }

        for (var index = 0; index < VisibleNearbyRecommendations.Count; index++)
        {
            if (VisibleNearbyRecommendations[index].SelectionKey == SelectedRecommendation.SelectionKey)
            {
                return index;
            }
        }

        return -1;
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

    private sealed record CatalogSearchEntry(
        RecommendationDto Item,
        string NormalizedTitle,
        string NormalizedMetadata,
        string NormalizedDescription);
}

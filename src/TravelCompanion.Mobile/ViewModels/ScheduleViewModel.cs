using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using TravelCompanion.Mobile.Pages;
using TravelCompanion.Mobile.Services;
using TravelCompanion.Shared;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Mobile.ViewModels;

public sealed partial class ScheduleViewModel : ViewModelBase, ISessionStateResettable
{
    private const string AllCitiesKey = "All Cities";
    private readonly AuthSessionService _sessionService;
    private readonly MobileBootstrapStore _bootstrapStore;
    private readonly MobileTodayStore _todayStore;
    private readonly TravelCompanionApiClient _apiClient;
    private readonly ILocationService _locationService;
    private readonly SessionLogoutService _sessionLogoutService;
    private readonly BuilderTripStore _builderTripStore;
    private readonly OfflineSyncCoordinator _syncCoordinator;
    private readonly MobileSyncStateStore _syncStateStore;
    private readonly ProductAnalyticsTracker _analytics;
    private readonly ILogger<ScheduleViewModel> _logger;
    private readonly List<ScheduleItemDto> _allItems = [];
    private readonly List<RecommendationDto> _recommendations = [];
    private readonly Dictionary<string, ScheduleTypeSectionViewModel> _sectionCache = new(StringComparer.Ordinal);
    private ReservationType _selectedType = ReservationType.Event;
    private string _tripTitle = "Your Trip";
    private string? _tripDates;
    private DateOnly? _tripStartsOn;
    private DateOnly? _tripEndsOn;
    private Guid? _tripId;
    private int? _builderRevision;
    private DateOnly? _selectedDate;
    private string _selectedCity = "Tu viaje";
    private string? _previewMessage;
    private string? _stayTitle;
    private ScheduleItemDto? _selectedItem;
    private ScheduleItemDto? _focusItem;
    private TodayDto? _today;
    private ScheduleTypeSectionViewModel? _activeSection;
    private IReadOnlyList<ScheduleDayViewModel> _activeDays = [];
    private IReadOnlyList<ScheduleTimelineItemViewModel> _selectedTimelineItems = [];
    private IReadOnlyList<ScheduleTodaySectionViewModel> _todaySections = [];
    private IReadOnlyList<ScheduleTodayLoadingSectionViewModel> _todayLoadingSections = [];
    private bool _isTodayLoading;
    private CancellationTokenSource? _selectedDayLoadCancellation;
    private GeoPointDto? _currentLocation;
    private bool _hasRequestedLocation;
    private readonly HashSet<Guid> _nearbyVisitPrompts = [];
    private readonly Dictionary<string, (DateTimeOffset SavedAt, ItineraryRouteDto Value)> _routeCache = [];
    private readonly Dictionary<DateOnly, string> _citiesByDate = [];
    private readonly Dictionary<DateOnly, TodayHotelBaseDto> _hotelsByDate = [];
    private readonly Dictionary<DateOnly, TodayDto> _todayByDate = [];
    private readonly Dictionary<DateOnly, DayReviewDto> _dayReviewsByDate = [];
    private readonly HashSet<DateOnly> _trackedDayReviews = [];
    private TodayHotelBaseDto? _selectedHotelBase;
    private DayReviewViewModel? _selectedDayReview;
    private string _destinationName = "Tu viaje";

    [RelayCommand]
    private async Task CalculateRouteAsync(ItineraryRouteViewModel? route)
    {
        if (route is null || route.ItemId == Guid.Empty || !_sessionService.CanCalculateRoutes) return;
        var token = await _sessionService.GetTokenAsync();
        if (string.IsNullOrWhiteSpace(token)) return;
        route.MarkLoading();
        GeoPointDto? origin = null;
        if (route.UseCurrentLocation)
        {
            origin = await _locationService.GetCurrentLocationAsync(CancellationToken.None);
            if (origin is null)
            {
                route.Apply(null);
                return;
            }
        }
        var request = new ItineraryRouteRequest(route.Mode, origin?.Latitude, origin?.Longitude);
        var key = $"{route.ItemId}:{route.Mode}:{origin?.Latitude:0.####}:{origin?.Longitude:0.####}";
        ItineraryRouteDto? result;
        if (_routeCache.TryGetValue(key, out var cached)
            && DateTimeOffset.UtcNow - cached.SavedAt < TimeSpan.FromMinutes(5))
        {
            result = cached.Value;
        }
        else
        {
            result = await _apiClient.GetItineraryRouteAsync(token, route.ItemId, request, CancellationToken.None);
            if (result is not null) _routeCache[key] = (DateTimeOffset.UtcNow, result);
        }
        route.Apply(result);
    }

    public ObservableCollection<ScheduleTypeSectionViewModel> TypeSections { get; } = [];
    public ObservableCollection<ScheduleTypeFilterViewModel> TypeFilters { get; } = [];
    public ObservableCollection<CityFilterViewModel> CityFilters { get; } = [];
    public ObservableCollection<ScheduleDayFilterViewModel> DayFilters { get; } = [];
    public IReadOnlyList<ScheduleDayViewModel> ActiveDays
    {
        get => _activeDays;
        private set => SetProperty(ref _activeDays, value);
    }

    public IReadOnlyList<ScheduleTimelineItemViewModel> SelectedTimelineItems
    {
        get => _selectedTimelineItems;
        private set => SetProperty(ref _selectedTimelineItems, value);
    }

    public IReadOnlyList<ScheduleTodaySectionViewModel> TodaySections
    {
        get => _todaySections;
        private set => SetProperty(ref _todaySections, value);
    }

    public IReadOnlyList<ScheduleTodayLoadingSectionViewModel> TodayLoadingSections
    {
        get => _todayLoadingSections;
        private set => SetProperty(ref _todayLoadingSections, value);
    }

    public DayReviewViewModel? SelectedDayReview
    {
        get => _selectedDayReview;
        private set
        {
            if (SetProperty(ref _selectedDayReview, value))
            {
                OnPropertyChanged(nameof(ShowDayReview));
            }
        }
    }

    public bool ShowDayReview => !ShowTodayLoading && SelectedDayReview is not null;
    public bool ShowImproveDay => _selectedDate.HasValue && _tripId.HasValue;
    public bool IsSelectedDayLocked => _sessionService.IsFreeMapPreview
        && _tripStartsOn is { } start && _selectedDate is { } date
        && !FreePlanningPolicy.CanPlanDate(start, date);
    public bool CanEditSelectedDay => _sessionService.CanEditItinerary && !IsSelectedDayLocked;
    public string LockedDayMessage => LocalizationResourceManager.Instance["FreePlanningLockedDay"];
    public string UnlockTripLabel => LocalizationResourceManager.Instance["FreePlanningUnlockTrip"];
    public string ImproveDayLabel => IsSelectedDayLocked ? UnlockTripLabel : LocalizationResourceManager.Instance["TodayImproveDay"];
    public bool HasFreshVisibleData => _bootstrapStore.HasFreshSnapshot()
        && (!_selectedDate.HasValue || _todayStore.HasFreshSnapshot(_selectedDate.Value));

    public bool HasScheduleItems => _allItems.Count > 0;
    public bool HasItineraryActions => _tripId.HasValue;
    public bool CanManageItinerary => _sessionService.IsBuilder
        && CanEditSelectedDay
        && !_sessionService.RequiresTripSetup
        && _tripStartsOn.HasValue;
    public bool ShowTrialBanner => _sessionService.IsTrial;
    public string TrialBannerText
    {
        get
        {
            if (!_sessionService.IsTrial) return string.Empty;
            if (_sessionService.TrialState == TrialAccessState.NotStarted)
                return "La prueba de 30 minutos comenzará al crear el itinerario.";
            if (_sessionService.TrialEditingExpiresAtUtc is { } editingExpiry && editingExpiry > DateTimeOffset.UtcNow)
            {
                var remaining = editingExpiry - DateTimeOffset.UtcNow;
                return $"Prueba gratuita · {Math.Max(0, (int)remaining.TotalMinutes):00}:{Math.Max(0, remaining.Seconds):00} para editar";
            }
            if (_sessionService.TrialDraftExpiresAtUtc is { } draftExpiry && draftExpiry > DateTimeOffset.UtcNow)
                return $"Borrador protegido hasta {draftExpiry.ToLocalTime():d MMM}. Activa el pase para seguir editando.";
            return "La prueba terminó. Activa el pase para recuperar tu itinerario.";
        }
    }

    public void RefreshTrialCountdown()
    {
        OnPropertyChanged(nameof(ShowTrialBanner));
        OnPropertyChanged(nameof(TrialBannerText));
        OnPropertyChanged(nameof(CanManageItinerary));
    }

    [RelayCommand]
    private Task RedeemPassAsync() => PaywallNavigation.OpenAsync(TravelCompanion.Shared.Dtos.PaywallEntryPoint.Today);

    public bool HasSelectedDayItems => TodaySections.Any(section => section.HasContent);
    public bool HasFocusItem => _focusItem is not null;
    public bool ShowInitialLoading => IsBusy && DayFilters.Count == 0;
    public bool ShowTodayLoading => _isTodayLoading && _selectedDate.HasValue;
    public bool ShowTodayContent => !ShowTodayLoading;
    public bool ShowEmptyState => HasLoaded
        && !IsBusy
        && !HasSelectedDayItems
        && !HasStayCard
        && !_tripStartsOn.HasValue;
    public bool HasPreviewMessage => !string.IsNullOrWhiteSpace(PreviewMessage);
    public bool HasStayCard => !string.IsNullOrWhiteSpace(StayTitle);
    public string StayAddress => _selectedHotelBase?.Address ?? string.Empty;
    public bool CanOpenStayMap => _selectedHotelBase is not null;
    public string? StayAttribution => _selectedHotelBase?.Attribution;

    [RelayCommand]
    private async Task OpenStayMapAsync()
    {
        if (!CanOpenStayMap || _selectedHotelBase is not { } hotel) return;
        await GoogleMapsLauncher.OpenAsync($"{hotel.Name}, {hotel.Address}", hotel.ProviderPlaceId);
    }
    public string SelectedCity => _selectedCity;
    public string SelectedDateLabel => _selectedDate.HasValue
        ? FormatLongDate(_selectedDate.Value)
        : TripDates ?? string.Empty;
    public string AmbientGlyph => GetAmbientGlyph(SelectedCity);
    public string? PreviewMessage
    {
        get => _previewMessage;
        private set => SetProperty(ref _previewMessage, value);
    }

    public string? StayTitle
    {
        get => _stayTitle;
        private set => SetProperty(ref _stayTitle, value);
    }

    public string SelectedTypeLabel => _selectedType switch
    {
        ReservationType.Flight => "Vuelos",
        ReservationType.Lodging => "Hospedajes",
        _ => "Eventos"
    };
    public string FocusTitle => _focusItem?.Title ?? "Tu viaje";
    public string FocusSubtitle => _focusItem is null
        ? "Cuando haya reservas, vas a ver aca el proximo momento relevante."
        : $"{_focusItem.TypeLabel} en {NormalizeCity(_focusItem.City)}";
    public string FocusMeta => _focusItem is null
        ? TripDates ?? string.Empty
        : $"{_focusItem.Date:MMM d} · {_focusItem.StartsAt:HH\\:mm}";

    public ScheduleViewModel(
        AuthSessionService sessionService,
        MobileBootstrapStore bootstrapStore,
        MobileTodayStore todayStore,
        TravelCompanionApiClient apiClient,
        ILocationService locationService,
        SessionLogoutService sessionLogoutService,
        BuilderTripStore builderTripStore,
        OfflineSyncCoordinator syncCoordinator,
        MobileSyncStateStore syncStateStore,
        ProductAnalyticsTracker analytics,
        ILogger<ScheduleViewModel> logger)
    {
        _sessionService = sessionService;
        _bootstrapStore = bootstrapStore;
        _todayStore = todayStore;
        _apiClient = apiClient;
        _locationService = locationService;
        _sessionLogoutService = sessionLogoutService;
        _builderTripStore = builderTripStore;
        _syncCoordinator = syncCoordinator;
        _syncStateStore = syncStateStore;
        _analytics = analytics;
        _logger = logger;
        _bootstrapStore.ScheduleUpdated += OnScheduleCacheUpdated;
    }

    public string TripTitle
    {
        get => _tripTitle;
        set => SetProperty(ref _tripTitle, value);
    }

    public string? TripDates
    {
        get => _tripDates;
        set => SetProperty(ref _tripDates, value);
    }

    public ScheduleItemDto? SelectedItem
    {
        get => _selectedItem;
        set => SetProperty(ref _selectedItem, value);
    }

    public void ResetForNewSession()
    {
        CancelSelectedDayLoading();
        _routeCache.Clear();
        _citiesByDate.Clear();
        _hotelsByDate.Clear();
        _todayByDate.Clear();
        _dayReviewsByDate.Clear();
        _trackedDayReviews.Clear();
        ResetLoadState();
        _allItems.Clear();
        _recommendations.Clear();
        _sectionCache.Clear();
        ActiveDays = [];
        SelectedTimelineItems = [];
        TodaySections = [];
        TodayLoadingSections = [];
        SetTodayLoading(false);
        TypeSections.Clear();
        _activeSection = null;
        TypeFilters.Clear();
        CityFilters.Clear();
        DayFilters.Clear();
        _selectedType = ReservationType.Event;
        _tripStartsOn = null;
        _tripEndsOn = null;
        _tripId = null;
        _builderRevision = null;
        _selectedDate = null;
        _selectedCity = "Tu viaje";
        PreviewMessage = null;
        StayTitle = null;
        _focusItem = null;
        _today = null;
        _selectedHotelBase = null;
        SelectedDayReview = null;
        _destinationName = "Tu viaje";
        _currentLocation = null;
        _hasRequestedLocation = false;
        _nearbyVisitPrompts.Clear();
        TripTitle = "Your Trip";
        TripDates = null;
        SelectedItem = null;
        NotifySelectedDayChanged();
        NotifyFocusChanged();
        OnPropertyChanged(nameof(CanManageItinerary));
        OnPropertyChanged(nameof(HasItineraryActions));
    }

    [RelayCommand]
    private Task LoadScheduleAsync()
    {
        return LoadAsync(async ct =>
        {
            var token = await _sessionService.GetTokenAsync();
            if (string.IsNullOrWhiteSpace(token))
            {
                _sessionService.Clear();
                await Shell.Current.GoToAsync("//login");
                return;
            }

            await LoadScheduleLocalFirstAsync(token, forceRefresh: false, ct);
            await RefreshBuilderSetupVersionAsync(token, ct);
        });
    }

    [RelayCommand]
    private Task RefreshScheduleAsync()
    {
        return LoadAsync(async ct =>
        {
            var token = await _sessionService.GetTokenAsync();
            if (string.IsNullOrWhiteSpace(token))
            {
                _sessionService.Clear();
                await Shell.Current.GoToAsync("//login");
                return;
            }

            await _syncCoordinator.SynchronizeVersionsAsync(token, force: true, ct);
            await LoadScheduleLocalFirstAsync(token, forceRefresh: false, ct);
            await RefreshBuilderSetupVersionAsync(token, ct);
        });
    }

    [RelayCommand]
    private async Task OpenScheduleItemAsync(ScheduleItemDto? item)
    {
        if (item is null)
        {
            return;
        }

        SelectedItem = null;
        await Shell.Current.GoToAsync(
            nameof(ScheduleItemDetailPage),
            new Dictionary<string, object>
            {
                ["ScheduleItem"] = item
            });
    }

    [RelayCommand]
    private void ToggleTypeFilter(ReservationType type)
    {
        var stopwatch = Stopwatch.StartNew();
        _selectedType = type;
        OnPropertyChanged(nameof(SelectedTypeLabel));
        foreach (var filter in TypeFilters)
        {
            filter.IsSelected = filter.Type == type;
        }

        UpdateCityFilters();
        ApplyCityFilter();
        stopwatch.Stop();

        _logger.LogInformation(
            "Schedule type filter changed in {ElapsedMs}ms. Type={ReservationType}; VisibleDays={VisibleDays}; VisibleItems={VisibleItems}.",
            stopwatch.Elapsed.TotalMilliseconds,
            type,
            ActiveDays.Count,
            ActiveDays.Sum(day => day.Count));
    }

    [RelayCommand]
    private void ToggleCityFilter(string? cityName)
    {
        if (string.IsNullOrWhiteSpace(cityName))
        {
            return;
        }

        var stopwatch = Stopwatch.StartNew();
        var filter = CityFilters.FirstOrDefault(f => f.CityName == cityName);
        if (filter is null)
        {
            return;
        }

        // If clicking "All Cities"
        if (filter.IsAllCities)
        {
            // Deselect all other cities and select "All Cities"
            foreach (var f in CityFilters)
            {
                f.IsSelected = f.IsAllCities;
            }
        }
        else
        {
            // Toggle the clicked city
            filter.IsSelected = !filter.IsSelected;

            // If at least one specific city is selected, deselect "All Cities"
            var allCitiesFilter = CityFilters.FirstOrDefault(f => f.IsAllCities);
            if (allCitiesFilter is not null)
            {
                var anySelected = CityFilters.Any(f => !f.IsAllCities && f.IsSelected);
                allCitiesFilter.IsSelected = !anySelected;
            }
        }

        ApplyCityFilter();
        stopwatch.Stop();

        _logger.LogInformation(
            "Schedule city filter changed in {ElapsedMs}ms. City={CityName}; Type={ReservationType}; VisibleDays={VisibleDays}; VisibleItems={VisibleItems}.",
            stopwatch.Elapsed.TotalMilliseconds,
            cityName,
            _selectedType,
            ActiveDays.Count,
            ActiveDays.Sum(day => day.Count));
    }

    [RelayCommand]
    private void ClearFilters()
    {
        foreach (var filter in CityFilters)
        {
            filter.IsSelected = filter.IsAllCities;
        }

        ApplyCityFilter();
    }

    [RelayCommand]
    private async Task AddPersonalItemAsync(ScheduleTodaySectionViewModel? section)
    {
        if (section is null) return;
        if (!CanEditSelectedDay) { await RedeemPassAsync(); return; }
        if (_sessionService.RequiresTripSetup)
        {
            await Shell.Current.GoToAsync(nameof(BuilderSetupPage));
            return;
        }
        await Shell.Current.GoToAsync(nameof(ItineraryItemEditorPage), new Dictionary<string, object>
        {
            ["Date"] = section.Date,
            ["PeriodKey"] = section.PeriodKey
        });
    }

    [RelayCommand]
    private Task EditItineraryAsync()
    {
        return CanManageItinerary
            ? Shell.Current.GoToAsync(nameof(BuilderSetupPage))
            : Task.CompletedTask;
    }

    [RelayCommand]
    private Task DownloadOfflineAsync()
    {
        return LoadAsync(async ct =>
        {
            var token = await _sessionService.GetTokenAsync();
            if (string.IsNullOrWhiteSpace(token))
            {
                _sessionService.Clear();
                await Shell.Current.GoToAsync("//login");
                return;
            }

            var bootstrapResult = await _bootstrapStore.RefreshResultAsync(token, cancellationToken: ct);
            if (bootstrapResult.IsUnauthorized)
            {
                _sessionService.Clear();
                await Shell.Current.GoToAsync("//login");
                return;
            }
            if (!bootstrapResult.IsSuccess || bootstrapResult.Value is not { } bootstrap)
            {
                ErrorMessage = "No pudimos actualizar la copia offline. Inténtalo cuando tengas conexión.";
                return;
            }

            ApplyBootstrapSchedule(bootstrap);
            if (_selectedDate is { } date)
            {
                var todayResult = await _todayStore.RefreshResultAsync(token, date, null, ct);
                if (todayResult.Value is { } today)
                {
                    ApplyToday(today);
                }
            }
            StatusMessage = "Viaje guardado para consultar sin conexión.";
        });
    }

    [RelayCommand]
    private async Task ShareItineraryAsync()
    {
        if (!_tripStartsOn.HasValue || !_tripEndsOn.HasValue || _allItems.Count == 0)
        {
            return;
        }

        var text = ItineraryShareFormatter.Format(
            _destinationName,
            _tripStartsOn.Value,
            _tripEndsOn.Value,
            _allItems);
        await Share.Default.RequestAsync(new ShareTextRequest
        {
            Title = $"Mi viaje a {_destinationName}",
            Text = text
        });
    }

    [RelayCommand]
    private async Task DeleteItineraryAsync()
    {
        if (!CanManageItinerary)
        {
            return;
        }

        var confirmed = await Shell.Current.DisplayAlertAsync(
            "Eliminar itinerario",
            "Se borrarán las fechas, ciudades, hoteles y todos los planes y reservas de este itinerario. Tu PIN y acceso Pago seguirán activos.",
            "Eliminar itinerario",
            "Cancelar");
        if (!confirmed)
        {
            return;
        }

        await LoadAsync(async ct =>
        {
            var token = await _sessionService.GetTokenAsync();
            if (string.IsNullOrWhiteSpace(token))
            {
                ErrorMessage = "Tu sesión venció. Vuelve a ingresar con tu PIN.";
                return;
            }

            var tripId = _tripId;
            var expectedRevision = _builderRevision;
            if (!tripId.HasValue || !expectedRevision.HasValue)
            {
                var setup = await _builderTripStore.GetAsync(token, cancellationToken: ct);
                if (setup?.TripId is null || (tripId.HasValue && setup.TripId != tripId))
                {
                    ErrorMessage = "El itinerario ya no existe o fue reemplazado en otro dispositivo. Actualiza Today antes de continuar.";
                    return;
                }

                tripId = setup.TripId;
                expectedRevision = setup.Revision;
            }

            await _apiClient.DeleteBuilderTripSetupAsync(
                token,
                new DeleteBuilderTripSetupRequest(tripId.Value, expectedRevision.Value),
                ct);
            await _builderTripStore.ClearAsync();
            var userId = _sessionService.CurrentUserId;
            _sessionService.MarkTripDeleted();
            await _sessionLogoutService.ResetContentAsync(userId);
            if (Shell.Current is AppShell shell)
            {
                shell.ApplySessionTabs(_sessionService);
            }

            await Shell.Current.GoToAsync(AppShell.GetAuthenticatedLandingRoute(_sessionService));
        });
    }

    [RelayCommand]
    private Task EditPersonalItemAsync(TodayReservationViewModel? reservation)
    {
        if (reservation is null || !reservation.Item.IsTravelerOwned) return Task.CompletedTask;
        return Shell.Current.GoToAsync(nameof(ItineraryItemEditorPage), new Dictionary<string, object> { ["ScheduleItem"] = reservation.Item });
    }

    [RelayCommand]
    private Task OpenReviewIssueAsync(DayReviewIssueViewModel? issue)
    {
        if (issue is null || !issue.CanOpenPlan)
        {
            return Task.CompletedTask;
        }

        var candidates = issue.ItemIds
            .Select(id => _allItems.FirstOrDefault(candidate => candidate.Id == id))
            .Where(candidate => candidate is not null)
            .Cast<ScheduleItemDto>()
            .ToList();
        var item = candidates.LastOrDefault(candidate => candidate.IsTravelerOwned && CanManageItinerary)
            ?? candidates.LastOrDefault();
        if (item is null)
        {
            return Task.CompletedTask;
        }

        return item.IsTravelerOwned && CanManageItinerary
            ? Shell.Current.GoToAsync(nameof(ItineraryItemEditorPage), new Dictionary<string, object> { ["ScheduleItem"] = item })
            : Shell.Current.GoToAsync(nameof(ScheduleItemDetailPage), new Dictionary<string, object> { ["ScheduleItem"] = item });
    }

    [RelayCommand]
    private async Task ImproveDayAsync()
    {
        if (!ShowImproveDay || IsBusy) return;
        try
        {
            if (!CanEditSelectedDay) { await RedeemPassAsync(); return; }
            await Shell.Current.GoToAsync("//main/assistant", new ShellNavigationQueryParameters
            {
                ["ReviewDate"] = _selectedDate!.Value,
                ["ReviewCity"] = SelectedCity
            });
        }
        catch (Exception) { ErrorMessage = LocalizationResourceManager.Instance["PlanningTryAgain"]; }
    }

    [RelayCommand]
    private async Task DeletePersonalItemAsync(TodayReservationViewModel? reservation)
    {
        if (reservation is null || !reservation.Item.IsTravelerOwned) return;
        await DeleteTravelerItemAsync(reservation.Item);
    }

    [RelayCommand]
    private async Task RemovePersonalRecommendationAsync(TodayLocationViewModel? location)
    {
        if (location?.AssignedItem is not { IsTravelerOwned: true } item) return;
        await DeleteTravelerItemAsync(item);
    }

    private async Task DeleteTravelerItemAsync(ScheduleItemDto item)
    {
        if (!CanEditSelectedDay) { await RedeemPassAsync(); return; }
        var confirmed = await Shell.Current.DisplayAlertAsync("Quitar plan", $"¿Quitar {item.Title} del itinerario?", "Quitar", "Cancelar");
        if (!confirmed) return;
        var token = await _sessionService.GetTokenAsync();
        if (string.IsNullOrWhiteSpace(token)) return;
        var revision = _builderRevision;
        if (!revision.HasValue)
        {
            var setup = await _builderTripStore.GetAsync(token);
            revision = setup?.Revision;
        }
        if (!revision.HasValue) return;
        var result = await _apiClient.DeleteItineraryItemAsync(token, item.Id, revision.Value);
        if (result?.Success != true)
        {
            ErrorMessage = result?.Message ?? "No se pudo eliminar el plan.";
            return;
        }
        _builderRevision = result.Revision;
        await _todayStore.InvalidateAllAsync();
        await _bootstrapStore.RemoveScheduleItemAsync(item.Id, result.Revision);
        await _syncStateStore.AcknowledgeItineraryVersionAsync(result.Revision);
    }

    [RelayCommand]
    private async Task SelectDayAsync(ScheduleDayFilterViewModel? day)
    {
        if (day is null || _selectedDate == day.Date)
        {
            return;
        }

        var stopwatch = Stopwatch.StartNew();
        _selectedDate = day.Date;
        _today = _todayStore.HasFreshSnapshot(day.Date)
            ? _todayByDate.GetValueOrDefault(day.Date)
            : null;
        SetTodayLoading(_today is null);
        RebuildSelectedDay();
        stopwatch.Stop();

        _logger.LogInformation(
            "Schedule day changed in {ElapsedMs}ms. Date={Date}; VisibleItems={VisibleItems}.",
            stopwatch.Elapsed.TotalMilliseconds,
            day.Date,
            SelectedTimelineItems.Count);

        var token = await _sessionService.GetTokenAsync();
        if (!string.IsNullOrWhiteSpace(token))
        {
            CancelSelectedDayLoading();
            var loadCancellation = new CancellationTokenSource();
            _selectedDayLoadCancellation = loadCancellation;
            try
            {
                await LoadTodayForSelectedDateAsync(token, forceRefresh: false, loadCancellation.Token);
            }
            catch (OperationCanceledException)
            {
                // Expected when another date is selected or the page is closed.
            }
            finally
            {
                if (ReferenceEquals(_selectedDayLoadCancellation, loadCancellation))
                {
                    _selectedDayLoadCancellation = null;
                }
                loadCancellation.Dispose();
            }
        }
    }

    public new void CancelLoading()
    {
        base.CancelLoading();
        CancelSelectedDayLoading();
    }

    private void CancelSelectedDayLoading()
    {
        _selectedDayLoadCancellation?.Cancel();
        _selectedDayLoadCancellation = null;
    }

    private async Task LoadScheduleLocalFirstAsync(
        string token,
        bool forceRefresh,
        CancellationToken cancellationToken = default)
    {
        var usableContentStopwatch = Stopwatch.StartNew();
        var usableContentLogged = false;
        if (forceRefresh)
        {
            _hasRequestedLocation = false;
        }

        var cached = await _bootstrapStore.GetCachedAsync(cancellationToken: cancellationToken);
        var isOnline = Connectivity.Current.NetworkAccess == NetworkAccess.Internet;
        var canShowCached = cached is not null
            && (!isOnline || _bootstrapStore.HasFreshSnapshot());
        if (canShowCached)
        {
            ApplyBootstrapSchedule(cached!.Value);
            HasLoaded = true;
            _logger.LogInformation(
                "Schedule usable content available in {ElapsedMs}ms. Source=cache; ForceRefresh={ForceRefresh}.",
                usableContentStopwatch.Elapsed.TotalMilliseconds,
                forceRefresh);
            usableContentLogged = true;
            MarkLastUpdated(cached.SavedAt);
            StatusMessage = forceRefresh
                ? "Actualizando itinerario..."
                : null;
        }
        else if (cached is not null && isOnline)
        {
            HideExpiredTodaySnapshot();
        }

        var shouldRefreshBootstrap = forceRefresh || !_bootstrapStore.HasFreshSnapshot();
        var bootstrapRefreshed = false;
        if (shouldRefreshBootstrap)
        {
            var result = await _bootstrapStore.RefreshResultAsync(token, cancellationToken: cancellationToken);
            if (result.IsUnauthorized)
            {
                _sessionService.Clear();
                await Shell.Current.GoToAsync("//login");
                return;
            }

            if (result.Value is { } bootstrap)
            {
                ApplyBootstrapSchedule(bootstrap);
                if (!usableContentLogged)
                {
                    _logger.LogInformation(
                        "Schedule usable content available in {ElapsedMs}ms. Source=network; ForceRefresh={ForceRefresh}.",
                        usableContentStopwatch.Elapsed.TotalMilliseconds,
                        forceRefresh);
                    usableContentLogged = true;
                }
                MarkLastUpdated(DateTimeOffset.UtcNow);
                bootstrapRefreshed = true;
                StatusMessage = null;
            }
            else if (cached is null)
            {
                throw new HttpRequestException("No pudimos cargar el itinerario. Comprueba la conexión e inténtalo de nuevo.");
            }
            else
            {
                ApplyBootstrapSchedule(cached.Value);
                MarkLastUpdated(cached.SavedAt);
                StatusMessage = OfflineCacheService.FormatSavedAt(cached.SavedAt);
            }
        }

        _currentLocation ??= await _locationService.GetLastKnownLocationAsync(cancellationToken);
        await LoadTodayForSelectedDateAsync(
            token,
            forceRefresh: forceRefresh || bootstrapRefreshed,
            cancellationToken);
        _ = UpdateLocationAfterContentAsync(cancellationToken);
    }

    private async Task RefreshBuilderSetupVersionAsync(string token, CancellationToken cancellationToken)
    {
        if (!_sessionService.IsBuilder || !_tripId.HasValue)
        {
            _builderRevision = null;
            return;
        }

        try
        {
            var setup = await _builderTripStore.GetAsync(token, cancellationToken: cancellationToken);
            _builderRevision = setup?.TripId == _tripId ? setup.Revision : null;
            if (setup?.TripId == _tripId)
            {
                ApplyBuilderDayMetadata(setup);
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
        {
            _builderRevision = null;
        }
    }

    private async Task LoadTodayForSelectedDateAsync(
        string token,
        bool forceRefresh,
        CancellationToken cancellationToken)
    {
        if (forceRefresh) _routeCache.Clear();
        if (!_selectedDate.HasValue)
        {
            return;
        }

        var selectedDate = _selectedDate.Value;
        var cached = await _todayStore.GetCachedAsync(selectedDate, cancellationToken);
        var isOnline = Connectivity.Current.NetworkAccess == NetworkAccess.Internet;
        var canShowCached = cached is not null
            && (!isOnline || _todayStore.HasFreshSnapshot(selectedDate));
        if (canShowCached && _selectedDate == selectedDate)
        {
            ApplyToday(cached!.Value);
        }
        else if (cached is not null && isOnline && _selectedDate == selectedDate)
        {
            HideExpiredTodaySnapshot();
        }

        if (!forceRefresh && _todayStore.HasFreshSnapshot(selectedDate))
        {
            if (_selectedDate == selectedDate)
            {
                await PromptForNearbyVisitAsync(token, cancellationToken);
            }
            return;
        }

        try
        {
            var result = await _todayStore.RefreshResultAsync(
                token,
                selectedDate,
                null,
                cancellationToken);
            if (result.IsUnauthorized)
            {
                _sessionService.Clear();
                await Shell.Current.GoToAsync("//login");
                return;
            }

            if (result.Value is { } today && _selectedDate == selectedDate)
            {
                ApplyToday(today);
                await PromptForNearbyVisitAsync(token, cancellationToken);
            }
            else if (cached is null && _selectedDate == selectedDate)
            {
                CompleteTodayLoadingWithScheduleFallback();
            }
            else if (!canShowCached && cached is not null && _selectedDate == selectedDate)
            {
                ApplyToday(cached.Value);
                StatusMessage = OfflineCacheService.FormatSavedAt(cached.SavedAt);
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
        {
            if (_selectedDate != selectedDate)
            {
                return;
            }

            if (cached is not null)
            {
                if (!canShowCached)
                {
                    ApplyToday(cached.Value);
                    StatusMessage = OfflineCacheService.FormatSavedAt(cached.SavedAt);
                }
            }
            else
            {
                StatusMessage = "No pudimos completar las sugerencias. Mostrando el itinerario disponible.";
                CompleteTodayLoadingWithScheduleFallback();
            }
        }
    }

    private void ApplyToday(TodayDto today, bool includeHotelMetadata = true)
    {
        _todayByDate[today.Date] = today;
        if (!string.IsNullOrWhiteSpace(today.City))
        {
            _citiesByDate[today.Date] = today.City;
            DayFilters.FirstOrDefault(item => item.Date == today.Date)?.UpdateCity(today.City);
        }
        if (includeHotelMetadata && today.HotelBase is not null)
        {
            _hotelsByDate[today.Date] = today.HotelBase;
        }
        if (_selectedDate != today.Date)
        {
            return;
        }

        _today = today;
        SetTodayLoading(false);
        RebuildSelectedDay();
    }

    private void ApplyBuilderDayMetadata(BuilderTripSetupDto setup)
    {
        _citiesByDate.Clear();
        _hotelsByDate.Clear();
        foreach (var segment in setup.Segments)
        {
            for (var date = segment.StartsOn; date <= segment.EndsOn; date = date.AddDays(1))
            {
                _citiesByDate[date] = segment.City;
                if (!string.IsNullOrWhiteSpace(segment.HotelName) || !string.IsNullOrWhiteSpace(segment.HotelPlaceId))
                {
                    _hotelsByDate[date] = new TodayHotelBaseDto(
                        string.IsNullOrWhiteSpace(segment.HotelName) ? "Hotel" : segment.HotelName,
                        segment.HotelAddress ?? string.Empty,
                        segment.HotelPlaceId,
                        segment.HotelLatitude,
                        segment.HotelLongitude)
                    {
                        Attribution = string.IsNullOrWhiteSpace(segment.HotelPlaceId) ? null : "Google Maps"
                    };
                }
            }
        }

        foreach (var day in DayFilters)
        {
            if (_citiesByDate.TryGetValue(day.Date, out var city))
            {
                day.UpdateCity(city);
            }
        }
        RebuildSelectedDay();
    }

    private void CompleteTodayLoadingWithScheduleFallback()
    {
        SetTodayLoading(false);
        RebuildSelectedDay();
    }

    private void HideExpiredTodaySnapshot()
    {
        if (_selectedDate is { } selectedDate)
        {
            _todayByDate.Remove(selectedDate);
        }

        _today = null;
        SetTodayLoading(_selectedDate.HasValue);
        RebuildSelectedDay();
    }

    private void ApplyBootstrapSchedule(MobileBootstrapDto bootstrap)
    {
        _recommendations.Clear();
        _recommendations.AddRange(bootstrap.Recommendations ?? []);

        if (bootstrap.Schedule is null)
        {
            ApplyEmptySchedule();
            return;
        }

        ApplySchedule(bootstrap.Schedule);
    }

    private void ApplySchedule(TripScheduleDto schedule)
    {
        var stopwatch = Stopwatch.StartNew();
        var sourceItems = schedule.Items ?? [];
        var previouslySelectedDate = _tripId == schedule.TripId
            ? _selectedDate
            : null;
        TripTitle = $"{schedule.DestinationName} for {schedule.TravelerName}";
        TripDates = $"{schedule.StartsOn:MMM d} - {schedule.EndsOn:MMM d, yyyy}";
        _destinationName = schedule.DestinationName;
        _tripStartsOn = schedule.StartsOn;
        _tripEndsOn = schedule.EndsOn;
        _tripId = schedule.TripId;
        if (_sessionService.IsBuilder)
        {
            _builderRevision = schedule.Revision;
        }
        OnPropertyChanged(nameof(CanManageItinerary));
        OnPropertyChanged(nameof(HasItineraryActions));
        _allItems.Clear();
        _allItems.AddRange(sourceItems);
        _dayReviewsByDate.Clear();
        var dayReviews = schedule.DayReviews ?? ScheduleReviewAnalyzer.Analyze(sourceItems, schedule.StartsOn, schedule.EndsOn);
        foreach (var review in dayReviews)
        {
            _dayReviewsByDate[review.Date] = review;
        }
        _focusItem = GetFocusItem(_allItems);
        _selectedDate = previouslySelectedDate.HasValue
            && previouslySelectedDate.Value >= schedule.StartsOn
            && previouslySelectedDate.Value <= schedule.EndsOn
                ? previouslySelectedDate
                : GetInitialSelectedDate(schedule, _allItems);
        _today = _selectedDate.HasValue && _todayStore.HasFreshSnapshot(_selectedDate.Value)
            ? _todayByDate.GetValueOrDefault(_selectedDate.Value)
            : null;
        SetTodayLoading(_today is null);
        NotifyFocusChanged();
        RebuildDayFilters(schedule);
        RebuildSelectedDay();
        stopwatch.Stop();

        _logger.LogInformation(
            "Schedule applied in {ElapsedMs}ms. SourceItems={SourceItems}; SelectedDate={SelectedDate}; VisibleItems={VisibleItems}.",
            stopwatch.Elapsed.TotalMilliseconds,
            sourceItems.Count,
            _selectedDate,
            SelectedTimelineItems.Count);
    }

    private void ApplyEmptySchedule()
    {
        TripTitle = "Your Trip";
        TripDates = "No reservations yet.";
        _allItems.Clear();
        _sectionCache.Clear();
        _focusItem = null;
        ActiveDays = [];
        SelectedTimelineItems = [];
        TodaySections = [];
        TodayLoadingSections = [];
        SetTodayLoading(false);
        TypeSections.Clear();
        _activeSection = null;
        TypeFilters.Clear();
        UpdateTypeFilters();
        CityFilters.Clear();
        CityFilters.Add(new CityFilterViewModel(AllCitiesKey, isSelected: true));
        DayFilters.Clear();
        _tripStartsOn = null;
        _tripEndsOn = null;
        _tripId = null;
        _builderRevision = null;
        _destinationName = "Tu viaje";
        _today = null;
        _selectedHotelBase = null;
        _citiesByDate.Clear();
        _hotelsByDate.Clear();
        _todayByDate.Clear();
        _dayReviewsByDate.Clear();
        SelectedDayReview = null;
        OnPropertyChanged(nameof(CanManageItinerary));
        OnPropertyChanged(nameof(HasItineraryActions));
        _selectedDate = null;
        _selectedCity = "Tu viaje";
        PreviewMessage = null;
        StayTitle = null;
        NotifySelectedDayChanged();
        NotifyFocusChanged();
        OnPropertyChanged(nameof(HasScheduleItems));
        OnPropertyChanged(nameof(HasSelectedDayItems));
        OnPropertyChanged(nameof(ShowInitialLoading));
        OnPropertyChanged(nameof(ShowEmptyState));
    }

    private void RebuildDayFilters(TripScheduleDto schedule)
    {
        DayFilters.Clear();

        var totalDays = schedule.EndsOn.DayNumber - schedule.StartsOn.DayNumber;
        for (var offset = 0; offset <= totalDays; offset++)
        {
            var date = schedule.StartsOn.AddDays(offset);
            DayFilters.Add(new ScheduleDayFilterViewModel(
                date,
                offset + 1,
                GetCityForDate(date, schedule.DestinationName),
                _selectedDate == date,
                _sessionService.IsFreeMapPreview && !FreePlanningPolicy.CanPlanDate(schedule.StartsOn, date)));
        }
    }

    private void RebuildSelectedDay()
    {
        foreach (var filter in DayFilters)
        {
            filter.IsSelected = filter.Date == _selectedDate;
        }

        if (_selectedDate is null)
        {
            _selectedCity = "Tu viaje";
            _selectedHotelBase = null;
            StayTitle = null;
            OnPropertyChanged(nameof(StayAddress));
            OnPropertyChanged(nameof(CanOpenStayMap));
            OnPropertyChanged(nameof(StayAttribution));
            PreviewMessage = null;
            SelectedTimelineItems = [];
            TodaySections = [];
            TodayLoadingSections = [];
            ActiveDays = [];
            SelectedDayReview = null;
            NotifySelectedDayChanged();
            return;
        }

        var selectedDate = _selectedDate.Value;
        var hasTimedReservation = ScheduleReviewAnalyzer.HasTimedReservation(selectedDate, _allItems);
        SelectedDayReview = hasTimedReservation && _dayReviewsByDate.TryGetValue(selectedDate, out var review)
            ? new DayReviewViewModel(review)
            : null;
        if (SelectedDayReview is not null && _trackedDayReviews.Add(selectedDate))
            _ = _analytics.TrackAsync("day_review_viewed", "today", tripId: _tripId);
        _selectedCity = GetCityForDate(selectedDate, _destinationName);
        _selectedHotelBase = _hotelsByDate.GetValueOrDefault(selectedDate);
        _selectedHotelBase ??= _today?.Date == selectedDate ? _today.HotelBase : null;
        StayTitle = _selectedHotelBase?.Name ?? GetStayTitleForDate(selectedDate);
        OnPropertyChanged(nameof(StayAddress));
        OnPropertyChanged(nameof(CanOpenStayMap));
        OnPropertyChanged(nameof(StayAttribution));
        PreviewMessage = null;

        var selectedItems = _allItems
            .Where(item => item.Type != ReservationType.Lodging)
            .Where(item => item.Date == selectedDate)
            .OrderBy(item => item.StartsAt)
            .ToList();

        var dayNumber = _tripStartsOn.HasValue
            ? selectedDate.DayNumber - _tripStartsOn.Value.DayNumber + 1
            : 1;
        TodayLoadingSections = BuildTodayLoadingSections(dayNumber);
        TodaySections = _today is not null && _today.Date == selectedDate
            ? BuildTodaySections(_today, dayNumber)
            : ShowTodayLoading
                ? []
                : BuildTodaySections(selectedDate, dayNumber, selectedItems);
        SelectedTimelineItems = selectedItems
            .Select(item => new ScheduleTimelineItemViewModel(item, dayNumber))
            .ToList();
        ActiveDays = selectedItems.Count == 0
            ? []
            : [new ScheduleDayViewModel(selectedDate, selectedItems)];

        NotifySelectedDayChanged();
    }

    [RelayCommand]
    private async Task OpenRecommendationAsync(RecommendationDto? recommendation)
    {
        if (recommendation is null)
        {
            return;
        }

        await Shell.Current.GoToAsync(
            nameof(RecommendationDetailPage),
            new Dictionary<string, object>
            {
                ["Recommendation"] = recommendation,
                ["IsUnlocked"] = true
            });
    }

    [RelayCommand]
    private async Task MarkLocationVisitedAsync(TodayLocationViewModel? location)
    {
        if (location is null)
        {
            return;
        }

        var confirmed = await Shell.Current.DisplayAlertAsync(
            "Marcar visitado",
            $"Marcamos {location.Title} como visitado para bajarlo de prioridad?",
            "Si",
            "No");
        if (!confirmed)
        {
            return;
        }

        await RecordRecommendationSignalAsync(
            location,
            RecommendationSignal.VisitedConfirmed,
            "today_manual");
    }

    [RelayCommand]
    private async Task DismissLocationAsync(TodayLocationViewModel? location)
    {
        if (location is null)
        {
            return;
        }

        await RecordRecommendationSignalAsync(
            location,
            RecommendationSignal.Dismissed,
            "today_dismiss");
    }

    private async Task RecordRecommendationSignalAsync(
        TodayLocationViewModel location,
        RecommendationSignal signal,
        string source)
    {
        var token = await _sessionService.GetTokenAsync();
        if (string.IsNullOrWhiteSpace(token))
        {
            return;
        }

        var response = await _apiClient.SendRecommendationSignalAsync(
            token,
            location.Recommendation.Id,
            new RecommendationSignalRequest(
                signal,
                source,
                _currentLocation?.Latitude,
                _currentLocation?.Longitude,
                location.DistanceKm.HasValue ? location.DistanceKm.Value * 1000 : null,
                signal == RecommendationSignal.VisitedConfirmed ? 0.85m : null,
                DateTimeOffset.UtcNow));

        if (response?.Accepted == true)
        {
            StatusMessage = response.Message;
            await LoadTodayForSelectedDateAsync(token, forceRefresh: true, cancellationToken: default);
        }
        else
        {
            StatusMessage = "No pude registrar la accion. Intenta de nuevo cuando vuelva la conexion.";
        }
    }

    private async Task PromptForNearbyVisitAsync(
        string token,
        CancellationToken cancellationToken)
    {
        if (_today is null || _currentLocation is null)
        {
            return;
        }

        var nearby = _today.Sections
            .SelectMany(section => section.Recommendations)
            .Where(recommendation => !recommendation.IsVisited)
            .Where(recommendation => recommendation.DistanceKm is <= 0.1m)
            .OrderBy(recommendation => recommendation.DistanceKm)
            .FirstOrDefault(recommendation => _nearbyVisitPrompts.Add(recommendation.Recommendation.Id));
        if (nearby is null)
        {
            return;
        }

        var confirmed = await Shell.Current.DisplayAlertAsync(
            "Estas cerca",
            $"Parece que estas cerca de {nearby.Recommendation.Title}. Lo marcamos como visitado?",
            "Si",
            "No");
        if (!confirmed)
        {
            return;
        }

        await _apiClient.SendRecommendationSignalAsync(
            token,
            nearby.Recommendation.Id,
            new RecommendationSignalRequest(
                RecommendationSignal.VisitedConfirmed,
                "today_nearby_confirmation",
                _currentLocation.Latitude,
                _currentLocation.Longitude,
                nearby.DistanceKm.HasValue ? nearby.DistanceKm.Value * 1000 : null,
                0.9m,
                DateTimeOffset.UtcNow),
            cancellationToken);
        await LoadTodayForSelectedDateAsync(token, forceRefresh: true, cancellationToken);
    }

    private IReadOnlyList<ScheduleTodaySectionViewModel> BuildTodaySections(
        TodayDto today,
        int dayNumber)
    {
        return today.Sections
            .Select(section => new ScheduleTodaySectionViewModel(
                dayNumber,
                today.Date,
                section.PeriodKey,
                section.Title,
                section.Description,
                section.Recommendations
                    .Select(recommendation => new TodayLocationViewModel(
                        recommendation,
                        FindTravelerAssignedItem(
                            today.Date,
                            section.PeriodKey,
                            recommendation.Recommendation.Id),
                        CalculateDistanceKm(_currentLocation, recommendation.Recommendation)))
                    .ToList(),
                section.Reservations
                    .OrderBy(reservation => reservation.StartsAt)
                    .Select(reservation => new TodayReservationViewModel(
                        reservation,
                        _hotelsByDate.GetValueOrDefault(today.Date),
                        _sessionService.CanCalculateRoutes))
                    .ToList(),
                CanEditSelectedDay))
            .ToList();
    }

    private static IReadOnlyList<ScheduleTodayLoadingSectionViewModel> BuildTodayLoadingSections(int dayNumber) =>
        TodayPeriod.All
            .Select(period => new ScheduleTodayLoadingSectionViewModel($"Dia {dayNumber}", period.Label))
            .ToList();

    private IReadOnlyList<ScheduleTodaySectionViewModel> BuildTodaySections(
        DateOnly selectedDate,
        int dayNumber,
        IReadOnlyList<ScheduleItemDto> selectedReservations)
    {
        return TodayPeriod.All
            .Select(period =>
            {
                var periodItems = selectedReservations
                    .Where(item => period.Contains(item.StartsAt))
                    .OrderBy(item => item.StartsAt)
                    .ToList();
                var reservations = periodItems
                    .Where(item => item.PlanningKind != ScheduleItemKind.Recommendation)
                    .Select(item => new TodayReservationViewModel(
                        item,
                        _hotelsByDate.GetValueOrDefault(selectedDate),
                        _sessionService.CanCalculateRoutes))
                    .ToList();
                var assignedLocations = periodItems
                    .Where(item => item.PlanningKind == ScheduleItemKind.Recommendation && item.RecommendationId.HasValue)
                    .Select(item => new
                    {
                        Item = item,
                        Recommendation = _recommendations.FirstOrDefault(recommendation => recommendation.Id == item.RecommendationId)
                            ?? ScheduleRecommendationFallback.Create(item)
                    })
                    .Select(entry => new TodayLocationViewModel(
                        entry.Recommendation,
                        CalculateDistanceKm(_currentLocation, entry.Recommendation),
                        isAssigned: true,
                        assignedItem: entry.Item))
                    .ToList();
                var locations = assignedLocations;
                var curatedDescription = periodItems
                    .Where(item => item.PlanningKind == ScheduleItemKind.Recommendation)
                    .Select(item => ExtractCuratedDescription(item.Notes))
                    .FirstOrDefault(description => !string.IsNullOrWhiteSpace(description));

                return new ScheduleTodaySectionViewModel(
                    dayNumber,
                    selectedDate,
                    PeriodKey(period.Label),
                    period.Label,
                    string.IsNullOrWhiteSpace(curatedDescription)
                        ? CreateSectionDescription(period, reservations, locations)
                        : curatedDescription,
                    locations,
                    reservations,
                    CanEditSelectedDay);
            })
            .ToList();
    }

    private ScheduleItemDto? FindTravelerAssignedItem(
        DateOnly date,
        string periodKey,
        Guid recommendationId) =>
        _allItems.FirstOrDefault(item =>
            item.Date == date
            && item.RecommendationId == recommendationId
            && string.Equals(
                PeriodKey(TodayPeriod.All.First(period => period.Contains(item.StartsAt)).Label),
                periodKey,
                StringComparison.OrdinalIgnoreCase)
            && item.IsTravelerOwned);

    private static string PeriodKey(string label) => label switch
    {
        "Mañana" => "morning",
        "Medio dia" or "Medio día" => "midday",
        "Noche" => "night",
        _ => "afternoon"
    };

    private IReadOnlyList<RecommendationDto> SelectRecommendationsForPeriod(
        TodayPeriod period,
        DateOnly selectedDate,
        ISet<Guid> usedRecommendationIds,
        GeoPointDto? rankingLocation = null,
        int maxSuggestions = 2)
    {
        var selectedCity = GetCityForDate(selectedDate, string.Empty);
        var candidates = _recommendations
            .Where(recommendation => !usedRecommendationIds.Contains(recommendation.Id))
            .Select(recommendation => new
            {
                Recommendation = recommendation,
                Score = ScoreRecommendationForPeriod(recommendation, period, selectedCity)
            })
            .OrderByDescending(candidate => candidate.Score)
            .ThenBy(candidate => CalculateDistanceKm(rankingLocation, candidate.Recommendation) ?? decimal.MaxValue)
            .ThenBy(candidate => candidate.Recommendation.Title)
            .Take(Math.Clamp(maxSuggestions, 0, 3))
            .Select(candidate => candidate.Recommendation)
            .ToList();

        foreach (var recommendation in candidates)
        {
            usedRecommendationIds.Add(recommendation.Id);
        }

        return candidates;
    }

    private static int ScoreRecommendationForPeriod(
        RecommendationDto recommendation,
        TodayPeriod period,
        string selectedCity)
    {
        var text = string.Join(
            ' ',
            recommendation.Title,
            recommendation.Category,
            recommendation.Neighborhood,
            string.Join(' ', recommendation.Tags)).ToLowerInvariant();
        var score = period.Keywords.Count(keyword => text.Contains(keyword, StringComparison.OrdinalIgnoreCase)) * 4;

        if (!string.IsNullOrWhiteSpace(selectedCity)
            && text.Contains(selectedCity.ToLowerInvariant(), StringComparison.OrdinalIgnoreCase))
        {
            score += 2;
        }

        if (recommendation.SuggestedDurationMinutes is > 0 and <= 120)
        {
            score += 1;
        }

        if (recommendation.Rating >= 4)
        {
            score += 1;
        }

        return score;
    }

    private static string CreateSectionDescription(
        TodayPeriod period,
        IReadOnlyList<TodayReservationViewModel> reservations,
        IReadOnlyList<TodayLocationViewModel> locations)
    {
        if (reservations.Count > 0)
        {
            var reservationLabel = reservations.Count == 1
                ? reservations[0].Title
                : $"{reservations.Count} reservas";
            return $"{period.Label}: tenes {reservationLabel}. Dejo cerca algunas locations utiles por si queres completar el bloque sin desviar demasiado el dia.";
        }

        if (locations.Count > 0)
        {
            return $"{period.Label}: bloque libre para elegir algo liviano. Estas locations encajan bien para sumar contexto al dia sin convertirlo en una agenda pesada.";
        }

        return $"{period.Label}: sin reservas cargadas ni locations sugeridas por ahora.";
    }

    private static string ExtractCuratedDescription(string notes)
    {
        if (string.IsNullOrWhiteSpace(notes))
        {
            return string.Empty;
        }

        const string prefix = "Descripcion:";
        var start = notes.IndexOf(prefix, StringComparison.OrdinalIgnoreCase);
        if (start < 0)
        {
            return string.Empty;
        }

        start += prefix.Length;
        var end = notes.IndexOf(Environment.NewLine, start, StringComparison.Ordinal);
        return (end < 0 ? notes[start..] : notes[start..end]).Trim();
    }

    private async Task UpdateLocationAsync(CancellationToken cancellationToken)
    {
        if (_hasRequestedLocation)
        {
            return;
        }

        _hasRequestedLocation = true;
        _currentLocation = await _locationService.GetCurrentLocationAsync(cancellationToken);
        if (_currentLocation is not null)
        {
            RebuildSelectedDay();
        }
    }

    private async Task UpdateLocationAfterContentAsync(CancellationToken cancellationToken)
    {
        try
        {
            await UpdateLocationAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // Expected when leaving the screen or starting another load.
        }
        catch (Exception exception)
        {
            _logger.LogDebug(exception, "Location refresh after rendering Today failed.");
        }
    }

    private static decimal? CalculateDistanceKm(GeoPointDto? currentLocation, RecommendationDto recommendation)
    {
        if (currentLocation is null)
        {
            return recommendation.DistanceKm;
        }

        if (recommendation.DestinationId == Guid.Empty
            && recommendation.Latitude == 0
            && recommendation.Longitude == 0)
        {
            return null;
        }

        const double earthRadiusKm = 6371;

        static double ToRadians(decimal degrees) => (double)degrees * Math.PI / 180;

        var latitudeDelta = ToRadians(recommendation.Latitude - currentLocation.Latitude);
        var longitudeDelta = ToRadians(recommendation.Longitude - currentLocation.Longitude);
        var originLatitudeRadians = ToRadians(currentLocation.Latitude);
        var targetLatitudeRadians = ToRadians(recommendation.Latitude);

        var a = Math.Sin(latitudeDelta / 2) * Math.Sin(latitudeDelta / 2)
            + Math.Cos(originLatitudeRadians) * Math.Cos(targetLatitudeRadians)
            * Math.Sin(longitudeDelta / 2) * Math.Sin(longitudeDelta / 2);

        var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
        return Math.Round((decimal)(earthRadiusKm * c), 1);
    }

    private string GetCityForDate(DateOnly date, string fallback)
    {
        if (_citiesByDate.TryGetValue(date, out var configuredCity) && !string.IsNullOrWhiteSpace(configuredCity))
        {
            return configuredCity;
        }

        var sameDayCity = _allItems
            .Where(item => item.Date == date)
            .Select(item => NormalizeCity(item.City))
            .FirstOrDefault(city => !string.Equals(city, "Unknown City", StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrWhiteSpace(sameDayCity))
        {
            return sameDayCity;
        }

        var activeStayCity = _allItems
            .Where(item => item.Type == ReservationType.Lodging)
            .Where(item => item.Date <= date && (item.EndsOn is null || item.EndsOn >= date))
            .Select(item => NormalizeCity(item.City))
            .FirstOrDefault(city => !string.Equals(city, "Unknown City", StringComparison.OrdinalIgnoreCase));

        return string.IsNullOrWhiteSpace(activeStayCity)
            ? fallback
            : activeStayCity;
    }

    private string? GetStayTitleForDate(DateOnly date)
    {
        return _allItems
            .Where(item => item.Type == ReservationType.Lodging)
            .Where(item => item.Date == date)
            .OrderByDescending(item => item.Date)
            .Select(item => string.IsNullOrWhiteSpace(item.LocationName) ? item.Title : item.LocationName)
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
    }

    private void UpdateTypeFilters()
    {
        // Build filter list first to minimize CollectionChanged events
        var types = new[] { ReservationType.Event, ReservationType.Flight, ReservationType.Lodging };
        var newFilters = types.Select(type => new ScheduleTypeFilterViewModel(type, type == _selectedType)).ToList();

        TypeFilters.Clear();
        foreach (var filter in newFilters)
        {
            TypeFilters.Add(filter);
        }
    }

    private void UpdateCityFilters()
    {
        // Preserve current selections
        var currentSelections = CityFilters
            .Where(f => f.IsSelected)
            .Select(f => f.CityName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Build complete filter list first to minimize CollectionChanged events
        var newFilters = new List<CityFilterViewModel>();

        // Add "All Cities" filter
        var allCitiesSelected = currentSelections.Count == 0 ||
                               currentSelections.Contains(AllCitiesKey);
        newFilters.Add(new CityFilterViewModel(AllCitiesKey, isSelected: allCitiesSelected));

        // Add individual city filters
        var now = DateTime.Now;
        var cities = _allItems
            .Where(item => item.Type == _selectedType)
            .Where(item => !IsPast(item, now))
            .Select(item => NormalizeCity(item.City))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase);

        foreach (var city in cities)
        {
            var isSelected = currentSelections.Contains(city);
            newFilters.Add(new CityFilterViewModel(city, isSelected: isSelected));
        }

        // If we had selections but none exist anymore, select "All Cities"
        if (currentSelections.Count > 0 &&
            !newFilters.Any(f => !f.IsAllCities && f.IsSelected))
        {
            var allCities = newFilters.FirstOrDefault(f => f.IsAllCities);
            if (allCities is not null)
            {
                allCities.IsSelected = true;
            }
        }

        // Clear and rebuild in one pass
        CityFilters.Clear();
        foreach (var filter in newFilters)
        {
            CityFilters.Add(filter);
        }
    }

    private void ApplyCityFilter()
    {
        var selectedCities = CityFilters
            .Where(f => !f.IsAllCities && f.IsSelected)
            .Select(f => f.CityName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // If no specific cities selected or "All Cities" is selected, show all
        var allCitiesSelected = CityFilters
            .FirstOrDefault(f => f.IsAllCities)?
            .IsSelected ?? true;

        ShowScheduleSection(_selectedType, selectedCities, allCitiesSelected);
    }

    private void ShowScheduleSection(
        ReservationType type,
        IReadOnlySet<string> selectedCities,
        bool allCitiesSelected)
    {
        var section = GetOrCreateScheduleSection(type, selectedCities, allCitiesSelected);
        foreach (var existingSection in TypeSections)
        {
            existingSection.IsVisible = ReferenceEquals(existingSection, section);
        }

        _activeSection = section;
        ActiveDays = section.Days;
        section.IsVisible = true;

        OnPropertyChanged(nameof(HasScheduleItems));
        OnPropertyChanged(nameof(ShowInitialLoading));
        OnPropertyChanged(nameof(ShowEmptyState));
    }

    private ScheduleTypeSectionViewModel GetOrCreateScheduleSection(
        ReservationType type,
        IReadOnlySet<string> selectedCities,
        bool allCitiesSelected)
    {
        var cacheKey = GetSectionCacheKey(type, selectedCities, allCitiesSelected);
        if (_sectionCache.TryGetValue(cacheKey, out var section))
        {
            return section;
        }

        section = CreateScheduleSection(type, selectedCities, allCitiesSelected);
        _sectionCache[cacheKey] = section;
        TypeSections.Add(section);
        return section;
    }

    private ScheduleTypeSectionViewModel CreateScheduleSection(
        ReservationType type,
        IReadOnlySet<string> selectedCities,
        bool allCitiesSelected)
    {
        var now = DateTime.Now;
        var filteredItems = _allItems
            .Where(item => item.Type == type)
            .Where(item => !IsPast(item, now));

        if (!allCitiesSelected && selectedCities.Count > 0)
        {
            filteredItems = filteredItems.Where(item => selectedCities.Contains(NormalizeCity(item.City)));
        }

        var dayGroups = filteredItems
            .GroupBy(item => item.Date)
            .OrderBy(group => group.Key)
            .Select(group => new ScheduleDayViewModel(
                group.Key,
                group.OrderBy(item => item.StartsAt)))
            .ToList();

        return new ScheduleTypeSectionViewModel(
            type,
            GetSectionCacheKey(type, selectedCities, allCitiesSelected),
            dayGroups);
    }

    private static string GetSectionCacheKey(
        ReservationType type,
        IReadOnlySet<string> selectedCities,
        bool allCitiesSelected)
    {
        if (allCitiesSelected || selectedCities.Count == 0)
        {
            return $"{type}|{AllCitiesKey}";
        }

        return $"{type}|{string.Join(";", selectedCities.Order(StringComparer.OrdinalIgnoreCase))}";
    }

    private static string NormalizeCity(string? city)
    {
        return string.IsNullOrWhiteSpace(city) ? "Unknown City" : city.Trim();
    }

    private static DateOnly? GetInitialSelectedDate(TripScheduleDto schedule, IReadOnlyList<ScheduleItemDto> items)
    {
        var today = DateOnly.FromDateTime(DateTime.Now);
        if (today >= schedule.StartsOn && today <= schedule.EndsOn)
        {
            return today;
        }

        var now = DateTime.Now;
        var nextItem = items
            .Where(item => !IsPast(item, now))
            .OrderBy(item => GetTimelineSortValue(item, now))
            .FirstOrDefault();

        return nextItem?.Date ?? schedule.StartsOn;
    }

    private static ScheduleItemDto? GetFocusItem(IReadOnlyList<ScheduleItemDto> items)
    {
        var now = DateTime.Now;
        return items
            .Where(item => !IsPast(item, now))
            .OrderBy(item => GetTimelineSortValue(item, now))
            .ThenBy(item => item.StartsAt)
            .FirstOrDefault();
    }

    private static bool IsPast(ScheduleItemDto item, DateTime now)
    {
        return GetEndDateTime(item) < now;
    }

    private static DateTime GetTimelineSortValue(ScheduleItemDto item, DateTime now)
    {
        var start = GetStartDateTime(item);
        var end = GetEndDateTime(item);
        if (start <= now && end >= now)
        {
            return now;
        }

        return start;
    }

    private static DateTime GetStartDateTime(ScheduleItemDto item)
    {
        return item.Date.ToDateTime(item.StartsAt);
    }

    private static DateTime GetEndDateTime(ScheduleItemDto item)
    {
        var endDate = item.EndsOn ?? item.Date;
        var endTime = item.EndsAt ?? item.StartsAt;
        return endDate.ToDateTime(endTime);
    }

    protected override void OnLoadStateChanged()
    {
        OnPropertyChanged(nameof(ShowInitialLoading));
        OnPropertyChanged(nameof(ShowEmptyState));
    }

    private void SetTodayLoading(bool value)
    {
        if (_isTodayLoading == value)
        {
            return;
        }

        _isTodayLoading = value;
        OnPropertyChanged(nameof(ShowTodayLoading));
        OnPropertyChanged(nameof(ShowTodayContent));
        OnPropertyChanged(nameof(ShowDayReview));
    }

    private void OnScheduleCacheUpdated(object? sender, ScheduleCacheUpdatedEventArgs e)
    {
        _todayByDate.Clear();
        _today = null;
        ApplySchedule(e.Schedule);
        // Keep the newly saved item visible while Today is regenerated from the server.
        SetTodayLoading(false);
        RebuildSelectedDay();
        MarkLastUpdated(e.SavedAt);
        StatusMessage = "Itinerario actualizado.";
        _ = RefreshTodayAfterScheduleUpdateAsync();
    }

    private async Task RefreshTodayAfterScheduleUpdateAsync()
    {
        try
        {
            // Do not join a Today request that started before the itinerary mutation.
            // Its valid but older response would hide the last saved recommendation.
            await _todayStore.InvalidateAllAsync();
            var token = await _sessionService.GetTokenAsync();
            if (string.IsNullOrWhiteSpace(token) || !_selectedDate.HasValue)
            {
                return;
            }

            var selectedDate = _selectedDate.Value;
            var result = await _todayStore.RefreshResultAsync(
                token,
                selectedDate,
                null,
                CancellationToken.None);
            if (result.IsUnauthorized)
            {
                _sessionService.Clear();
                await Shell.Current.GoToAsync("//login");
                return;
            }

            if (result.Value is { } today && _selectedDate == selectedDate)
            {
                ApplyToday(today);
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
        {
            _logger.LogWarning(
                "Today refresh after schedule update failed ({ErrorType}); keeping the updated schedule visible.",
                ex.GetType().Name);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Unexpected Today refresh failure after schedule update; keeping the updated schedule visible.");
        }
    }

    private void NotifyFocusChanged()
    {
        OnPropertyChanged(nameof(HasFocusItem));
        OnPropertyChanged(nameof(FocusTitle));
        OnPropertyChanged(nameof(FocusSubtitle));
        OnPropertyChanged(nameof(FocusMeta));
    }

    private void NotifySelectedDayChanged()
    {
        OnPropertyChanged(nameof(SelectedCity));
        OnPropertyChanged(nameof(SelectedDateLabel));
        OnPropertyChanged(nameof(ShowImproveDay));
        OnPropertyChanged(nameof(ImproveDayLabel));
        OnPropertyChanged(nameof(IsSelectedDayLocked));
        OnPropertyChanged(nameof(CanEditSelectedDay));
        OnPropertyChanged(nameof(CanManageItinerary));
        OnPropertyChanged(nameof(AmbientGlyph));
        OnPropertyChanged(nameof(HasPreviewMessage));
        OnPropertyChanged(nameof(HasStayCard));
        OnPropertyChanged(nameof(HasScheduleItems));
        OnPropertyChanged(nameof(HasSelectedDayItems));
        OnPropertyChanged(nameof(ShowInitialLoading));
        OnPropertyChanged(nameof(ShowEmptyState));
        OnPropertyChanged(nameof(ShowTodayLoading));
        OnPropertyChanged(nameof(ShowTodayContent));
    }

    private static string FormatLongDate(DateOnly date)
    {
        var culture = CultureInfo.CurrentCulture;
        var dayName = culture.TextInfo.ToTitleCase(date.ToString("dddd", culture));
        return $"{dayName} · {date.Day} de {date.ToString("MMMM", culture)}";
    }

    private static string GetAmbientGlyph(string city)
    {
        if (city.Contains("tok", StringComparison.OrdinalIgnoreCase))
        {
            return "東京";
        }

        if (city.Contains("kyo", StringComparison.OrdinalIgnoreCase)
            || city.Contains("kio", StringComparison.OrdinalIgnoreCase))
        {
            return "京都";
        }

        if (city.Contains("osaka", StringComparison.OrdinalIgnoreCase))
        {
            return "大阪";
        }

        if (city.Contains("hiroshima", StringComparison.OrdinalIgnoreCase))
        {
            return "広島";
        }

        return "旅";
    }
}

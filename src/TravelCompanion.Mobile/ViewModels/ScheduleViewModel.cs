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
    private readonly ILogger<ScheduleViewModel> _logger;
    private readonly List<ScheduleItemDto> _allItems = [];
    private readonly List<RecommendationDto> _recommendations = [];
    private readonly Dictionary<string, ScheduleTypeSectionViewModel> _sectionCache = new(StringComparer.Ordinal);
    private ReservationType _selectedType = ReservationType.Event;
    private string _tripTitle = "Your Trip";
    private string? _tripDates;
    private DateOnly? _tripStartsOn;
    private DateOnly? _tripEndsOn;
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
    private GeoPointDto? _currentLocation;
    private bool _hasRequestedLocation;
    private readonly HashSet<Guid> _nearbyVisitPrompts = [];

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

    public bool HasScheduleItems => _allItems.Count > 0;
    public bool HasSelectedDayItems => TodaySections.Any(section => section.HasContent);
    public bool HasFocusItem => _focusItem is not null;
    public bool ShowInitialLoading => IsBusy && !HasScheduleItems;
    public bool ShowEmptyState => HasLoaded && !IsBusy && !HasSelectedDayItems && !HasStayCard;
    public bool HasPreviewMessage => !string.IsNullOrWhiteSpace(PreviewMessage);
    public bool HasStayCard => !string.IsNullOrWhiteSpace(StayTitle);
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
        ILogger<ScheduleViewModel> logger)
    {
        _sessionService = sessionService;
        _bootstrapStore = bootstrapStore;
        _todayStore = todayStore;
        _apiClient = apiClient;
        _locationService = locationService;
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
        ResetLoadState();
        _allItems.Clear();
        _recommendations.Clear();
        _sectionCache.Clear();
        ActiveDays = [];
        SelectedTimelineItems = [];
        TodaySections = [];
        TypeSections.Clear();
        _activeSection = null;
        TypeFilters.Clear();
        CityFilters.Clear();
        DayFilters.Clear();
        _selectedType = ReservationType.Event;
        _tripStartsOn = null;
        _tripEndsOn = null;
        _selectedDate = null;
        _selectedCity = "Tu viaje";
        PreviewMessage = null;
        StayTitle = null;
        _focusItem = null;
        _today = null;
        _currentLocation = null;
        _hasRequestedLocation = false;
        _nearbyVisitPrompts.Clear();
        TripTitle = "Your Trip";
        TripDates = null;
        SelectedItem = null;
        NotifySelectedDayChanged();
        NotifyFocusChanged();
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

            await LoadScheduleLocalFirstAsync(token, forceRefresh: true, ct);
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
    private async Task SelectDayAsync(ScheduleDayFilterViewModel? day)
    {
        if (day is null || _selectedDate == day.Date)
        {
            return;
        }

        var stopwatch = Stopwatch.StartNew();
        _selectedDate = day.Date;
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
            await LoadTodayForSelectedDateAsync(token, forceRefresh: false, cancellationToken: default);
        }
    }

    private async Task LoadScheduleLocalFirstAsync(
        string token,
        bool forceRefresh,
        CancellationToken cancellationToken = default)
    {
        if (forceRefresh)
        {
            _hasRequestedLocation = false;
        }

        var cached = await _bootstrapStore.GetCachedAsync(cancellationToken: cancellationToken);
        if (cached is not null)
        {
            ApplyBootstrapSchedule(cached.Value);
            await UpdateLocationAsync(cancellationToken);
            await LoadTodayForSelectedDateAsync(token, forceRefresh, cancellationToken);
            MarkLastUpdated(cached.SavedAt);

            if (!forceRefresh && _bootstrapStore.HasFreshSnapshot())
            {
                StatusMessage = null;
                return;
            }

            StatusMessage = forceRefresh
                ? "Actualizando itinerario..."
                : $"Mostrando itinerario guardado mientras la API responde. {OfflineCacheService.FormatSavedAt(cached.SavedAt)}";
        }

        try
        {
            var bootstrap = await _bootstrapStore.RefreshAsync(token, cancellationToken: cancellationToken);
            if (bootstrap is null)
            {
                _sessionService.Clear();
                await Shell.Current.GoToAsync("//login");
                return;
            }

            ApplyBootstrapSchedule(bootstrap);
            await UpdateLocationAsync(cancellationToken);
            await LoadTodayForSelectedDateAsync(token, forceRefresh: true, cancellationToken);
            MarkLastUpdated(DateTimeOffset.UtcNow);
            StatusMessage = null;
        }
        catch
        {
            if (cached is null)
            {
                throw;
            }

            StatusMessage = $"Render puede estar despertando. Mostrando itinerario guardado. {OfflineCacheService.FormatSavedAt(cached.SavedAt)}";
        }
    }

    private async Task LoadTodayForSelectedDateAsync(
        string token,
        bool forceRefresh,
        CancellationToken cancellationToken)
    {
        if (!_selectedDate.HasValue)
        {
            return;
        }

        var selectedDate = _selectedDate.Value;
        var cached = await _todayStore.GetCachedAsync(selectedDate, cancellationToken);
        if (cached is not null)
        {
            ApplyToday(cached.Value);
        }

        if (!forceRefresh && _todayStore.HasFreshSnapshot(selectedDate))
        {
            await PromptForNearbyVisitAsync(token, cancellationToken);
            return;
        }

        try
        {
            var today = await _todayStore.RefreshAsync(
                token,
                selectedDate,
                _currentLocation,
                cancellationToken);
            if (today is not null)
            {
                ApplyToday(today);
                await PromptForNearbyVisitAsync(token, cancellationToken);
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
        {
            if (cached is not null)
            {
                StatusMessage = $"Render puede estar despertando. Mostrando Today guardado. {OfflineCacheService.FormatSavedAt(cached.SavedAt)}";
            }
        }
    }

    private void ApplyToday(TodayDto today)
    {
        _today = today;
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
        TripTitle = $"{schedule.DestinationName} for {schedule.TravelerName}";
        TripDates = $"{schedule.StartsOn:MMM d} - {schedule.EndsOn:MMM d, yyyy}";
        _tripStartsOn = schedule.StartsOn;
        _tripEndsOn = schedule.EndsOn;
        _allItems.Clear();
        _allItems.AddRange(sourceItems);
        _focusItem = GetFocusItem(_allItems);
        _selectedDate = GetInitialSelectedDate(schedule, _allItems);
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
        TypeSections.Clear();
        _activeSection = null;
        TypeFilters.Clear();
        UpdateTypeFilters();
        CityFilters.Clear();
        CityFilters.Add(new CityFilterViewModel(AllCitiesKey, isSelected: true));
        DayFilters.Clear();
        _tripStartsOn = null;
        _tripEndsOn = null;
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
                _selectedDate == date));
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
            StayTitle = null;
            PreviewMessage = null;
            SelectedTimelineItems = [];
            TodaySections = [];
            ActiveDays = [];
            NotifySelectedDayChanged();
            return;
        }

        var selectedDate = _selectedDate.Value;
        _selectedCity = GetCityForDate(selectedDate, TripTitle);
        StayTitle = GetStayTitleForDate(selectedDate);
        PreviewMessage = null;

        var selectedItems = _allItems
            .Where(item => item.Type != ReservationType.Lodging)
            .Where(item => item.Date == selectedDate)
            .OrderBy(item => item.StartsAt)
            .ToList();

        var dayNumber = _tripStartsOn.HasValue
            ? selectedDate.DayNumber - _tripStartsOn.Value.DayNumber + 1
            : 1;
        TodaySections = _today is not null && _today.Date == selectedDate
            ? BuildTodaySections(_today, dayNumber)
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
            StatusMessage = "No pude registrar la accion. Probalo de nuevo cuando la API responda.";
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

    private static IReadOnlyList<ScheduleTodaySectionViewModel> BuildTodaySections(
        TodayDto today,
        int dayNumber)
    {
        return today.Sections
            .Select(section => new ScheduleTodaySectionViewModel(
                dayNumber,
                section.Title,
                section.Description,
                section.Recommendations
                    .Select(recommendation => new TodayLocationViewModel(recommendation))
                    .ToList(),
                section.Reservations
                    .OrderBy(reservation => reservation.StartsAt)
                    .Select(reservation => new TodayReservationViewModel(reservation))
                    .ToList()))
            .ToList();
    }

    private IReadOnlyList<ScheduleTodaySectionViewModel> BuildTodaySections(
        DateOnly selectedDate,
        int dayNumber,
        IReadOnlyList<ScheduleItemDto> selectedReservations)
    {
        var usedRecommendationIds = new HashSet<Guid>();
        return TodayPeriod.All
            .Select(period =>
            {
                var reservations = selectedReservations
                    .Where(item => period.Contains(item.StartsAt))
                    .OrderBy(item => item.StartsAt)
                    .Select(item => new TodayReservationViewModel(item))
                    .ToList();
                var locations = SelectRecommendationsForPeriod(period, selectedDate, usedRecommendationIds)
                    .Select(recommendation => new TodayLocationViewModel(
                        recommendation,
                        CalculateDistanceKm(_currentLocation, recommendation)))
                    .ToList();

                return new ScheduleTodaySectionViewModel(
                    dayNumber,
                    period.Label,
                    CreateSectionDescription(period, reservations, locations),
                    locations,
                    reservations);
            })
            .ToList();
    }

    private IReadOnlyList<RecommendationDto> SelectRecommendationsForPeriod(
        TodayPeriod period,
        DateOnly selectedDate,
        ISet<Guid> usedRecommendationIds)
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
            .ThenBy(candidate => CalculateDistanceKm(_currentLocation, candidate.Recommendation) ?? decimal.MaxValue)
            .ThenBy(candidate => candidate.Recommendation.Title)
            .Take(2)
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

    private static decimal? CalculateDistanceKm(GeoPointDto? currentLocation, RecommendationDto recommendation)
    {
        if (currentLocation is null)
        {
            return recommendation.DistanceKm;
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

    private void RebuildDefaultTypeSections()
    {
        _sectionCache.Clear();
        TypeSections.Clear();
        foreach (var type in new[] { ReservationType.Event, ReservationType.Flight, ReservationType.Lodging })
        {
            var section = CreateScheduleSection(type, new HashSet<string>(StringComparer.OrdinalIgnoreCase), allCitiesSelected: true);
            _sectionCache[section.CacheKey] = section;
            TypeSections.Add(section);
        }
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

    private static ReservationType GetInitialScheduleType(IReadOnlyList<ScheduleItemDto> items)
    {
        var now = DateTime.Now;
        return items
            .Where(item => !IsPast(item, now))
            .OrderBy(item => GetTimelineSortValue(item, now))
            .ThenBy(item => item.StartsAt)
            .Select(item => item.Type)
            .FirstOrDefault(ReservationType.Event);
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

    private void OnScheduleCacheUpdated(object? sender, ScheduleCacheUpdatedEventArgs e)
    {
        _today = null;
        ApplySchedule(e.Schedule);
        MarkLastUpdated(e.SavedAt);
        StatusMessage = "Itinerario actualizado.";
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
        OnPropertyChanged(nameof(AmbientGlyph));
        OnPropertyChanged(nameof(HasPreviewMessage));
        OnPropertyChanged(nameof(HasStayCard));
        OnPropertyChanged(nameof(HasScheduleItems));
        OnPropertyChanged(nameof(HasSelectedDayItems));
        OnPropertyChanged(nameof(ShowInitialLoading));
        OnPropertyChanged(nameof(ShowEmptyState));
    }

    private static string FormatLongDate(DateOnly date)
    {
        var culture = CultureInfo.CurrentCulture;
        var dayName = culture.TextInfo.ToTitleCase(date.ToString("dddd", culture));
        return $"{dayName} · {date.Day} de {date.ToString("MMMM", culture)}";
    }

    private static string FormatShortDate(DateOnly date)
    {
        return $"{date.Day} de {date.ToString("MMMM", CultureInfo.CurrentCulture)}";
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

using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.Input;
using TravelCompanion.Mobile.Services;
using TravelCompanion.Shared;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Mobile.ViewModels;

public sealed partial class ItineraryItemEditorViewModel(
    TravelCompanionApiClient apiClient,
    AuthSessionService sessionService,
    MobileBootstrapStore bootstrapStore,
    MobileTodayStore todayStore,
    MobileSyncStateStore syncStateStore,
    MapViewModel mapViewModel,
    BuilderTripStore builderTripStore,
    ILocalReservationNotifications notifications) : ViewModelBase
{
    private static readonly IReadOnlyList<ItineraryFlexibilityOption> FlexibilityChoices =
    [
        new(ItineraryFlexibility.Flexible, LocalizationResourceManager.Instance.GetString("ItemFlexibilityFlexible")),
        new(ItineraryFlexibility.FixedByTraveler, LocalizationResourceManager.Instance.GetString("ItemFlexibilityFixed")),
        new(ItineraryFlexibility.ConfirmedReservation, LocalizationResourceManager.Instance.GetString("ItemFlexibilityConfirmed"))
    ];
    private RecommendationDto? _recommendation;
    private DateTime _date = DateTime.Today;
    private DateTime _minimumDate = DateTime.Today;
    private DateTime _maximumDate = DateTime.Today.AddYears(2);
    private string _selectedPeriod = "Tarde";
    private bool _useExactTime;
    private bool _reminderEnabled;
    public bool ReminderEnabled { get => _reminderEnabled; set { if (SetProperty(ref _reminderEnabled, value)) RefreshReminderPreview(); } }
    private string? _tripTimeZone;
    private bool _editingTimeZoneValid = true;
    private string _reminderPreview = string.Empty;
    public string ReminderPreview { get => _reminderPreview; private set => SetProperty(ref _reminderPreview, value); }
    private string ReservationTimeZone => TimeZoneInfo.Local.Id;
    public string ReservationTimeZoneLabel => string.Format(CultureInfo.CurrentUICulture,
        LocalizationResourceManager.Instance["ItemTimeZone"],
        ReservationTimeZone);

    public void RefreshReminderPreview()
    {
        OnPropertyChanged(nameof(ReservationTimeZoneLabel));
        if (!UseExactTime || !ReminderEnabled) { ReminderPreview = string.Empty; return; }
        var next = ReservationReminderPolicy.Create(Guid.Empty, _existingItem?.Type ?? ReservationType.Event,
            DateOnly.FromDateTime(Date), TimeOnly.FromTimeSpan(Time), ReservationTimeZone ?? "", TitleText,
            DateTimeOffset.UtcNow, false).FirstOrDefault();
        ReminderPreview = next is null ? LocalizationResourceManager.Instance["ItemNoUpcomingReminder"]
            : string.Format(CultureInfo.CurrentUICulture, LocalizationResourceManager.Instance["ItemNextReminder"],
                next.NotifyAtUtc.ToLocalTime().ToString("f", CultureInfo.CurrentUICulture));
    }

    [RelayCommand]
    private Task TestReminderAsync() => LoadAsync(async () =>
    {
        if (!await notifications.RequestPermissionAsync())
        {
            ErrorMessage = LocalizationResourceManager.Instance["ItemReminderPermission"];
            return;
        }
        await notifications.ShowTestAsync(LocalizationResourceManager.Instance["ItemTestReminder"],
            LocalizationResourceManager.Instance["ItemTestReminderBody"]);
        StatusMessage = LocalizationResourceManager.Instance["ItemTestReminderSent"];
    });
    private TimeSpan _time = new(15, 0, 0);
    private string _durationMinutes = string.Empty;
    private ItineraryFlexibilityOption _selectedFlexibility = FlexibilityChoices[0];
    private string _notes = string.Empty;
    private string _titleText = string.Empty;
    private string _locationName = string.Empty;
    private string _address = string.Empty;
    private readonly ItineraryEditorContext _editorContext = new();
    private int _revision { get => _editorContext.Revision; set => _editorContext.Revision = value; }
    private ScheduleItemDto? _existingItem;
    private IReadOnlyList<BuilderTripSetupSegmentDto> _segments = [];
    private CancellationTokenSource? _placeSearch;
    private string _placeSessionToken = Guid.NewGuid().ToString();
    private string? _selectedGooglePlaceId;
    private Guid? _selectedCatalogId;
    private string? _selectedPlaceCity;
    private bool _placeWasChanged;
    private readonly Dictionary<string, string> _suggestionCities = [];
    private IReadOnlyList<string> SearchCities
    {
        get
        {
            var cities = TripCityScope.ForDate(_segments, DateOnly.FromDateTime(Date));
            return cities.Count > 0 ? cities : [CurrentCity.Trim()];
        }
    }
    private string SearchCityScope => string.Join("|", SearchCities);
    private decimal? _selectedLatitude;
    private decimal? _selectedLongitude;
    private string _currentCity = string.Empty;
    private string _fallbackCity = string.Empty;
    private string? _placeSearchMessage;
    private bool _applyingPlaceSelection;
    private readonly Dictionary<string, IReadOnlyList<PlaceSuggestionDto>> _placeSuggestionCache = new(StringComparer.Ordinal);

    public IReadOnlyList<string> Periods { get; } = ["Mañana", "Medio día", "Tarde", "Noche"];
    public IReadOnlyList<ItineraryFlexibilityOption> FlexibilityOptions => FlexibilityChoices;
    public ObservableCollection<PlaceSuggestionDto> PlaceSuggestions { get; } = [];
    public string HeaderTitle => _recommendation?.Title ?? "Nuevo plan";
    public string Subtitle => _recommendation?.Neighborhood ?? "Agrega una idea personal";
    public string TitleText { get => _titleText; set => SetProperty(ref _titleText, value); }
    public string LocationName
    {
        get => _locationName;
        set
        {
            if (SetProperty(ref _locationName, value) && !_applyingPlaceSelection)
            {
                ClearSelectedPlace();
                CancelPlaceSearch();
            }
        }
    }
    public string Address
    {
        get => _address;
        set
        {
            if (SetProperty(ref _address, value) && !_applyingPlaceSelection)
            {
                ClearSelectedPlace();
            }
        }
    }
    public DateTime Date
    {
        get => _date;
        set
        {
            if (SetProperty(ref _date, value))
            {
                CancelPlaceSearch();
                RefreshCurrentCity();
                RefreshReminderPreview();
            }
        }
    }
    public DateTime MinimumDate { get => _minimumDate; private set => SetProperty(ref _minimumDate, value); }
    public DateTime MaximumDate { get => _maximumDate; private set => SetProperty(ref _maximumDate, value); }
    public string SelectedPeriod { get => _selectedPeriod; set => SetProperty(ref _selectedPeriod, value); }
    public bool UseExactTime { get => _useExactTime; set { if (SetProperty(ref _useExactTime, value)) RefreshReminderPreview(); } }
    public TimeSpan Time { get => _time; set { if (SetProperty(ref _time, value)) RefreshReminderPreview(); } }
    public string DurationMinutes { get => _durationMinutes; set => SetProperty(ref _durationMinutes, value); }
    public ItineraryFlexibilityOption SelectedFlexibility { get => _selectedFlexibility; set => SetProperty(ref _selectedFlexibility, value); }
    public string Notes { get => _notes; set => SetProperty(ref _notes, value); }
    public string CurrentCity { get => _currentCity; private set => SetProperty(ref _currentCity, value); }
    public string? PlaceSearchMessage
    {
        get => _placeSearchMessage;
        private set
        {
            if (SetProperty(ref _placeSearchMessage, value))
            {
                OnPropertyChanged(nameof(HasPlaceSearchMessage));
            }
        }
    }
    public bool HasPlaceSearchMessage => !string.IsNullOrWhiteSpace(PlaceSearchMessage);
    public bool CanSearchPlaces => _recommendation is null;

    public async Task InitializeAsync(
        RecommendationDto recommendation,
        DateOnly? initialDate = null,
        TimeOnly? suggestedStartTime = null)
    {
        CancelPlaceSearch();
        _existingItem = null;
        _editingTimeZoneValid = true;
        ReminderEnabled = false;
        _recommendation = recommendation;
        _selectedCatalogId = recommendation.Id;
        _selectedPlaceCity = null;
        _placeWasChanged = false;
        _fallbackCity = recommendation.Neighborhood.Split(',')[0].Trim();
        _applyingPlaceSelection = true;
        TitleText = recommendation.Title;
        DurationMinutes = Math.Max(15, recommendation.SuggestedDurationMinutes).ToString(CultureInfo.InvariantCulture);
        SelectedFlexibility = FlexibilityChoices[0];
        LocationName = recommendation.Title;
        Address = recommendation.Neighborhood;
        _applyingPlaceSelection = false;
        OnPropertyChanged(nameof(HeaderTitle));
        OnPropertyChanged(nameof(Subtitle));
        OnPropertyChanged(nameof(CanSearchPlaces));
        var setup = await LoadSetupAsync();
        if (setup is not null)
        {
            var fallbackDate = setup.ArrivalDate ?? DateOnly.FromDateTime(DateTime.Today);
            var selectedDate = initialDate.HasValue
                && _segments.Any(segment => initialDate.Value >= segment.StartsOn && initialDate.Value <= segment.EndsOn)
                    ? initialDate.Value
                    : fallbackDate;
            Date = selectedDate.ToDateTime(TimeOnly.MinValue);
        }

        if (suggestedStartTime.HasValue)
        {
            Time = suggestedStartTime.Value.ToTimeSpan();
            SelectedPeriod = suggestedStartTime.Value.Hour switch
            {
                < 12 => "Mañana",
                < 15 => "Medio día",
                < 19 => "Tarde",
                _ => "Noche"
            };
        }
    }

    public async Task InitializeManualAsync(DateOnly date, string periodKey)
    {
        CancelPlaceSearch();
        _existingItem = null;
        _editingTimeZoneValid = true;
        ReminderEnabled = false;
        _recommendation = null;
        _fallbackCity = string.Empty;
        _applyingPlaceSelection = true;
        TitleText = string.Empty;
        DurationMinutes = string.Empty;
        SelectedFlexibility = FlexibilityChoices[0];
        LocationName = string.Empty;
        Address = string.Empty;
        _applyingPlaceSelection = false;
        ClearSelectedPlace();
        Date = date.ToDateTime(TimeOnly.MinValue);
        SelectedPeriod = periodKey switch { "morning" => "Mañana", "midday" => "Medio día", "night" => "Noche", _ => "Tarde" };
        OnPropertyChanged(nameof(HeaderTitle));
        OnPropertyChanged(nameof(Subtitle));
        OnPropertyChanged(nameof(CanSearchPlaces));
        await LoadSetupAsync();
    }

    public async Task InitializeExistingAsync(ScheduleItemDto item)
    {
        CancelPlaceSearch();
        _existingItem = item;
        _selectedCatalogId = item.RecommendationId;
        _selectedPlaceCity = item.City;
        _placeWasChanged = false;
        _editingTimeZoneValid = true;
        _recommendation = null;
        _fallbackCity = item.City;
        _applyingPlaceSelection = true;
        TitleText = item.Title;
        LocationName = item.LocationName;
        Address = item.Address;
        _applyingPlaceSelection = false;
        _selectedGooglePlaceId = item.ItemSource == ItineraryItemSource.GooglePlace
            ? item.ProviderPlaceId
            : null;
        // Keep coordinate-only locations too (catalog suggestions need not have a Google ID).
        _selectedLatitude = item.Latitude;
        _selectedLongitude = item.Longitude;
        Notes = item.Notes;
        Date = item.Date.ToDateTime(TimeOnly.MinValue);
        SelectedPeriod = item.StartsAt.Hour switch { < 12 => "Mañana", < 15 => "Medio día", < 19 => "Tarde", _ => "Noche" };
        UseExactTime = item.HasExactTime;
        ReminderEnabled = ReservationReminderPolicy.IsEligible(item.Type, item.TimePrecision, item.PlanningKind, item.Flexibility, item.ReminderEnabled);
        Time = item.StartsAt.ToTimeSpan();
        DurationMinutes = (item.DurationMinutes ?? (item.EndsAt.HasValue
            ? Math.Max(15, (int)(item.EndsAt.Value.ToTimeSpan() - item.StartsAt.ToTimeSpan()).TotalMinutes) : (int?)null))
            ?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
        SelectedFlexibility = FlexibilityChoices.First(option => option.Value == item.Flexibility);
        OnPropertyChanged(nameof(HeaderTitle));
        OnPropertyChanged(nameof(Subtitle));
        OnPropertyChanged(nameof(CanSearchPlaces));
        await LoadSetupAsync();
        if (item.HasExactTime)
        {
            var local = ReservationLocalTime.ConvertStart(item.Date, item.StartsAt,
                item.TimeZoneId ?? _tripTimeZone, TimeZoneInfo.Local);
            if (local.HasValue)
            {
                Date = local.Value.Date;
                Time = local.Value.TimeOfDay;
                SelectedPeriod = local.Value.Hour switch { < 12 => "Mañana", < 15 => "Medio día", < 19 => "Tarde", _ => "Noche" };
            }
            else
            {
                _editingTimeZoneValid = false;
                ErrorMessage = LocalizationResourceManager.Instance["ItemUnknownTimeZone"];
            }
        }
    }

    public async Task SearchPlaceSuggestionsAsync()
    {
        if (_applyingPlaceSelection) return;
        CancelPlaceSearch();
        PlaceSearchMessage = null;
        if (!CanSearchPlaces) return;

        var query = NormalizeSearchText(LocationName);
        var cities = SearchCities;
        var city = SearchCityScope;
        if (query.Length < 2) return;
        if (string.IsNullOrWhiteSpace(city))
        {
            PlaceSearchMessage = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "es"
                ? "Aún no se cargó la ciudad del itinerario. Si el problema continúa, vuelve a abrir el editor."
                : "The itinerary city has not loaded yet. If this continues, reopen the editor.";
            return;
        }

        var operation = new CancellationTokenSource();
        _placeSearch = operation;
        var sessionToken = _placeSessionToken;
        try
        {
            var cached = await bootstrapStore.GetCachedAsync(cancellationToken: operation.Token);
            if (operation.IsCancellationRequested
                || !ReferenceEquals(_placeSearch, operation)
                || !string.Equals(NormalizeSearchText(LocationName), query, StringComparison.Ordinal)
                || !string.Equals(SearchCityScope, city, StringComparison.Ordinal)) return;

            var score = CatalogSearch.CreateFieldScorer(query);
            var matches = cached?.Value.Recommendations
                .Select(item => new
                {
                    Item = item,
                    Score = score(item.Title,
                        $"{item.Neighborhood} {item.Category} {item.RefinedType} {string.Join(' ', item.Tags)}",
                        item.Description)
                })
                .Where(candidate => candidate.Score > 0 && cities.Any(candidateCity => TextContains(candidate.Item.Neighborhood, candidateCity)))
                .OrderByDescending(candidate => candidate.Score)
                .ThenBy(candidate => candidate.Item.Title)
                .Select(candidate => candidate.Item)
                .Select(item => new PlaceSuggestionDto(
                    $"yuku:{item.Id:N}", item.Title, item.Neighborhood, item.Provider,
                    item.Id, item.Latitude, item.Longitude))
                .ToList() ?? [];
            matches = cities.SelectMany(candidateCity => matches
                .Where(match => TextContains(match.Address, candidateCity))
                .Take(cities.Count > 1 ? 4 : 8)).DistinctBy(match => match.PlaceId).ToList();
            foreach (var match in matches) PlaceSuggestions.Add(match);
            if (matches.Count > 0 && cities.All(candidateCity => matches.Any(match => TextContains(match.Address, candidateCity))))
            {
                return;
            }

            if (query.Length < 3 || !sessionService.CanSearchGooglePlaces)
            {
                PlaceSearchMessage = "No encontramos coincidencias. Puedes completar el lugar manualmente.";
                return;
            }

            await Task.Delay(350, operation.Token);
            var cacheKey = $"{sessionService.ContextVersion}:{CultureInfo.CurrentUICulture.Name}:place:{city.ToUpperInvariant()}:{query.ToUpperInvariant()}";
            if (!_placeSuggestionCache.TryGetValue(cacheKey, out var results))
            {
                var token = await sessionService.GetTokenAsync();
                if (string.IsNullOrWhiteSpace(token)) return;
                var combined = new List<PlaceSuggestionDto>();
                foreach (var candidateCity in cities)
                {
                    var suggestions = await apiClient.AutocompletePlacesAsync(token, new PlaceAutocompleteRequest(
                        query, candidateCity, sessionToken, CultureInfo.CurrentUICulture.Name,
                        PlaceAutocompleteMode.Place), operation.Token);
                    foreach (var suggestion in suggestions) _suggestionCities[suggestion.PlaceId] = candidateCity;
                    combined.AddRange(suggestions.Take(cities.Count > 1 ? 3 : 5));
                }
                results = combined.DistinctBy(item => item.PlaceId).ToList();
                if (_placeSuggestionCache.Count >= 20) _placeSuggestionCache.Remove(_placeSuggestionCache.Keys.First());
                _placeSuggestionCache[cacheKey] = results;
            }
            if (operation.IsCancellationRequested
                || !ReferenceEquals(_placeSearch, operation)
                || !string.Equals(NormalizeSearchText(LocationName), query, StringComparison.Ordinal)
                || !string.Equals(SearchCityScope, city, StringComparison.Ordinal)
                || !string.Equals(_placeSessionToken, sessionToken, StringComparison.Ordinal)) return;

            foreach (var result in results.Take(cities.Count > 1 ? 6 : 5)) PlaceSuggestions.Add(result);
            PlaceSearchMessage = results.Count == 0
                ? "No encontramos coincidencias. Puedes escribir el lugar y la dirección manualmente."
                : null;
        }
        catch (OperationCanceledException) { }
        catch (Exception)
        {
            if (!operation.IsCancellationRequested)
            {
                PlaceSearchMessage = "La búsqueda no está disponible. Puedes completar el lugar manualmente.";
            }
        }
        finally
        {
            if (ReferenceEquals(_placeSearch, operation)) _placeSearch = null;
            operation.Dispose();
        }
    }

    public async Task SelectPlaceAsync(PlaceSuggestionDto suggestion)
    {
        CancelPlaceSearch(clearSuggestions: false);
        void CommitSelection()
        {
            _selectedPlaceCity = _suggestionCities.GetValueOrDefault(suggestion.PlaceId)
                ?? SearchCities.FirstOrDefault(city => TextContains(suggestion.Address, city)) ?? CurrentCity;
            _placeWasChanged = true;
            _recommendation = null;
            _selectedCatalogId = suggestion.RecommendationId;
        }
        if (suggestion.Provider.Equals("YUKU", StringComparison.OrdinalIgnoreCase)
            || suggestion.RecommendationId.HasValue)
        {
            CommitSelection();
            _applyingPlaceSelection = true;
            LocationName = suggestion.Name;
            Address = suggestion.Address;
            _applyingPlaceSelection = false;
            _selectedGooglePlaceId = null;
            _selectedLatitude = suggestion.Latitude;
            _selectedLongitude = suggestion.Longitude;
            if (!_editingTimeZoneValid)
        {
            ErrorMessage = LocalizationResourceManager.Instance["ItemUnknownTimeZone"];
            return;
        }
        if (string.IsNullOrWhiteSpace(TitleText)) TitleText = suggestion.Name;
            PlaceSuggestions.Clear();
            PlaceSearchMessage = null;
            return;
        }
        var query = NormalizeSearchText(LocationName);
        var city = SearchCityScope;
        var sessionToken = _placeSessionToken;
        var operation = new CancellationTokenSource();
        _placeSearch = operation;
        try
        {
            var token = await sessionService.GetTokenAsync();
            if (string.IsNullOrWhiteSpace(token)) return;
            var place = await apiClient.GetPlaceDetailsAsync(token, new PlaceDetailsRequest(
                suggestion.PlaceId,
                sessionToken,
                CultureInfo.CurrentUICulture.Name), operation.Token);
            if (place is null
                || operation.IsCancellationRequested
                || !ReferenceEquals(_placeSearch, operation)
                || !string.Equals(NormalizeSearchText(LocationName), query, StringComparison.Ordinal)
                || !string.Equals(SearchCityScope, city, StringComparison.Ordinal))
            {
                if (place is null) PlaceSearchMessage = "No pudimos cargar ese lugar. Puedes completarlo manualmente.";
                return;
            }

            CommitSelection();
            _applyingPlaceSelection = true;
            LocationName = place.Title;
            Address = place.Neighborhood;
            _applyingPlaceSelection = false;
            _selectedGooglePlaceId = place.ProviderPlaceId;
            _selectedLatitude = place.Latitude;
            _selectedLongitude = place.Longitude;
            if (string.IsNullOrWhiteSpace(TitleText)) TitleText = place.Title;
            PlaceSuggestions.Clear();
            PlaceSearchMessage = null;
        }
        catch (OperationCanceledException) { }
        catch (Exception)
        {
            if (!operation.IsCancellationRequested)
            {
                PlaceSearchMessage = "No pudimos cargar ese lugar. Puedes completarlo manualmente.";
            }
        }
        finally
        {
            _placeSessionToken = Guid.NewGuid().ToString();
            if (ReferenceEquals(_placeSearch, operation)) _placeSearch = null;
            operation.Dispose();
        }
    }

    public void CancelPlaceSearches() => CancelPlaceSearch();

    [RelayCommand]
    private Task SaveAsync() => LoadAsync(async ct =>
    {
        if (string.IsNullOrWhiteSpace(TitleText))
        {
            ErrorMessage = "Escribe un nombre para el plan.";
            return;
        }
        var token = await sessionService.GetTokenAsync();
        if (string.IsNullOrWhiteSpace(token)) return;
        if (!_editorContext.IsCurrent(sessionService.ContextVersion))
        {
            ErrorMessage = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "es"
                ? "Vuelve a abrir el editor para cargar el itinerario actualizado. Tus datos siguen en el formulario."
                : "Reopen the editor to load the current itinerary. Your entries are still in the form.";
            return;
        }
        var periodKey = SelectedPeriod switch
        {
            "Mañana" => "morning",
            "Medio día" => "midday",
            "Noche" => "night",
            _ => "afternoon"
        };
        Guid? recommendationId = _selectedCatalogId ?? (_recommendation is not null && _recommendation.Id != Guid.Empty
            ? _recommendation.Id
            : _placeWasChanged ? null : _existingItem?.RecommendationId);
        if (!ItineraryDurationInput.TryParse(DurationMinutes, out var duration))
        {
            ErrorMessage = "La duración debe estar entre 15 y 1440 minutos.";
            return;
        }
        var startsAt = UseExactTime ? TimeOnly.FromTimeSpan(Time) : (TimeOnly?)null;
        var mutation = new ItineraryItemMutationRequest(
            recommendationId,
            recommendationId.HasValue ? null : _selectedGooglePlaceId, TitleText.Trim(), DateOnly.FromDateTime(Date), periodKey,
            UseExactTime, startsAt, duration.HasValue ? startsAt?.AddMinutes(duration.Value) : null,
            _selectedPlaceCity ?? (recommendationId.HasValue ? _recommendation?.Neighborhood.Split(',')[0] ?? CurrentCity : CurrentCity),
            LocationName, Address,
            Notes, _recommendation?.Latitude ?? _selectedLatitude, _recommendation?.Longitude ?? _selectedLongitude,
            _revision, Guid.NewGuid().ToString("N"), Flexibility: SelectedFlexibility.Value, DurationMinutes: duration,
            ReminderEnabled: UseExactTime && ReminderEnabled, TimeZoneId: UseExactTime ? ReservationTimeZone : null);
        var result = await SaveMutationAsync(mutation);
        if (!_editorContext.IsCurrent(sessionService.ContextVersion)) return;
        if (result?.HasOverlap == true)
        {
            var confirmed = await Shell.Current.DisplayAlertAsync(
                "Ya hay un plan en este momento",
                "¿Quieres agregar este plan igualmente? Los dos quedarán visibles en el mismo bloque.",
                "Agregar igualmente",
                "Cancelar");
            if (!confirmed)
            {
                ErrorMessage = null;
                return;
            }

            if (!_editorContext.IsCurrent(sessionService.ContextVersion)) return;
            result = await SaveMutationAsync(mutation with { ConfirmOverlap = true });
            if (!_editorContext.IsCurrent(sessionService.ContextVersion)) return;
        }

        if (result is null || !result.Success)
        {
            if (result is not null && result.Revision != _revision)
            {
                var currentSchedule = await apiClient.GetScheduleAsync(token, ct);
                if (currentSchedule is not null)
                {
                    await bootstrapStore.ReplaceScheduleAsync(currentSchedule, ct);
                }
                _revision = result.Revision;
                await builderTripStore.UpdateRevisionAsync(result.Revision, ct);
            }
            ErrorMessage = result?.Message ?? "No se pudo guardar. Comprueba tu conexión.";
            return;
        }
        _revision = result.Revision;
        await builderTripStore.UpdateRevisionAsync(result.Revision, ct);
        await todayStore.InvalidateAllAsync();
        if (result.Item is not null) await bootstrapStore.UpsertScheduleItemAsync(result.Item, result.Revision, ct);
        await syncStateStore.AcknowledgeItineraryVersionAsync(result.Revision, ct);
        mapViewModel.ResetSelection();
        await Shell.Current.Navigation.PopToRootAsync(animated: false);
        await Shell.Current.GoToAsync("//main/schedule");

        Task<ItineraryItemMutationResponse?> SaveMutationAsync(ItineraryItemMutationRequest request) =>
            _existingItem is null
                ? apiClient.CreateItineraryItemAsync(token, request, ct)
                : apiClient.UpdateItineraryItemAsync(token, _existingItem.Id, request, ct);
    });

    [RelayCommand]
    private async Task CancelAsync()
    {
        CancelPlaceSearch();
        await Shell.Current.Navigation.PopToRootAsync(animated: false);
    }

    private async Task<BuilderTripSetupDto?> LoadSetupAsync()
    {
        var version = sessionService.ContextVersion;
        var tripId = sessionService.CurrentTripId;
        BuilderTripSetupDto? setup;
        try
        {
            // An offline/cached setup may predate changes from Today or Assistant.
            setup = await _editorContext.LoadAsync(version, tripId, async () =>
            {
                var token = await sessionService.GetTokenAsync();
                return string.IsNullOrWhiteSpace(token) ? null : await apiClient.GetBuilderTripSetupAsync(token);
            }, () => sessionService.ContextVersion);
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or OperationCanceledException)
        {
            ErrorMessage = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "es"
                ? "No se pudo actualizar el itinerario. Comprueba tu conexión y vuelve a abrir el editor."
                : "Could not refresh the itinerary. Check your connection and reopen the editor.";
            return null;
        }
        if (setup is null)
        {
            ErrorMessage = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "es"
                ? "No se pudo cargar el itinerario. Vuelve a abrir el editor para reintentar."
                : "Could not load the itinerary. Reopen the editor to retry.";
            return null;
        }
        _tripTimeZone = setup.TimeZoneId;
        RefreshReminderPreview();
        _segments = setup?.Segments ?? [];
        if (setup?.ArrivalDate is { } arrivalDate)
        {
            MinimumDate = arrivalDate.ToDateTime(TimeOnly.MinValue);
            MaximumDate = (setup.DepartureDate ?? arrivalDate).ToDateTime(TimeOnly.MinValue);
        }
        RefreshCurrentCity();
        if (CanSearchPlaces && string.IsNullOrWhiteSpace(_selectedGooglePlaceId)
            && !_selectedLatitude.HasValue && NormalizeSearchText(LocationName).Length >= 2)
            await SearchPlaceSuggestionsAsync();
        return setup;
    }

    private void RefreshCurrentCity()
    {
        var date = DateOnly.FromDateTime(Date);
        var segment = _segments.FirstOrDefault(item => date >= item.StartsOn && date <= item.EndsOn);
        CurrentCity = segment?.City?.Trim() ?? _fallbackCity;
    }

    private void CancelPlaceSearch(bool clearSuggestions = true)
    {
        _placeSearch?.Cancel();
        _placeSearch = null;
        if (clearSuggestions) PlaceSuggestions.Clear();
        PlaceSearchMessage = null;
    }

    private void ClearSelectedPlace()
    {
        _selectedCatalogId = null;
        _recommendation = null;
        _selectedPlaceCity = null;
        _placeWasChanged = true;
        _selectedGooglePlaceId = null;
        _selectedLatitude = null;
        _selectedLongitude = null;
    }

    private static string NormalizeSearchText(string value) =>
        string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static bool TextContains(string value, string query) =>
        CultureInfo.InvariantCulture.CompareInfo.IndexOf(
            value, query, CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) >= 0;
}

public sealed record ItineraryFlexibilityOption(ItineraryFlexibility Value, string Label);

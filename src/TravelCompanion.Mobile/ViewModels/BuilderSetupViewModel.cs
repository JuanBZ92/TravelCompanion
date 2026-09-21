using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TravelCompanion.Mobile.Pages;
using TravelCompanion.Mobile.Services;
using TravelCompanion.Shared;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Mobile.ViewModels;

public sealed partial class BuilderSetupViewModel(
    TravelCompanionApiClient apiClient,
    AuthSessionService sessionService,
    PendingItineraryActionStore pendingStore,
    MobileBootstrapStore bootstrapStore,
    MobileTodayStore todayStore,
    MobileSyncStateStore syncStateStore,
    BuilderTripStore builderTripStore) : ViewModelBase
{
    private DateTime _arrivalDate = DateTime.Today;
    private DateTime _departureDate = DateTime.Today.AddDays(6);
    private int _revision;
    private Guid? _tripId;
    private readonly Dictionary<string, IReadOnlyList<PlaceSuggestionDto>> _hotelSearchCache = new(StringComparer.Ordinal);

    public ObservableCollection<BuilderSegmentViewModel> Segments { get; } = [];
    public ObservableCollection<string> SuggestedCities { get; } = [];
    public bool HasSuggestedCities => SuggestedCities.Count > 0;
    public bool IsEditing => _tripId.HasValue;
    public string HeaderEyebrow => IsEditing ? "EDITAR ITINERARIO · JAPÓN" : "CREAR ITINERARIO · JAPÓN";
    public string HeaderTitle => IsEditing ? "Editar itinerario" : "Primero, las bases";
    public string HeaderDescription => IsEditing
        ? "Ajusta fechas, ciudades y hoteles. Tus planes se conservan en sus días actuales."
        : "Define cuándo y en qué ciudades estarás. El itinerario comienza vacío.";
    public string PrimaryButtonText => IsEditing ? "Guardar cambios" : "Crear itinerario";
    public DateTime ArrivalDate
    {
        get => _arrivalDate;
        set
        {
            var normalized = value.Date;
            if (!SetProperty(ref _arrivalDate, normalized))
            {
                return;
            }

            if (_departureDate < normalized)
            {
                _departureDate = normalized;
                OnPropertyChanged(nameof(DepartureDate));
            }

            AlignOuterSegmentDates();
        }
    }

    public DateTime DepartureDate
    {
        get => _departureDate;
        set
        {
            var normalized = value.Date < ArrivalDate.Date ? ArrivalDate.Date : value.Date;
            if (SetProperty(ref _departureDate, normalized))
            {
                AlignOuterSegmentDates();
            }
        }
    }

    [RelayCommand]
    private Task LoadSetupAsync() => LoadAsync(async ct =>
    {
        var preserveDraft = Segments.Count > 0;
        var token = await sessionService.GetTokenAsync();
        if (string.IsNullOrWhiteSpace(token))
        {
            ErrorMessage = "Tu sesión venció. Vuelve a ingresar con tu PIN.";
            return;
        }

        var setup = await builderTripStore.GetAsync(token, cancellationToken: ct);
        await LoadSuggestedCitiesAsync(token, ct);
        if (setup is null)
        {
            if (Segments.Count == 0)
            {
                AddDefaultSegment();
            }

            ErrorMessage = "No pudimos cargar la configuración del viaje. Reintenta en unos segundos.";
            return;
        }
        sessionService.ApplyTrialAccess(setup.TrialAccess);
        SetTripId(setup.TripId);
        _revision = setup.Revision;
        if (preserveDraft) return;
        Segments.Clear();
        ArrivalDate = setup.ArrivalDate?.ToDateTime(TimeOnly.MinValue) ?? DateTime.Today;
        DepartureDate = setup.DepartureDate?.ToDateTime(TimeOnly.MinValue) ?? DateTime.Today.AddDays(6);
        foreach (var segment in setup.Segments)
        {
            Segments.Add(BuilderSegmentViewModel.FromDto(segment));
        }
        if (Segments.Count == 0) AddDefaultSegment();
    });

    private async Task LoadSuggestedCitiesAsync(string token, CancellationToken cancellationToken)
    {
        var cached = await bootstrapStore.GetCachedAsync(cancellationToken: cancellationToken);
        var bootstrap = cached?.Value;
        if (bootstrap is null)
        {
            try
            {
                var result = await bootstrapStore.RefreshResultAsync(token, cancellationToken: cancellationToken);
                if (result.IsUnauthorized)
                {
                    sessionService.Clear();
                    await Shell.Current.GoToAsync("//login");
                    return;
                }
                bootstrap = result.Value;
            }
            catch
            {
                // Free text remains available when the catalog cannot be refreshed.
            }
        }

        if (bootstrap is null)
        {
            return;
        }

        var cities = bootstrap.Recommendations
            .Select(recommendation => recommendation.Neighborhood
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .FirstOrDefault())
            .Where(city => !string.IsNullOrWhiteSpace(city))
            .Select(city => city!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(city => city, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        SuggestedCities.Clear();
        foreach (var city in cities)
        {
            SuggestedCities.Add(city);
        }

        OnPropertyChanged(nameof(HasSuggestedCities));
    }

    [RelayCommand]
    private void AddSegment()
    {
        ErrorMessage = null;
        if (Segments.Count == 0)
        {
            AddDefaultSegment();
            return;
        }

        var tripDayCount = (DepartureDate.Date - ArrivalDate.Date).Days + 1;
        if (Segments.Count >= tripDayCount)
        {
            ErrorMessage = "No puedes agregar más ciudades que días de viaje.";
            return;
        }

        var previous = Segments[^1];
        Segments.Add(new BuilderSegmentViewModel
        {
            City = previous.City,
            StartsOn = DepartureDate.Date,
            EndsOn = DepartureDate.Date
        });
        RedistributeSegmentDates();
    }

    [RelayCommand]
    private void RemoveSegment(BuilderSegmentViewModel? segment)
    {
        if (segment is null || Segments.Count <= 1)
        {
            return;
        }

        var index = Segments.IndexOf(segment);
        if (index < 0)
        {
            return;
        }

        if (index > 0)
        {
            Segments[index - 1].EndsOn = segment.EndsOn;
        }
        else
        {
            Segments[1].StartsOn = segment.StartsOn;
        }

        Segments.RemoveAt(index);
    }

    public async Task SearchHotelSuggestionsAsync(BuilderSegmentViewModel segment)
    {
        if (segment.ApplyingHotelSelection) return;
        if (!string.IsNullOrWhiteSpace(segment.HotelPlaceId)) return;
        foreach (var other in Segments.Where(item => !ReferenceEquals(item, segment)))
        {
            other.CancelHotelSearch();
            other.HotelSuggestions.Clear();
        }
        segment.CancelHotelSearch();
        segment.HotelSuggestions.Clear();
        var query = NormalizeSearchText(segment.HotelName);
        var city = segment.City.Trim();
        if (query.Length < 2 || string.IsNullOrWhiteSpace(city)) return;

        var operation = new CancellationTokenSource();
        segment.HotelSearch = operation;
        try
        {
            var cached = await bootstrapStore.GetCachedAsync(cancellationToken: operation.Token);
            if (operation.IsCancellationRequested
                || !ReferenceEquals(segment.HotelSearch, operation)
                || !string.Equals(segment.City.Trim(), city, StringComparison.Ordinal)
                || !string.Equals(NormalizeSearchText(segment.HotelName), query, StringComparison.Ordinal)) return;

            var score = CatalogSearch.CreateFieldScorer(query);
            var matches = cached?.Value.Recommendations
                .Select(item => new
                {
                    Item = item,
                    Score = score(item.Title,
                        $"{item.Neighborhood} {item.Category} {item.RefinedType} {string.Join(' ', item.Tags)}",
                        item.Description)
                })
                .Where(candidate => candidate.Score > 0
                    && TextContains(candidate.Item.Neighborhood, city)
                    && (TextContains(candidate.Item.Category, "hotel")
                        || candidate.Item.Tags.Any(tag => TextContains(tag, "hotel") || TextContains(tag, "alojamiento"))))
                .OrderByDescending(candidate => candidate.Score)
                .ThenBy(candidate => candidate.Item.Title)
                .Take(8)
                .Select(candidate => candidate.Item)
                .Select(item => new PlaceSuggestionDto(
                    $"yuku:{item.Id:N}", item.Title, item.Neighborhood, item.Provider,
                    item.Id, item.Latitude, item.Longitude))
                .ToList() ?? [];
            foreach (var match in matches) segment.HotelSuggestions.Add(match);
            if (matches.Count > 0)
            {
                StatusMessage = null;
                return;
            }

            if (query.Length < 3 || !sessionService.CanSearchGooglePlaces)
            {
                StatusMessage = "No hay hoteles coincidentes. Puedes completarlo manualmente.";
                return;
            }

            await Task.Delay(350, operation.Token);
            var cacheKey = $"{sessionService.ContextVersion}:{System.Globalization.CultureInfo.CurrentUICulture.Name}:hotel:{city.ToUpperInvariant()}:{query.ToUpperInvariant()}";
            if (!_hotelSearchCache.TryGetValue(cacheKey, out var results))
            {
                var token = await sessionService.GetTokenAsync();
                if (string.IsNullOrWhiteSpace(token)) return;
                results = await apiClient.AutocompleteHotelsAsync(token, new PlaceAutocompleteRequest(query, city,
                    segment.HotelSessionToken, System.Globalization.CultureInfo.CurrentUICulture.Name), operation.Token);
                if (_hotelSearchCache.Count >= 20) _hotelSearchCache.Remove(_hotelSearchCache.Keys.First());
                _hotelSearchCache[cacheKey] = results;
            }
            if (operation.IsCancellationRequested
                || !string.Equals(segment.City.Trim(), city, StringComparison.Ordinal)
                || !string.Equals(NormalizeSearchText(segment.HotelName), query, StringComparison.Ordinal)) return;
            foreach (var result in results.Take(5)) segment.HotelSuggestions.Add(result);
            StatusMessage = results.Count == 0 ? "Sin resultados. Puedes escribir el hotel y direccion manualmente." : null;
        }
        catch (OperationCanceledException) { }
        catch (Exception) { if (!operation.IsCancellationRequested) StatusMessage = "Busqueda no disponible. Puedes escribir el hotel y direccion manualmente."; }
        finally
        {
            if (ReferenceEquals(segment.HotelSearch, operation)) segment.HotelSearch = null;
            operation.Dispose();
        }
    }

    public async Task SelectHotelAsync(BuilderSegmentViewModel segment, PlaceSuggestionDto suggestion)
    {
        segment.CancelHotelSearch();
        var city = segment.City;
        var session = segment.HotelSessionToken;
        using var operation = new CancellationTokenSource();
        segment.HotelSearch = operation;

        ApplyHotelSelection(segment, suggestion.Name, suggestion.Address, suggestion.Latitude, suggestion.Longitude, suggestion.PlaceId);
        segment.HotelSuggestions.Clear();
        StatusMessage = null;
        if (suggestion.Provider.Equals("YUKU", StringComparison.OrdinalIgnoreCase)
            || suggestion.Latitude.HasValue && suggestion.Longitude.HasValue)
        {
            segment.HotelSessionToken = Guid.NewGuid().ToString();
            segment.HotelSearch = null;
            return;
        }
        try
        {
            var token = await sessionService.GetTokenAsync();
            if (string.IsNullOrWhiteSpace(token)) return;
            var hotel = await apiClient.GetPlaceDetailsAsync(token, new PlaceDetailsRequest(suggestion.PlaceId, session,
                System.Globalization.CultureInfo.CurrentUICulture.Name), operation.Token);
            if (operation.IsCancellationRequested
                || segment.City != city
                || !string.Equals(segment.HotelPlaceId, suggestion.PlaceId, StringComparison.Ordinal)) return;
            if (hotel is null) return;

            ApplyHotelSelection(segment, hotel.Title, hotel.Neighborhood, hotel.Latitude, hotel.Longitude,
                hotel.ProviderPlaceId ?? suggestion.PlaceId);
        }
        catch (OperationCanceledException) { }
        catch (Exception) when (!operation.IsCancellationRequested)
        {
            StatusMessage = "Hotel seleccionado. La ubicacion exacta se completara cuando este disponible.";
        }
        finally
        {
            segment.HotelSessionToken = Guid.NewGuid().ToString();
            if (ReferenceEquals(segment.HotelSearch, operation)) segment.HotelSearch = null;
        }
    }

    private static void ApplyHotelSelection(
        BuilderSegmentViewModel segment,
        string name,
        string address,
        decimal? latitude,
        decimal? longitude,
        string placeId)
    {
        segment.ApplyingHotelSelection = true;
        try
        {
            segment.HotelName = name;
            segment.HotelAddress = address;
            segment.HotelLatitude = latitude;
            segment.HotelLongitude = longitude;
            segment.HotelPlaceId = placeId;
        }
        finally
        {
            segment.ApplyingHotelSelection = false;
        }
    }

    public void CancelHotelSearches()
    {
        foreach (var segment in Segments) segment.CancelHotelSearch();
    }

    private static string NormalizeSearchText(string value) =>
        string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static bool TextContains(string value, string query) =>
        System.Globalization.CultureInfo.InvariantCulture.CompareInfo.IndexOf(
            value, query,
            System.Globalization.CompareOptions.IgnoreCase | System.Globalization.CompareOptions.IgnoreNonSpace) >= 0;

    [RelayCommand]
    private Task RetryAsync() => _saveAttempted ? SaveSetupAsync() : LoadSetupAsync();

    private bool _saveAttempted;

    [RelayCommand]
    private Task SaveSetupAsync() => LoadAsync(async ct =>
    {
        _saveAttempted = true;
        if (Segments.Count == 0)
        {
            ErrorMessage = "Agrega al menos una ciudad.";
            return;
        }

        if (!TryValidateTripDates(out var validationError))
        {
            ErrorMessage = validationError;
            return;
        }

        var request = new SaveBuilderTripSetupRequest(
            DateOnly.FromDateTime(ArrivalDate),
            DateOnly.FromDateTime(DepartureDate),
            "Asia/Tokyo",
            _revision,
            Segments.Select(item => item.ToDto()).ToList());
        var token = await sessionService.GetTokenAsync();
        var result = string.IsNullOrWhiteSpace(token) ? null : await apiClient.SaveBuilderTripSetupAsync(token, request, ct);
        if (result?.TripId is null)
        {
            ErrorMessage = "Revisa las fechas: las ciudades deben cubrir todo el viaje sin huecos.";
            return;
        }

        _revision = result.Revision;
        sessionService.ApplyTrialAccess(result.TrialAccess);
        SetTripId(result.TripId);
        sessionService.MarkTripConfigured(result.TripId.Value, result.Destination);
        await builderTripStore.SaveAsync(result, ct);
        await todayStore.InvalidateAllAsync();
        var schedule = result.Schedule ?? await apiClient.GetScheduleAsync(token!, ct);
        if (schedule is not null)
        {
            await bootstrapStore.RebindTripAsync(schedule, ct);
            await syncStateStore.AcknowledgeItineraryVersionAsync(schedule.Revision, ct);
        }
        if (Shell.Current is AppShell shell) shell.ApplySessionTabs(sessionService);
        var pending = pendingStore.Take();
        if (pending is not null)
        {
            await Shell.Current.GoToAsync(nameof(ItineraryItemEditorPage), new Dictionary<string, object> { ["Recommendation"] = pending });
        }
        else
        {
            await Shell.Current.GoToAsync("//main/schedule");
        }
    });

    [RelayCommand]
    private async Task CancelAsync()
    {
        pendingStore.Clear();
        await Shell.Current.GoToAsync(IsEditing ? "//main/schedule" : "//main/map");
    }

    private void SetTripId(Guid? tripId)
    {
        if (_tripId == tripId)
        {
            return;
        }

        _tripId = tripId;
        OnPropertyChanged(nameof(IsEditing));
        OnPropertyChanged(nameof(HeaderEyebrow));
        OnPropertyChanged(nameof(HeaderTitle));
        OnPropertyChanged(nameof(HeaderDescription));
        OnPropertyChanged(nameof(PrimaryButtonText));
    }

    private void AddDefaultSegment() => Segments.Add(new BuilderSegmentViewModel
    {
        City = "Tokyo",
        StartsOn = ArrivalDate,
        EndsOn = DepartureDate
    });

    private void AlignOuterSegmentDates()
    {
        if (Segments.Count == 0)
        {
            return;
        }

        Segments[0].StartsOn = ArrivalDate.Date;
        Segments[^1].EndsOn = DepartureDate.Date;
    }

    private void RedistributeSegmentDates()
    {
        var totalDays = (DepartureDate.Date - ArrivalDate.Date).Days + 1;
        var baseDays = totalDays / Segments.Count;
        var extraDays = totalDays % Segments.Count;
        var cursor = ArrivalDate.Date;

        for (var index = 0; index < Segments.Count; index++)
        {
            var segmentDays = baseDays + (index < extraDays ? 1 : 0);
            Segments[index].StartsOn = cursor;
            Segments[index].EndsOn = cursor.AddDays(segmentDays - 1);
            cursor = Segments[index].EndsOn.AddDays(1);
        }
    }

    private bool TryValidateTripDates(out string error)
    {
        if (DepartureDate.Date < ArrivalDate.Date)
        {
            error = "La fecha de salida no puede ser anterior a la llegada.";
            return false;
        }

        if ((DepartureDate.Date - ArrivalDate.Date).TotalDays >= 91)
        {
            error = "El viaje puede tener como máximo 91 días.";
            return false;
        }

        var expectedStart = ArrivalDate.Date;
        foreach (var segment in Segments)
        {
            if (string.IsNullOrWhiteSpace(segment.City))
            {
                error = "Completa el nombre de todas las ciudades.";
                return false;
            }

            if ((segment.StartsOn.Date != expectedStart
                    && (ReferenceEquals(segment, Segments[0]) || segment.StartsOn.Date != expectedStart.AddDays(-1)))
                || segment.EndsOn.Date < segment.StartsOn.Date)
            {
                error = $"Revisa las fechas de {segment.City}: solo el día de traslado puede compartirse entre ciudades.";
                return false;
            }

            expectedStart = segment.EndsOn.Date.AddDays(1);
        }

        if (expectedStart != DepartureDate.Date.AddDays(1))
        {
            error = "Las ciudades deben cubrir todos los días entre llegada y salida.";
            return false;
        }

        error = string.Empty;
        return true;
    }
}

public sealed partial class BuilderSegmentViewModel : ObservableObject
{
    public ObservableCollection<string> CitySuggestions { get; } = [];
    public ObservableCollection<PlaceSuggestionDto> HotelSuggestions { get; } = [];
    public CancellationTokenSource? HotelSearch { get; set; }
    public string HotelSessionToken { get; set; } = Guid.NewGuid().ToString();
    public bool ApplyingHotelSelection { get; set; }
    public void CancelHotelSearch() { HotelSearch?.Cancel(); HotelSearch = null; }
    partial void OnHotelNameChanged(string value) => InvalidateHotel();
    partial void OnCityChanged(string value) => InvalidateHotel();
    private void InvalidateHotel()
    {
        if (ApplyingHotelSelection) return;
        CancelHotelSearch();
        HotelPlaceId = string.Empty;
        HotelAddress = string.Empty;
        HotelLatitude = null;
        HotelLongitude = null;
        HotelSuggestions.Clear();
    }
    [ObservableProperty] private string _city = "Tokyo";
    [ObservableProperty] private DateTime _startsOn = DateTime.Today;
    [ObservableProperty] private DateTime _endsOn = DateTime.Today;
    [ObservableProperty] private string _hotelName = string.Empty;
    [ObservableProperty] private string _hotelAddress = string.Empty;
    [ObservableProperty] private decimal? _hotelLatitude;
    [ObservableProperty] private decimal? _hotelLongitude;
    [ObservableProperty] private string _hotelPlaceId = string.Empty;

    public BuilderTripSetupSegmentDto ToDto() => new(
        City.Trim(), DateOnly.FromDateTime(StartsOn), DateOnly.FromDateTime(EndsOn),
        string.IsNullOrWhiteSpace(HotelName) ? null : HotelName.Trim(),
        string.IsNullOrWhiteSpace(HotelAddress) ? null : HotelAddress.Trim(),
        HotelLatitude, HotelLongitude,
        string.IsNullOrWhiteSpace(HotelPlaceId) ? null : HotelPlaceId);

    public static BuilderSegmentViewModel FromDto(BuilderTripSetupSegmentDto dto) => new()
    {
        City = dto.City,
        StartsOn = dto.StartsOn.ToDateTime(TimeOnly.MinValue),
        EndsOn = dto.EndsOn.ToDateTime(TimeOnly.MinValue),
        HotelName = dto.HotelName ?? string.Empty,
        HotelAddress = dto.HotelAddress ?? string.Empty,
        HotelLatitude = dto.HotelLatitude,
        HotelLongitude = dto.HotelLongitude,
        HotelPlaceId = dto.HotelPlaceId ?? string.Empty
    };
}

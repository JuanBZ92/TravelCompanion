using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TravelCompanion.Mobile.Pages;
using TravelCompanion.Mobile.Services;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Mobile.ViewModels;

public sealed partial class BuilderSetupViewModel(
    TravelCompanionApiClient apiClient,
    AuthSessionService sessionService,
    PendingItineraryActionStore pendingStore,
    SessionLogoutService logoutService,
    MobileBootstrapStore bootstrapStore) : ViewModelBase
{
    private DateTime _arrivalDate = DateTime.Today;
    private DateTime _departureDate = DateTime.Today.AddDays(6);
    private int _revision;

    public ObservableCollection<BuilderSegmentViewModel> Segments { get; } = [];
    public ObservableCollection<string> SuggestedCities { get; } = [];
    public bool HasSuggestedCities => SuggestedCities.Count > 0;
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
        var token = await sessionService.GetTokenAsync();
        if (string.IsNullOrWhiteSpace(token))
        {
            ErrorMessage = "Tu sesión venció. Vuelve a ingresar con tu PIN.";
            return;
        }

        var setup = await apiClient.GetBuilderTripSetupAsync(token, ct);
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
        Segments.Clear();
        _revision = setup.Revision;
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
                bootstrap = await bootstrapStore.RefreshAsync(token, cancellationToken: cancellationToken);
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

    [RelayCommand]
    private async Task SearchHotelAsync(BuilderSegmentViewModel? segment)
    {
        if (segment is null || segment.ApplyingHotelSelection) return;
        segment.CancelHotelSearch();
        segment.HotelSuggestions.Clear();
        if (segment.HotelName.Trim().Length < 3 || string.IsNullOrWhiteSpace(segment.City)) return;
        using var operation = new CancellationTokenSource();
        segment.HotelSearch = operation;
        var query = segment.HotelName.Trim();
        var city = segment.City;
        try
        {
            await Task.Delay(350, operation.Token);
            var token = await sessionService.GetTokenAsync();
            if (string.IsNullOrWhiteSpace(token)) return;
            var results = await apiClient.AutocompleteHotelsAsync(token, new PlaceAutocompleteRequest(query, city,
                segment.HotelSessionToken, System.Globalization.CultureInfo.CurrentUICulture.Name), operation.Token);
            if (operation.IsCancellationRequested || segment.City != city || segment.HotelName.Trim() != query) return;
            foreach (var result in results.Take(5)) segment.HotelSuggestions.Add(result);
            StatusMessage = results.Count == 0 ? "Sin resultados. Puedes escribir el hotel y direccion manualmente." : null;
        }
        catch (OperationCanceledException) { }
        catch (Exception) { if (!operation.IsCancellationRequested) StatusMessage = "Busqueda no disponible. Puedes escribir el hotel y direccion manualmente."; }
        finally { if (ReferenceEquals(segment.HotelSearch, operation)) segment.HotelSearch = null; }
    }

    public Task SearchHotelSuggestionsAsync(BuilderSegmentViewModel segment) => SearchHotelAsync(segment);

    public async Task SelectHotelAsync(BuilderSegmentViewModel segment, PlaceSuggestionDto suggestion)
    {
        segment.CancelHotelSearch();
        var query = segment.HotelName;
        var city = segment.City;
        var session = segment.HotelSessionToken;
        using var operation = new CancellationTokenSource();
        segment.HotelSearch = operation;
        try
        {
            var token = await sessionService.GetTokenAsync();
            if (string.IsNullOrWhiteSpace(token)) return;
            var hotel = await apiClient.GetPlaceDetailsAsync(token, new PlaceDetailsRequest(suggestion.PlaceId, session,
                System.Globalization.CultureInfo.CurrentUICulture.Name), operation.Token);
            if (operation.IsCancellationRequested || segment.City != city || segment.HotelName != query) return;
            if (hotel is null) { StatusMessage = "No pudimos cargar el hotel. Puedes completarlo manualmente."; return; }
            segment.ApplyingHotelSelection = true;
            segment.HotelName = hotel.Title;
            segment.HotelAddress = hotel.Neighborhood;
            segment.HotelLatitude = hotel.Latitude;
            segment.HotelLongitude = hotel.Longitude;
            segment.HotelPlaceId = hotel.ProviderPlaceId ?? string.Empty;
            segment.HotelSuggestions.Clear();
        }
        catch (OperationCanceledException) { }
        catch (Exception) { StatusMessage = "No pudimos cargar el hotel. Puedes completarlo manualmente."; }
        finally
        {
            segment.ApplyingHotelSelection = false;
            segment.HotelSessionToken = Guid.NewGuid().ToString();
            if (ReferenceEquals(segment.HotelSearch, operation)) segment.HotelSearch = null;
        }
    }

    public void CancelHotelSearches()
    {
        foreach (var segment in Segments) segment.CancelHotelSearch();
    }

    [RelayCommand]
    private Task SaveSetupAsync() => LoadAsync(async ct =>
    {
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
        sessionService.MarkTripConfigured(result.TripId.Value, result.Destination);
        await logoutService.ResetContentAsync(
            sessionService.CurrentUserId,
            preservePendingItineraryAction: true);
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
        await Shell.Current.GoToAsync("//main/map");
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

            if (segment.StartsOn.Date != expectedStart || segment.EndsOn.Date < segment.StartsOn.Date)
            {
                error = $"Revisa las fechas de {segment.City}: las ciudades deben cubrir el viaje sin huecos ni días repetidos.";
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

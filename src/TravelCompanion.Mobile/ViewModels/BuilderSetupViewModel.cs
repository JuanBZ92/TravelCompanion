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
    PendingItineraryActionStore pendingStore) : ViewModelBase
{
    private DateTime _arrivalDate = DateTime.Today;
    private DateTime _departureDate = DateTime.Today.AddDays(6);
    private int _revision;

    public ObservableCollection<BuilderSegmentViewModel> Segments { get; } = [];
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
        if (segment is null || string.IsNullOrWhiteSpace(segment.City)) return;
        var token = await sessionService.GetTokenAsync();
        if (string.IsNullOrWhiteSpace(token)) return;
        var query = string.IsNullOrWhiteSpace(segment.HotelName) ? "hotel" : segment.HotelName.Trim();
        var results = await apiClient.SearchPlacesAsync(token, new PlaceSearchRequest(query, City: segment.City));
        if (results.Count == 0)
        {
            StatusMessage = "No encontramos hoteles. Puedes escribirlo manualmente.";
            return;
        }
        var choices = results.Take(8).Select(item => item.Title).Append("Cancelar").ToArray();
        var selected = await Shell.Current.DisplayActionSheetAsync("Elegir hotel/base", "Cancelar", null, choices);
        var hotel = results.FirstOrDefault(item => item.Title == selected);
        if (hotel is null) return;
        segment.HotelName = hotel.Title;
        segment.HotelAddress = hotel.Neighborhood;
        segment.HotelLatitude = hotel.Latitude;
        segment.HotelLongitude = hotel.Longitude;
        segment.HotelPlaceId = hotel.ProviderPlaceId ?? string.Empty;
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

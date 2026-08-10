using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TravelCompanion.Mobile.Services;
using TravelCompanion.Shared;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Mobile.ViewModels;

public sealed partial class DocsViewModel(
    TravelCompanionApiClient apiClient,
    AuthSessionService sessionService,
    OfflineCacheService offlineCacheService) : ViewModelBase, ISessionStateResettable
{
    private static readonly TimeSpan DocsCacheMaxAge = TimeSpan.FromDays(14);

    [ObservableProperty]
    private string title = "Documentos";

    [ObservableProperty]
    private string subtitle = "Vuelos, hoteles, trenes y guias. Todo en un solo lugar.";

    [ObservableProperty]
    private string flightAirline = "Vuelos";

    [ObservableProperty]
    private string flightPassenger = string.Empty;

    [ObservableProperty]
    private string? flightConfirmationCode;

    [ObservableProperty]
    private FlightJourneyItemViewModel? selectedJourney;

    public ObservableCollection<FlightJourneyItemViewModel> Journeys { get; } = [];
    public ObservableCollection<DocumentItemViewModel> HotelDocuments { get; } = [];
    public ObservableCollection<DocumentItemViewModel> OtherDocuments { get; } = [];
    public ObservableCollection<HotelItemViewModel> Hotels { get; } = [];

    public bool HasFlights => Journeys.Count > 0;
    public bool HasHotelDocuments => HotelDocuments.Count > 0;
    public bool HasOtherDocuments => OtherDocuments.Count > 0;
    public bool HasHotels => Hotels.Count > 0;
    partial void OnSelectedJourneyChanged(FlightJourneyItemViewModel? value)
    {
        foreach (var journey in Journeys)
        {
            journey.IsSelected = ReferenceEquals(journey, value);
        }
    }

    [RelayCommand]
    public async Task LoadAsync()
    {
        if (IsBusy)
        {
            return;
        }

        var token = await sessionService.GetTokenAsync();
        if (string.IsNullOrWhiteSpace(token))
        {
            ErrorMessage = "Inicia sesion para ver tus documentos.";
            HasLoaded = true;
            return;
        }

        try
        {
            IsBusy = true;
            ErrorMessage = null;
            StatusMessage = null;
            var cacheKey = GetCacheKey(sessionService.CurrentUserId, sessionService.CurrentTripId);
            var cached = await offlineCacheService.GetAsync<TravelDocsDto>(cacheKey, DocsCacheMaxAge);
            if (cached is not null)
            {
                ApplyDocs(cached.Value);
                MarkLastUpdated(cached.SavedAt);
                StatusMessage = $"Mostrando documentos guardados mientras la API responde. {OfflineCacheService.FormatSavedAt(cached.SavedAt)}";
            }

            var docs = await apiClient.GetTravelDocsAsync(token);
            if (docs is null)
            {
                if (cached is null)
                {
                    ApplyPreviewDocs();
                    StatusMessage = "Vista previa con documentos dummy. Carga los documentos reales desde el admin cuando esten disponibles.";
                }
                else
                {
                    StatusMessage = $"Render puede estar despertando. Mostrando documentos guardados. {OfflineCacheService.FormatSavedAt(cached.SavedAt)}";
                }
            }
            else
            {
                ApplyDocs(docs);
                await offlineCacheService.SaveAsync(cacheKey, docs);
                MarkLastUpdated(DateTimeOffset.UtcNow);
                StatusMessage = null;
            }

            HasLoaded = true;
        }
        catch
        {
            var cacheKey = GetCacheKey(sessionService.CurrentUserId, sessionService.CurrentTripId);
            var cached = await offlineCacheService.GetAsync<TravelDocsDto>(cacheKey, DocsCacheMaxAge);
            if (cached is not null)
            {
                ApplyDocs(cached.Value);
                MarkLastUpdated(cached.SavedAt);
                StatusMessage = $"Render puede estar despertando. Mostrando documentos guardados. {OfflineCacheService.FormatSavedAt(cached.SavedAt)}";
            }
            else
            {
                ApplyPreviewDocs();
                StatusMessage = "No pudimos cargar documentos reales. Mostrando una vista previa dummy.";
            }

            ErrorMessage = null;
            HasLoaded = true;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private Task RefreshAsync() => LoadAsync();

    [RelayCommand]
    private void SelectJourney(FlightJourneyItemViewModel journey)
    {
        SelectedJourney = journey;
    }

    [RelayCommand]
    private static async Task CopyAsync(string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            await Clipboard.SetTextAsync(value);
        }
    }

    public void ResetForNewSession()
    {
        HasLoaded = false;
        ErrorMessage = null;
        Journeys.Clear();
        HotelDocuments.Clear();
        OtherDocuments.Clear();
        Hotels.Clear();
        SelectedJourney = null;
        OnPropertyChanged(nameof(HasFlights));
        OnPropertyChanged(nameof(HasHotelDocuments));
        OnPropertyChanged(nameof(HasOtherDocuments));
        OnPropertyChanged(nameof(HasHotels));
    }

    private void ApplyDocs(TravelDocsDto? docs)
    {
        Journeys.Clear();
        HotelDocuments.Clear();
        OtherDocuments.Clear();
        Hotels.Clear();

        if (docs is null)
        {
            ErrorMessage = "No hay documentos cargados para este viaje.";
            NotifySectionsChanged();
            return;
        }

        Title = "Documentos";
        Subtitle = $"{docs.DestinationName} · {docs.StartsOn:dd/MM} - {docs.EndsOn:dd/MM}";
        FlightAirline = docs.Flights?.Airline ?? "Vuelos";
        FlightPassenger = docs.Flights?.PassengerName ?? docs.TravelerName;
        FlightConfirmationCode = docs.Flights?.ConfirmationCode;

        foreach (var journey in docs.Flights?.Journeys ?? [])
        {
            Journeys.Add(new FlightJourneyItemViewModel(journey, SelectJourney));
        }

        SelectedJourney = Journeys.FirstOrDefault();

        foreach (var document in docs.HotelDocuments)
        {
            HotelDocuments.Add(new DocumentItemViewModel(document, apiClient.BaseAddress));
        }

        foreach (var document in docs.OtherDocuments)
        {
            OtherDocuments.Add(new DocumentItemViewModel(document, apiClient.BaseAddress));
        }

        foreach (var hotel in docs.Hotels)
        {
            Hotels.Add(new HotelItemViewModel(hotel));
        }

        NotifySectionsChanged();
    }

    private void ApplyPreviewDocs()
    {
        ApplyDocs(new TravelDocsDto(
            Guid.Empty,
            "Viajero demo",
            "Japon",
            new DateOnly(2026, 10, 5),
            new DateOnly(2026, 10, 15),
            new FlightDocsSectionDto(
                "Japan Airlines",
                "Viajero demo",
                "Buenos Aires -> Tokyo",
                "DEMO-PNR",
                [
                    new FlightJourneyDto(
                        "ida",
                        "Ida",
                        "Buenos Aires -> Tokyo",
                        [
                            new FlightLegDto(
                                Guid.Empty,
                                new DateOnly(2026, 10, 5),
                                new TimeOnly(13, 30),
                                new DateOnly(2026, 10, 6),
                                new TimeOnly(9, 25),
                                "JL0042",
                                "19h 55m",
                                "Economy",
                                "EZE · Buenos Aires",
                                "HND · Tokyo",
                                null)
                        ]),
                    new FlightJourneyDto(
                        "vuelta",
                        "Vuelta",
                        "Tokyo -> Buenos Aires",
                        [
                            new FlightLegDto(
                                Guid.Empty,
                                new DateOnly(2026, 10, 15),
                                new TimeOnly(22, 45),
                                new DateOnly(2026, 10, 16),
                                new TimeOnly(19, 10),
                                "JL0041",
                                "20h 25m",
                                "Economy",
                                "HND · Tokyo",
                                "EZE · Buenos Aires",
                                null)
                        ])
                ]),
            [
                new TravelDocumentDto(
                    Guid.Empty,
                    TravelDocumentCategory.Hotel,
                    "Hotel Tokyo",
                    "Confirmacion Hotel demo Ginza",
                    string.Empty,
                    10)
            ],
            [
                new TravelDocumentDto(
                    Guid.Empty,
                    TravelDocumentCategory.Other,
                    "Trenes",
                    "Tickets y pases de transporte",
                    string.Empty,
                    20),
                new TravelDocumentDto(
                    Guid.Empty,
                    TravelDocumentCategory.Other,
                    "Seguro de viaje",
                    "Poliza y telefonos utiles",
                    string.Empty,
                    30)
            ],
            [
                new TravelHotelDocDto(
                    Guid.Empty,
                    "Tokyo",
                    "Hotel demo Ginza",
                    "06/10 - 10/10",
                    "DEMO-HTL-1026",
                    "Ginza, Chuo City, Tokyo")
            ]));
    }

    private void NotifySectionsChanged()
    {
        OnPropertyChanged(nameof(HasFlights));
        OnPropertyChanged(nameof(HasHotelDocuments));
        OnPropertyChanged(nameof(HasOtherDocuments));
        OnPropertyChanged(nameof(HasHotels));
    }

    private static string GetCacheKey(Guid? userId, Guid? tripId)
    {
        return $"mobile-docs-{tripId?.ToString() ?? "trip-auto"}-{userId?.ToString() ?? "anonymous"}";
    }
}

public sealed partial class FlightJourneyItemViewModel : ObservableObject
{
    private readonly Action<FlightJourneyItemViewModel> _select;

    public FlightJourneyItemViewModel(FlightJourneyDto journey, Action<FlightJourneyItemViewModel> select)
    {
        _select = select;
        Id = journey.Id;
        Label = journey.Label.ToUpperInvariant();
        Route = journey.Route;
        Legs = journey.Legs
            .Select((leg, index) => new FlightLegItemViewModel(leg, index))
            .ToList();
        SelectCommand = new RelayCommand(() => _select(this));
    }

    [ObservableProperty]
    private bool isSelected;

    public string Id { get; }
    public string Label { get; }
    public string Route { get; }
    public IReadOnlyList<FlightLegItemViewModel> Legs { get; }
    public ICommand SelectCommand { get; }
}

public sealed class FlightLegItemViewModel(FlightLegDto leg, int index)
{
    public string FlightNumber => string.IsNullOrWhiteSpace(leg.FlightNumber) ? "Vuelo" : leg.FlightNumber;
    public string Duration => leg.Duration ?? string.Empty;
    public string DateLabel => leg.Date.ToString("dddd · dd 'de' MMMM", new CultureInfo("es-ES"));
    public string DepartTime => leg.DepartTime.ToString("HH\\:mm");
    public string ArriveTime => leg.ArriveTime?.ToString("HH\\:mm") ?? "--:--";
    public string From => leg.From;
    public string To => leg.To;
    public string? ConnectionNote => index == 0 ? null : leg.ConnectionNote;
    public bool HasConnectionNote => !string.IsNullOrWhiteSpace(ConnectionNote);
}

public sealed class DocumentItemViewModel
{
    public DocumentItemViewModel(TravelDocumentDto document, Uri? apiBaseAddress)
    {
        Title = document.Title;
        Subtitle = document.Subtitle;
        FileUrl = ResolveFileUrl(document.FileUrl, apiBaseAddress);
        OpenCommand = new AsyncRelayCommand(OpenAsync);
    }

    public string Title { get; }
    public string Subtitle { get; }
    public string FileUrl { get; }
    public ICommand OpenCommand { get; }

    private async Task OpenAsync()
    {
        if (!string.IsNullOrWhiteSpace(FileUrl))
        {
            await Launcher.OpenAsync(FileUrl);
        }
    }

    private static string ResolveFileUrl(string fileUrl, Uri? apiBaseAddress)
    {
        if (string.IsNullOrWhiteSpace(fileUrl) || Uri.TryCreate(fileUrl, UriKind.Absolute, out _))
        {
            return fileUrl;
        }

        return apiBaseAddress is null
            ? fileUrl
            : new Uri(apiBaseAddress, fileUrl).ToString();
    }
}

public sealed class HotelItemViewModel(TravelHotelDocDto hotel)
{
    public string City => hotel.City.ToUpperInvariant();
    public string Name => hotel.Name;
    public string DateRange => hotel.DateRange;
}

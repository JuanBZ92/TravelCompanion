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
    OfflineCacheService offlineCacheService,
    OfflineSyncCoordinator syncCoordinator,
    MobileSyncStateStore syncStateStore,
    TripDocumentStore documentStore) : ViewModelBase, ISessionStateResettable
{
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
    public Task LoadAsync() => base.LoadAsync(LoadCoreAsync);

    private async Task LoadCoreAsync(CancellationToken cancellationToken)
    {
        if (!sessionService.HasSession)
        {
            sessionService.Clear();
            await Shell.Current.GoToAsync("//login");
            return;
        }
        Title = Text("TabDocs");
        Subtitle = Text("LocalDocumentsNotice");
        await RefreshLocalDocumentsAsync(cancellationToken);
        OnPropertyChanged(nameof(CanAttachDocument));
        if (!sessionService.HasCuratedDocs || !sessionService.HasKnownValidAccess) return;
        var contextVersion = sessionService.ContextVersion;
        var userId = sessionService.CurrentUserId;
        var tripId = sessionService.CurrentTripId;
        var token = await sessionService.GetTokenAsync();
        if (string.IsNullOrWhiteSpace(token))
        {
            ErrorMessage = "Inicia sesion para ver tus documentos.";
            return;
        }

        try
        {
            var cacheKey = GetCacheKey(userId, tripId);
            var cached = await offlineCacheService.GetAsync<TravelDocsDto>(cacheKey, maxAge: null, cancellationToken);
            if (cached is not null)
            {
                ApplyDocs(cached.Value);
                await RefreshDocumentAvailabilityAsync(cancellationToken);
                MarkLastUpdated(cached.SavedAt);
                StatusMessage = OfflineCacheService.FormatSavedAt(cached.SavedAt);
                var versions = await syncStateStore.GetCachedStateAsync(cancellationToken);
                if (Connectivity.Current.NetworkAccess != NetworkAccess.Internet
                    || versions is not null && cached.Metadata?.DataVersion == versions.DocumentsVersion.ToString(CultureInfo.InvariantCulture))
                    return;
            }

            var result = await apiClient.GetTravelDocsResultAsync(token, cancellationToken);
            if (contextVersion != sessionService.ContextVersion
                || userId != sessionService.CurrentUserId
                || tripId != sessionService.CurrentTripId)
            {
                return;
            }
            if (result.IsUnauthorized)
            {
                sessionService.Clear();
                await Shell.Current.GoToAsync("//login");
                return;
            }
            var docs = result.Value;
            if (docs is null)
            {
                if (cached is null)
                {
                    StatusMessage = Text("NoDocuments");
                }
                else
                {
                    StatusMessage = $"Mostrando documentos guardados mientras recuperamos la conexion. {OfflineCacheService.FormatSavedAt(cached.SavedAt)}";
                }
            }
            else
            {
                ApplyDocs(docs);
                await RefreshDocumentAvailabilityAsync(cancellationToken);
                var metadata = await syncStateStore.CreateCacheMetadataAsync(
                    "documents",
                    $"downloaded:{DateTimeOffset.UtcNow.UtcTicks}",
                    cancellationToken: cancellationToken);
                await offlineCacheService.SaveAsync(cacheKey, docs, metadata, cancellationToken);
                MarkLastUpdated(DateTimeOffset.UtcNow);
                StatusMessage = null;
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            var cacheKey = GetCacheKey(sessionService.CurrentUserId, sessionService.CurrentTripId);
            var cached = await offlineCacheService.GetAsync<TravelDocsDto>(cacheKey, maxAge: null, cancellationToken);
            if (cached is not null)
            {
                ApplyDocs(cached.Value);
                await RefreshDocumentAvailabilityAsync(cancellationToken);
                MarkLastUpdated(cached.SavedAt);
                StatusMessage = $"Mostrando documentos guardados mientras recuperamos la conexion. {OfflineCacheService.FormatSavedAt(cached.SavedAt)}";
            }
            else
            {
                StatusMessage = Text("NoDocuments");
            }

            ErrorMessage = null;
        }
    }

    [RelayCommand]
    private Task RefreshAsync() => base.LoadAsync(async cancellationToken =>
    {
        var token = await sessionService.GetTokenAsync();
        if (!string.IsNullOrWhiteSpace(token))
        {
            await syncCoordinator.SynchronizeVersionsAsync(token, force: true, cancellationToken);
        }
        await LoadCoreAsync(cancellationToken);
    });

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
        LocalDocuments.Clear();
        OnPropertyChanged(nameof(CanAttachDocument));
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

        Title = Text("TabDocs");
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
            HotelDocuments.Add(new DocumentItemViewModel(document, apiClient.BaseAddress, documentStore, () => RefreshLocalDocumentsAsync(default)));
        }

        foreach (var document in docs.OtherDocuments)
        {
            OtherDocuments.Add(new DocumentItemViewModel(document, apiClient.BaseAddress, documentStore, () => RefreshLocalDocumentsAsync(default)));
        }

        foreach (var hotel in docs.Hotels)
        {
            Hotels.Add(new HotelItemViewModel(hotel));
        }

        NotifySectionsChanged();
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

public sealed class DocumentItemViewModel : ObservableObject
{
    private readonly TravelDocumentDto _document;
    private readonly TripDocumentStore _store;
    private string _availability = LocalizationResourceManager.Instance["RequiresConnection"];
    public DocumentItemViewModel(TravelDocumentDto document, Uri? apiBaseAddress, TripDocumentStore store, Func<Task> changed)
    {
        _document = document; _store = store;
        Title = document.Title; Subtitle = document.Subtitle;
        FileUrl = Uri.TryCreate(document.FileUrl, UriKind.Absolute, out var uri) ? uri.ToString()
            : apiBaseAddress is null ? document.FileUrl : new Uri(apiBaseAddress, document.FileUrl).ToString();
        OpenCommand = new AsyncRelayCommand(async () =>
        {
            try
            {
                var local = (await store.ListAsync()).FirstOrDefault(item => item.SourceUrl == document.FileUrl);
                if (local is not null) await store.OpenAsync(local.Id);
                else if (Uri.TryCreate(FileUrl, UriKind.Absolute, out var target) && target.Scheme is "https" or "http")
                    await Launcher.OpenAsync(target);
            }
            catch { await Shell.Current.DisplayAlertAsync(Title, LocalizationResourceManager.Instance["DocumentUnavailable"], "OK"); }
        });
        DownloadCommand = new AsyncRelayCommand(async () =>
        {
            try { await store.DownloadAsync(document.FileUrl, document.Title); await RefreshAsync(); await changed(); }
            catch { await Shell.Current.DisplayAlertAsync(Title, LocalizationResourceManager.Instance["DocumentError"], "OK"); }
        });
    }
    public string Title { get; }
    public string Subtitle { get; }
    public string FileUrl { get; }
    public string Availability { get => _availability; private set => SetProperty(ref _availability, value); }
    public string DownloadText => LocalizationResourceManager.Instance["DownloadDocument"];
    public ICommand OpenCommand { get; }
    public ICommand DownloadCommand { get; }
    public async Task RefreshAsync(CancellationToken ct = default) => Availability = LocalizationResourceManager.Instance[
        await _store.IsDownloadedAsync(_document.FileUrl, ct) ? "OfflineReady" : "RequiresConnection"];
}

public sealed class HotelItemViewModel(TravelHotelDocDto hotel)
{
    public string City => hotel.City.ToUpperInvariant();
    public string Name => hotel.Name;
    public string DateRange => hotel.DateRange;
}

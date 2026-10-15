using CommunityToolkit.Mvvm.ComponentModel;
using TravelCompanion.Mobile.Services;
using TravelCompanion.Shared;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Mobile.ViewModels;

/// <summary>A single cancelable estimate; reservation and Maps remain usable on partial failure.</summary>
public sealed class RoutePlannerViewModel : ObservableObject, IDisposable
{
    private readonly Func<bool> current;
    private readonly Func<bool> canCalculate;
    private readonly Func<CancellationToken, Task<GeoPointDto?>> locate;
    private readonly Func<ItineraryRouteRequest, CancellationToken, Task<ItineraryRouteDto?>> estimate;
    private readonly string zoneId;
    private CancellationTokenSource? cancellation;
    private long generation;
    private GeoPointDto? currentLocation;
    private RouteOriginOption selectedOrigin;
    private string mode;
    private int margin = 10;
    private bool busy;
    private bool accessDenied;
    private string status = "";
    private ItineraryRouteDto? result;

    public RoutePlannerViewModel(ScheduleItemDto destination, TodayHotelBaseDto? hotel,
        IReadOnlyList<ScheduleItemDto> items, string tripZoneId, Func<bool> current, Func<bool> canCalculate,
        Func<CancellationToken, Task<GeoPointDto?>> locate,
        Func<ItineraryRouteRequest, CancellationToken, Task<ItineraryRouteDto?>> estimate,
        string initialMode = "WALK", bool fromCurrent = false)
    {
        Destination = destination; zoneId = destination.TimeZoneId ?? tripZoneId;
        this.current = current; this.canCalculate = canCalculate; this.locate = locate; this.estimate = estimate;
        mode = initialMode is "TRANSIT" or "DRIVE" ? initialMode : "WALK";
        var origins = new List<RouteOriginOption> { new("CurrentLocation", Text("Current"), "") };
        if (hotel is not null && !string.IsNullOrWhiteSpace(hotel.Name))
            origins.Add(new("Hotel", string.Format(Text("Hotel"), hotel.Name), hotel.Name + ", " + hotel.Address,
                hotel.ProviderPlaceId, Latitude: hotel.Latitude, Longitude: hotel.Longitude));
        if (ItineraryRoutePolicy.Previous(items, destination, tripZoneId) is { } previous)
            origins.Add(new("PreviousActivity", string.Format(Text("Previous"), previous.Title),
                string.Join(", ", new[] { previous.LocationName, previous.Address, previous.City, previous.Title }.Where(value => !string.IsNullOrWhiteSpace(value))),
                previous.ProviderPlaceId, previous.Id, previous.Latitude, previous.Longitude));
        Origins = origins;
        selectedOrigin = !fromCurrent ? origins.FirstOrDefault(option => option.Kind == "Hotel") ?? origins[0] : origins[0];
    }

    public ScheduleItemDto Destination { get; }
    public IReadOnlyList<RouteOriginOption> Origins { get; }
    public IReadOnlyList<int> Margins { get; } = [0, 5, 10, 15, 30, 45, 60, 90, 120];
    public RouteOriginOption SelectedOrigin { get => selectedOrigin; set { if (SetProperty(ref selectedOrigin, value)) OptionsChanged(); } }
    public string Mode { get => mode; set { if ((value is "WALK" or "TRANSIT" or "DRIVE") && SetProperty(ref mode, value)) OptionsChanged(); } }
    public int Margin { get => margin; set { if (Margins.Contains(value) && SetProperty(ref margin, value)) OptionsChanged(); } }
    public bool Busy { get => busy; private set { if (SetProperty(ref busy, value)) OnPropertyChanged(nameof(CanEstimate)); } }
    public string Status { get => status; private set => SetProperty(ref status, value); }
    public bool CanEstimate => !Busy && !accessDenied && current() && canCalculate();
    public bool HasResult => result?.Status == "Available";
    public string Duration => HasResult ? string.Format(Text("Duration"), result!.Minutes) : "";
    public string Departure => HasResult ? string.Format(Text(result!.DeparturePassed ? "LeavePassed" : "Leave"), result.DepartureLabel) : "";
    public string OriginSummary => string.Format(Text("OriginSummary"), selectedOrigin.Label);
    public string Timing => $"{Destination.Date.ToString("ddd d MMM", LocalizationResourceManager.Instance.CurrentCulture)} · {Destination.StartsAt:HH\\:mm} · {zoneId}";
    public string MarginSummary => string.Format(Text("MarginSummary"), margin);
    public Uri MapsUri => RouteDirectionsUri.Create(Destination, selectedOrigin, mode, currentLocation);

    public async Task EstimateAsync()
    {
        if (!CanEstimate) { if (current() && (accessDenied || !canCalculate())) Status = Text("Access"); return; }
        Cancel();
        var operation = ++generation;
        using var source = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        cancellation = source;
        var ct = source.Token;
        Busy = true; result = null; Status = Text("Loading"); NotifyResult();
        bool Active() => !ct.IsCancellationRequested && operation == generation && current() && canCalculate();
        try
        {
            GeoPointDto? point = null;
            if (selectedOrigin.Kind == "CurrentLocation")
            {
                point = await locate(ct);
                if (!Active()) return;
                if (point is null) { Status = Text("LocationUnavailable"); return; }
                currentLocation = point;
            }
            var request = new ItineraryRouteRequest(mode, point?.Latitude, point?.Longitude)
            {
                OriginKind = selectedOrigin.Kind, PreviousActivityId = selectedOrigin.ActivityId, ArrivalMarginMinutes = margin
            };
            var response = await estimate(request, ct);
            if (!Active()) return;
            if (response?.Status == "Available" && (response.OriginKind != selectedOrigin.Kind
                || response.ArrivalMarginMinutes != margin || response.Mode != mode))
            {
                // Older backends ignore optional request fields. Never label a hotel estimate as a previous-activity route.
                Status = Text("Unconfirmed");
                return;
            }
            result = response;
            if (response?.Status == "AccessDenied") accessDenied = true;
            Status = response?.Status switch
            {
                "Available" => response.PreviousActivityOverlapsDeparture ? Text("Tight") : "",
                "Past" => Text("Past"),
                "TooEarly" => Text("TooEarly"),
                "NoLocation" => Text("NoLocation"),
                "AccessDenied" => Text("Access"),
                _ => Text("Unavailable")
            };
        }
        catch (OperationCanceledException) { if (operation == generation && current()) Status = Text("Unavailable"); }
        catch (Exception exception) when (exception is HttpRequestException or TimeoutException or InvalidOperationException or System.Text.Json.JsonException)
        { if (Active()) Status = Text("Unavailable"); }
        finally
        {
            if (ReferenceEquals(cancellation, source)) cancellation = null;
            if (operation == generation) { Busy = false; NotifyResult(); }
        }
    }

    public void RefreshAccess()
    {
        if (accessDenied || !current() || !canCalculate()) { Cancel(); result = null; Status = Text("Access"); NotifyResult(); }
        OnPropertyChanged(nameof(CanEstimate));
    }
    public void Cancel()
    {
        generation++; cancellation?.Cancel(); cancellation = null;
        if (Busy) Status = "";
        Busy = false;
    }
    public void Dispose() => Cancel();
    private void OptionsChanged() { Cancel(); result = null; currentLocation = null; Status = accessDenied ? Text("Access") : ""; NotifyResult(); OnPropertyChanged(nameof(OriginSummary)); OnPropertyChanged(nameof(MarginSummary)); }
    private void NotifyResult() { OnPropertyChanged(nameof(HasResult)); OnPropertyChanged(nameof(Duration)); OnPropertyChanged(nameof(Departure)); }
    private static string Text(string suffix) => LocalizationResourceManager.Instance["RoutePlanner" + suffix];
}

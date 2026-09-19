using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Options;
using TravelCompanion.Api.Options;

namespace TravelCompanion.Api.Services;

public sealed record RouteWaypoint(string? PlaceId, decimal? Latitude, decimal? Longitude)
{
    public bool IsValid => !string.IsNullOrWhiteSpace(PlaceId) || Latitude.HasValue && Longitude.HasValue;
    public object ToGoogle() => !string.IsNullOrWhiteSpace(PlaceId) ? new { placeId = PlaceId } :
        (object)new { location = new { latLng = new { latitude = Latitude, longitude = Longitude } } };
}

public sealed record RouteEstimate(int Minutes, DateTimeOffset LeaveAt);

public interface IGoogleRoutesService
{
    Task<RouteEstimate?> EstimateAsync(RouteWaypoint origin, RouteWaypoint destination, string mode,
        DateTimeOffset arrival, CancellationToken cancellationToken);
}

public sealed class GoogleRoutesService(IHttpClientFactory clients, IOptions<GoogleRoutesOptions> options,
    IOptions<GooglePlacesOptions> placesOptions,
    ILogger<GoogleRoutesService> logger) : IGoogleRoutesService
{
    private readonly SemaphoreSlim _gate = new(3);
    private readonly ConcurrentDictionary<string, Lazy<Task<RouteEstimate?>>> _pending = new();

    public async Task<RouteEstimate?> EstimateAsync(RouteWaypoint origin, RouteWaypoint destination, string mode,
        DateTimeOffset arrival, CancellationToken cancellationToken)
    {
        var apiKey = ApiKey();
        if (!options.Value.Enabled || string.IsNullOrWhiteSpace(apiKey)) return null;
        var key = JsonSerializer.Serialize(new { origin, destination, mode, arrival });
        var work = _pending.GetOrAdd(key, _ => new(() => ComputeAsync(origin, destination, mode, arrival)));
        try { return await work.Value.WaitAsync(cancellationToken); }
        finally
        {
            if (work.IsValueCreated && work.Value.IsCompleted) _pending.TryRemove(key, out _);
        }
    }

    private async Task<RouteEstimate?> ComputeAsync(RouteWaypoint origin, RouteWaypoint destination, string mode, DateTimeOffset arrival)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(12));
        var entered = false;
        try
        {
            await _gate.WaitAsync(timeout.Token);
            entered = true;
            return await QueryAsync(origin, destination, mode, arrival, null, timeout.Token);
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or JsonException or FormatException)
        {
            logger.LogWarning("Route estimate unavailable ({ErrorType}).", ex.GetType().Name);
            return null;
        }
        finally { if (entered) _gate.Release(); }
    }

    private async Task<RouteEstimate?> QueryAsync(RouteWaypoint origin, RouteWaypoint destination, string mode,
        DateTimeOffset arrival, DateTimeOffset? departure, CancellationToken ct)
    {
        var body = new Dictionary<string, object>
        {
            ["origin"] = origin.ToGoogle(), ["destination"] = destination.ToGoogle(), ["travelMode"] = mode
        };
        if (mode == "TRANSIT") body["arrivalTime"] = arrival.ToUniversalTime().ToString("O");
        if (mode == "DRIVE")
        {
            body["routingPreference"] = "TRAFFIC_AWARE";
            body["departureTime"] = (departure ?? (arrival > DateTimeOffset.UtcNow ? arrival : DateTimeOffset.UtcNow)).ToUniversalTime().ToString("O");
        }
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://routes.googleapis.com/directions/v2:computeRoutes");
        request.Headers.Add("X-Goog-Api-Key", ApiKey());
        request.Headers.Add("X-Goog-FieldMask", "routes.duration,routes.legs.steps.travelMode,routes.legs.steps.staticDuration,routes.legs.steps.transitDetails.stopDetails.departureTime");
        request.Content = JsonContent.Create(body);
        using var response = await clients.CreateClient("GoogleRoutes").SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("Google Routes returned {StatusCode}.", (int)response.StatusCode);
            return null;
        }
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        if (!json.RootElement.TryGetProperty("routes", out var routes) || routes.GetArrayLength() == 0) return null;
        var route = routes[0];
        var seconds = Seconds(route.GetProperty("duration").GetString());
        var leave = arrival.AddSeconds(-seconds);
        // The first transit departure minus access walking is the actual time to leave the origin.
        if (mode == "TRANSIT" && route.TryGetProperty("legs", out var legs))
        {
            double accessSeconds = 0;
            foreach (var leg in legs.EnumerateArray())
            {
                if (!leg.TryGetProperty("steps", out var steps)) continue;
                foreach (var step in steps.EnumerateArray())
                {
                    if (step.TryGetProperty("transitDetails", out var transit))
                    {
                        leave = DateTimeOffset.Parse(transit.GetProperty("stopDetails").GetProperty("departureTime").GetString()!, CultureInfo.InvariantCulture).AddSeconds(-accessSeconds);
                        return new((int)Math.Ceiling(seconds / 60), leave);
                    }
                    if (step.TryGetProperty("staticDuration", out var duration)) accessSeconds += Seconds(duration.GetString());
                }
            }
        }
        return new((int)Math.Ceiling(seconds / 60), leave);
    }

    private static double Seconds(string? value) => double.Parse(value!.TrimEnd('s'), CultureInfo.InvariantCulture);
    private string ApiKey() => string.IsNullOrWhiteSpace(options.Value.ApiKey) ? placesOptions.Value.ApiKey : options.Value.ApiKey;
}

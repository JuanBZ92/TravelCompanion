using System.Globalization;
using System.Text;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Mobile.Services;

public sealed record RouteOriginOption(string Kind, string Label, string Query,
    string? PlaceId = null, Guid? ActivityId = null, decimal? Latitude = null, decimal? Longitude = null)
{
    public override string ToString() => Label;
}

public static class RouteDirectionsUri
{
    public static Uri Create(ScheduleItemDto destination, RouteOriginOption origin, string mode, GeoPointDto? current = null)
    {
        var target = Query(destination.Latitude, destination.Longitude,
            string.Join(", ", new[] { destination.LocationName, destination.Address, destination.City, destination.Title }
                .Where(value => !string.IsNullOrWhiteSpace(value)).Distinct()));
        var url = $"https://www.google.com/maps/dir/?api=1&destination={Encoded(target, 600)}";
        if (!string.IsNullOrWhiteSpace(destination.ProviderPlaceId) && Uri.EscapeDataString(destination.ProviderPlaceId).Length <= 256)
            url += "&destination_place_id=" + Uri.EscapeDataString(destination.ProviderPlaceId);
        var originQuery = origin.Kind == "CurrentLocation"
            ? current is null ? null : Query(current.Latitude, current.Longitude, "")
            : Query(origin.Latitude, origin.Longitude, origin.Query);
        if (!string.IsNullOrWhiteSpace(originQuery)) url += "&origin=" + Encoded(originQuery, 600);
        if (!string.IsNullOrWhiteSpace(origin.PlaceId) && !string.IsNullOrWhiteSpace(originQuery) && Uri.EscapeDataString(origin.PlaceId).Length <= 256)
            url += "&origin_place_id=" + Uri.EscapeDataString(origin.PlaceId);
        url += "&travelmode=" + (mode switch { "TRANSIT" => "transit", "DRIVE" => "driving", _ => "walking" });
        return new Uri(url);
    }

    private static string Query(decimal? latitude, decimal? longitude, string fallback) => latitude.HasValue && longitude.HasValue
        ? string.Create(CultureInfo.InvariantCulture, $"{latitude},{longitude}") : fallback;

    private static string Encoded(string value, int maxLength)
    {
        // Maps URLs have a 2,048 character limit. Keep complete UTF-8 characters and escape sequences.
        var encoded = new StringBuilder();
        foreach (var rune in value.EnumerateRunes())
        {
            var part = Uri.EscapeDataString(rune.ToString());
            if (encoded.Length + part.Length > maxLength) break;
            encoded.Append(part);
        }
        return encoded.ToString();
    }
}

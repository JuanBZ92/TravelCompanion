using System.Globalization;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Mobile.Services;

public static class GoogleMapsLauncher
{
    public static Task<bool> OpenAsync(RecommendationDto recommendation)
    {
        var queryParts = new[] { recommendation.Title, recommendation.Neighborhood }
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim());
        var query = string.Join(", ", queryParts);

        if (string.IsNullOrWhiteSpace(query))
        {
            return OpenAsync(recommendation.Latitude, recommendation.Longitude);
        }

        return OpenAsync(query, recommendation.ProviderPlaceId);
    }

    public static Task<bool> OpenAsync(decimal latitude, decimal longitude)
    {
        var coordinates = string.Create(
            CultureInfo.InvariantCulture,
            $"{latitude},{longitude}");

        return OpenAsync(coordinates);
    }

    public static Task<bool> OpenAsync(string query, string? providerPlaceId = null)
    {
        var url = $"https://www.google.com/maps/search/?api=1&query={Uri.EscapeDataString(query)}";
        if (!string.IsNullOrWhiteSpace(providerPlaceId))
        {
            url += $"&query_place_id={Uri.EscapeDataString(providerPlaceId.Trim())}";
        }

        return Launcher.Default.TryOpenAsync(new Uri(url));
    }
}

using Microsoft.EntityFrameworkCore;
using TravelCompanion.Api.Data;
using TravelCompanion.Shared;

namespace TravelCompanion.Api.Services;

public sealed class ExternalPlaceInsightsService(TravelCompanionDbContext dbContext)
{
    public async Task<ExternalPlaceInsightsReport> GetReportAsync(CancellationToken cancellationToken = default)
    {
        var savedPlaces = await dbContext.Reservations
            .AsNoTracking()
            .Where(item => item.ItemSource == ItineraryItemSource.GooglePlace
                && item.ProviderPlaceId != null
                && item.ProviderPlaceId != string.Empty)
            .Select(item => new SavedExternalPlace(
                item.Trip!.DestinationId,
                item.Trip.Destination != null ? item.Trip.Destination.Name : "Sin destino",
                item.ProviderPlaceId!,
                item.Title,
                item.LocationName,
                item.City,
                item.Date,
                item.TripId,
                item.Trip.TravelerName,
                item.Trip.AppUserId))
            .ToListAsync(cancellationToken);

        var catalogPlaces = await dbContext.Recommendations
            .AsNoTracking()
            .Where(item => item.ProviderPlaceId != null && item.ProviderPlaceId != string.Empty)
            .Select(item => new { item.Id, item.DestinationId, item.ProviderPlaceId })
            .ToListAsync(cancellationToken);

        var catalogByPlace = catalogPlaces
            .GroupBy(item => (item.DestinationId, PlaceId: NormalizePlaceId(item.ProviderPlaceId!)))
            .ToDictionary(group => group.Key, group => group.First().Id);

        var places = savedPlaces
            .GroupBy(item => (item.DestinationId, PlaceId: NormalizePlaceId(item.ProviderPlaceId)))
            .Select(group =>
            {
                catalogByPlace.TryGetValue(group.Key, out var recommendationId);
                var title = group
                    .Select(item => FirstUseful(item.Title, item.LocationName))
                    .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? "Lugar sin nombre";
                var city = group.Select(item => item.City)
                    .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;
                var trips = group
                    .GroupBy(item => item.TripId)
                    .Select(trip => trip.Select(item => item.TravelerName)
                        .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? "Viaje sin nombre")
                    .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                var travelerCount = group
                    .Select(item => item.AppUserId?.ToString("N") ?? $"trip:{item.TripId:N}")
                    .Distinct(StringComparer.Ordinal)
                    .Count();

                return new ExternalPlaceInsight(
                    group.Key.DestinationId,
                    group.First().DestinationName,
                    group.First().ProviderPlaceId,
                    title,
                    city,
                    group.Count(),
                    group.Select(item => item.TripId).Distinct().Count(),
                    travelerCount,
                    group.Min(item => item.Date),
                    group.Max(item => item.Date),
                    recommendationId == Guid.Empty ? null : recommendationId,
                    trips,
                    BuildGoogleMapsUrl(title, group.First().ProviderPlaceId));
            })
            .OrderByDescending(item => item.SavedCount)
            .ThenBy(item => item.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new ExternalPlaceInsightsReport(
            savedPlaces.Count,
            places.Count,
            places.Count(item => !item.IsInCatalog),
            savedPlaces.Select(item => item.TripId).Distinct().Count(),
            places);
    }

    public static string BuildGoogleMapsUrl(string title, string providerPlaceId)
    {
        return $"https://www.google.com/maps/search/?api=1&query={Uri.EscapeDataString(title)}&query_place_id={Uri.EscapeDataString(providerPlaceId)}";
    }

    private static string NormalizePlaceId(string value) => value.Trim().ToUpperInvariant();

    private static string FirstUseful(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;

    private sealed record SavedExternalPlace(
        Guid DestinationId,
        string DestinationName,
        string ProviderPlaceId,
        string Title,
        string LocationName,
        string City,
        DateOnly Date,
        Guid TripId,
        string TravelerName,
        Guid? AppUserId);
}

public sealed record ExternalPlaceInsightsReport(
    int SavedCount,
    int UniquePlaceCount,
    int PendingReviewCount,
    int TripCount,
    IReadOnlyList<ExternalPlaceInsight> Places);

public sealed record ExternalPlaceInsight(
    Guid DestinationId,
    string DestinationName,
    string ProviderPlaceId,
    string Title,
    string City,
    int SavedCount,
    int TripCount,
    int TravelerCount,
    DateOnly FirstPlannedDate,
    DateOnly LastPlannedDate,
    Guid? RecommendationId,
    IReadOnlyList<string> TripNames,
    string GoogleMapsUrl)
{
    public bool IsInCatalog => RecommendationId.HasValue;
}

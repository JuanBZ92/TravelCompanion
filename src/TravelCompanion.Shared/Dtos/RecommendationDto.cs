using TravelCompanion.Shared;

namespace TravelCompanion.Shared.Dtos;

public sealed record RecommendationDto(
    Guid Id,
    Guid DestinationId,
    string Title,
    string Category,
    string Neighborhood,
    string Description,
    IReadOnlyList<string> Tags,
    string PriceLevel,
    decimal Latitude,
    decimal Longitude,
    int SuggestedDurationMinutes,
    double? Rating,
    string? OpeningHours,
    ContentAccessLevel AccessLevel,
    IReadOnlyList<Guid> PackageIds,
    decimal? DistanceKm)
{
    public string Provider { get; init; } = "YUKU";
    public string? ProviderPlaceId { get; init; }
    public string? Attribution { get; init; }
}

public sealed record PlaceSearchRequest(
    string Query,
    decimal? Latitude = null,
    decimal? Longitude = null,
    string? City = null);

public sealed record RecommendationTagDto(
    string Tag,
    string DisplayName,
    IReadOnlyList<string> Aliases,
    int RecommendationCount,
    bool IsCategory);

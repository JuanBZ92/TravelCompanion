namespace TravelCompanion.Shared.Dtos;

public enum FreeMapMarkerAccess
{
    Unlocked,
    Locked
}

public sealed record FreeMapCityDto(
    string Slug,
    string Name,
    decimal CenterLatitude,
    decimal CenterLongitude,
    decimal FreeRadiusKm,
    int SortOrder);

public sealed record FreeMapMarkerDto(
    string MarkerKey,
    decimal Latitude,
    decimal Longitude,
    FreeMapMarkerAccess Access,
    RecommendationDto? Recommendation);

public sealed record FreeMapPreviewDto(
    DateTimeOffset GeneratedAtUtc,
    FreeMapCityDto City,
    string? ContactUrl,
    int UnlockedCount,
    int LockedCount,
    IReadOnlyList<FreeMapMarkerDto> Markers);

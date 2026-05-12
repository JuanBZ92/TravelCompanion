namespace TravelCompanion.Shared.Dtos;

public sealed record RecommendationDto(
    Guid Id,
    Guid DestinationId,
    string Title,
    string Category,
    string Neighborhood,
    string Description,
    decimal Latitude,
    decimal Longitude,
    int SuggestedDurationMinutes,
    decimal? DistanceKm);

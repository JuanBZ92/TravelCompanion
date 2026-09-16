namespace TravelCompanion.Shared.Dtos;

public sealed record TodayDto(
    DateTimeOffset GeneratedAtUtc,
    DateOnly Date,
    GeoPointDto? CurrentLocationUsed,
    IReadOnlyList<TodaySectionDto> Sections)
{
    public TodayHotelBaseDto? HotelBase { get; init; }
}

public sealed record TodayHotelBaseDto(string Name, string Address, string? ProviderPlaceId, decimal? Latitude, decimal? Longitude)
{
    public string? Attribution { get; init; }
}

public sealed record TodaySectionDto(
    string PeriodKey,
    string Title,
    string Description,
    IReadOnlyList<ScheduleItemDto> Reservations,
    IReadOnlyList<TodayRecommendationDto> Recommendations);

public sealed record TodayRecommendationDto(
    RecommendationDto Recommendation,
    decimal? DistanceKm,
    string RankReason,
    bool IsVisited,
    string? VisitStatusLabel,
    string SuggestedForPeriod,
    bool IsAssigned = false);

public enum RecommendationSignal
{
    Suggested,
    Viewed,
    Saved,
    Dismissed,
    VisitedCandidate,
    VisitedConfirmed
}

public sealed record RecommendationSignalRequest(
    RecommendationSignal Signal,
    string? Source,
    decimal? Latitude,
    decimal? Longitude,
    decimal? DistanceMeters,
    decimal? Confidence,
    DateTimeOffset? OccurredAtUtc);

public sealed record RecommendationSignalResponse(
    bool Accepted,
    string Message);

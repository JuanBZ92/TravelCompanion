namespace TravelCompanion.Shared.Dtos;

public sealed record MobileDiscoverDto(
    DateTimeOffset GeneratedAtUtc,
    DestinationSummaryDto Destination,
    IReadOnlyList<RecommendationDto> Recommendations);

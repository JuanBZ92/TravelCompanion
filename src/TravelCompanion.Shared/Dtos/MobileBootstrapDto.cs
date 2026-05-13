namespace TravelCompanion.Shared.Dtos;

public sealed record MobileBootstrapDto(
    DateTimeOffset GeneratedAtUtc,
    DestinationSummaryDto Destination,
    UserEntitlementsDto Entitlements,
    IReadOnlyList<RecommendationDto> Recommendations,
    IReadOnlyList<TravelPackageDto> Packages,
    TripScheduleDto? Schedule);

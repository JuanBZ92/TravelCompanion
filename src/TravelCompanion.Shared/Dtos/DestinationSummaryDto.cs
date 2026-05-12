namespace TravelCompanion.Shared.Dtos;

public sealed record DestinationSummaryDto(
    Guid Id,
    string Name,
    string Slug,
    string Country,
    string HeroImageUrl,
    string ShortDescription);

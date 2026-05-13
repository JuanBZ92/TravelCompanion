using TravelCompanion.Shared;

namespace TravelCompanion.Shared.Dtos;

public sealed record TravelPackageDto(
    Guid Id,
    Guid DestinationId,
    string Name,
    string Slug,
    string Description,
    decimal Price,
    string Currency,
    bool IsSubscription,
    ContentAccessLevel RequiredAccessLevel,
    bool IsUnlocked);

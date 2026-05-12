using TravelCompanion.Shared;

namespace TravelCompanion.Shared.Dtos;

public sealed record UserEntitlementsDto(
    Guid UserId,
    string Email,
    string DisplayName,
    IReadOnlyList<ContentAccessLevel> AccessLevels,
    IReadOnlyList<Guid> DestinationIds,
    IReadOnlyList<Guid> PackageIds,
    IReadOnlyList<UserEntitlementDto> Entitlements);

public sealed record UserEntitlementDto(
    Guid Id,
    ContentAccessLevel AccessLevel,
    Guid? DestinationId,
    Guid? PackageId,
    DateTimeOffset GrantedAt,
    DateTimeOffset? ExpiresAt,
    string Source);

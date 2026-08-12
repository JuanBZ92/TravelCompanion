using TravelCompanion.Shared;

namespace TravelCompanion.Api.Models;

public sealed class BuilderAccessGrant
{
    public Guid Id { get; set; }
    public Guid AppUserId { get; set; }
    public AppUser? AppUser { get; set; }
    public Guid DestinationId { get; set; }
    public Destination? Destination { get; set; }
    public Guid? TripId { get; set; }
    public Trip? Trip { get; set; }
    public required string PinHash { get; set; }
    public BuilderAccessStatus Status { get; set; } = BuilderAccessStatus.Active;
    public string? OrderReference { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? RedeemedAtUtc { get; set; }
    public DateTimeOffset? ExpiresAtUtc { get; set; }
    public DateTimeOffset? RevokedAtUtc { get; set; }
}

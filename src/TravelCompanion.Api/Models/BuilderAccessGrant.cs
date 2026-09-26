using TravelCompanion.Shared;
using TravelCompanion.Shared.Dtos;

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
    public string? PinHash { get; set; }
    public BuilderAccessStatus Status { get; set; } = BuilderAccessStatus.Active;
    public string? OrderReference { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? RedeemedAtUtc { get; set; }
    public DateTimeOffset? ExpiresAtUtc { get; set; }
    public DateTimeOffset? RevokedAtUtc { get; set; }
    public bool IsTrial { get; set; }
    public FreeAccessPolicy FreePolicy { get; set; }
    public DateTimeOffset? TrialEditingStartedAtUtc { get; set; }
    public DateTimeOffset? TrialEditingExpiresAtUtc { get; set; }
    public DateTimeOffset? TrialDraftExpiresAtUtc { get; set; }
    public int TrialAssistantRequestsUsed { get; set; }
    public DateTimeOffset? ConvertedAtUtc { get; set; }
    public StoreProvider? Origin { get; set; }
    public Guid? PurchaseTransactionId { get; set; }
    public StorePurchaseTransaction? PurchaseTransaction { get; set; }
    public DateTimeOffset? PurchasedAtUtc { get; set; }
    public DateTimeOffset? MaximumExpiresAtUtc { get; set; }
}

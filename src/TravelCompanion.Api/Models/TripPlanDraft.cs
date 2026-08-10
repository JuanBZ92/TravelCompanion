namespace TravelCompanion.Api.Models;

public sealed class TripPlanDraft
{
    public Guid TripId { get; set; }
    public Trip? Trip { get; set; }
    public int BasePlanRevision { get; set; }
    public string PayloadJson { get; set; } = "{}";
    public string? PendingAccessPinHash { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}

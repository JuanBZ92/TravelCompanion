namespace TravelCompanion.Api.Models;

public sealed class PlanningApplicationReceipt
{
    public Guid Id { get; set; }
    public Guid TripId { get; set; }
    public Trip? Trip { get; set; }
    public Guid AppUserId { get; set; }
    public Guid MutationId { get; set; }
    public required string RequestHash { get; set; }
    public required string ResultJson { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
}

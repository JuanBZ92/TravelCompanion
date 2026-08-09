namespace TravelCompanion.Api.Models;

public sealed class Trip
{
    public Guid Id { get; set; }
    public string? ExternalId { get; set; }
    public Guid? AppUserId { get; set; }
    public AppUser? AppUser { get; set; }
    public Guid DestinationId { get; set; }
    public Destination? Destination { get; set; }
    public required string TravelerName { get; set; }
    public string? AccessPinHash { get; set; }
    public DateTimeOffset? AccessPinUpdatedAt { get; set; }
    public DateOnly StartsOn { get; set; }
    public DateOnly EndsOn { get; set; }
    public string TimeZoneId { get; set; } = "UTC";
    public List<Reservation> Reservations { get; set; } = [];
    public List<TravelDocument> Documents { get; set; } = [];
}

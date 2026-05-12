namespace TravelCompanion.Api.Models;

public sealed class Trip
{
    public Guid Id { get; set; }
    public Guid DestinationId { get; set; }
    public Destination? Destination { get; set; }
    public required string TravelerName { get; set; }
    public DateOnly StartsOn { get; set; }
    public DateOnly EndsOn { get; set; }
    public List<Reservation> Reservations { get; set; } = [];
}

using TravelCompanion.Shared;

namespace TravelCompanion.Api.Models;

public sealed class Reservation
{
    public Guid Id { get; set; }
    public Guid TripId { get; set; }
    public Trip? Trip { get; set; }
    public DateOnly Date { get; set; }
    public TimeOnly StartsAt { get; set; }
    public required string Title { get; set; }
    public required string LocationName { get; set; }
    public required string Address { get; set; }
    public required string ConfirmationCode { get; set; }
    public required string Notes { get; set; }
    public ContentAccessLevel AccessLevel { get; set; } = ContentAccessLevel.Paid;
}

using TravelCompanion.Shared;

namespace TravelCompanion.Api.Models;

public sealed class Reservation
{
    public Guid Id { get; set; }
    public string? ExternalId { get; set; }
    public Guid TripId { get; set; }
    public Trip? Trip { get; set; }
    public ReservationType Type { get; set; } = ReservationType.Event;
    public DateOnly Date { get; set; }
    public TimeOnly StartsAt { get; set; }
    public DateOnly? EndsOn { get; set; }
    public TimeOnly? EndsAt { get; set; }
    public required string Title { get; set; }
    public required string City { get; set; }
    public required string LocationName { get; set; }
    public required string Address { get; set; }
    public required string ConfirmationCode { get; set; }
    public required string Notes { get; set; }
    public string? Airline { get; set; }
    public string? FlightNumber { get; set; }
    public string? OriginName { get; set; }
    public string? DestinationName { get; set; }
    public string? OriginAirport { get; set; }
    public string? DestinationAirport { get; set; }
    public string? SourceName { get; set; }
    public string? SourceUrl { get; set; }
}

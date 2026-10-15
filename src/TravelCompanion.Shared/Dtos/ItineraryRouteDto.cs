namespace TravelCompanion.Shared.Dtos;

public sealed record ItineraryRouteRequest(string Mode, decimal? Latitude = null, decimal? Longitude = null)
{
    // Optional additions preserve the existing hotel/GPS request shape.
    public string? OriginKind { get; init; }
    public Guid? PreviousActivityId { get; init; }
    public int ArrivalMarginMinutes { get; init; } = 10;
}
public sealed record ItineraryRouteDto(string Mode, string Status, string Origin, int? Minutes = null,
    DateTimeOffset? LeaveAt = null, bool DeparturePassed = false, string? DepartureLabel = null,
    string? OriginKind = null)
{
    public int ArrivalMarginMinutes { get; init; } = 10;
    public bool PreviousActivityOverlapsDeparture { get; init; }
}

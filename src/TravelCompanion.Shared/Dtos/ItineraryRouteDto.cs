namespace TravelCompanion.Shared.Dtos;

public sealed record ItineraryRouteRequest(string Mode, decimal? Latitude = null, decimal? Longitude = null);
public sealed record ItineraryRouteDto(string Mode, string Status, string Origin, int? Minutes = null,
    DateTimeOffset? LeaveAt = null, bool DeparturePassed = false, string? DepartureLabel = null);

using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Api.Services;

public sealed record TravelPlanningContext(
    string City,
    DateOnly Date,
    TimeOnly? WindowStart,
    TimeOnly? WindowEnd,
    int? AvailableMinutes,
    GeoPointDto? CurrentLocation);

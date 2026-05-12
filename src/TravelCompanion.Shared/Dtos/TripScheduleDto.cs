namespace TravelCompanion.Shared.Dtos;

public sealed record TripScheduleDto(
    Guid TripId,
    string TravelerName,
    string DestinationName,
    DateOnly StartsOn,
    DateOnly EndsOn,
    IReadOnlyList<ScheduleItemDto> Items);

public sealed record ScheduleItemDto(
    Guid Id,
    DateOnly Date,
    TimeOnly StartsAt,
    string Title,
    string LocationName,
    string Address,
    string ConfirmationCode,
    string Notes);

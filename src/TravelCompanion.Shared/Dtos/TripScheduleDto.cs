using TravelCompanion.Shared;

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
    ReservationType Type,
    DateOnly Date,
    TimeOnly StartsAt,
    DateOnly? EndsOn,
    TimeOnly? EndsAt,
    string Title,
    string City,
    string LocationName,
    string Address,
    string ConfirmationCode,
    string Notes,
    string? Airline,
    string? FlightNumber,
    string? OriginName,
    string? DestinationName,
    string? OriginAirport,
    string? DestinationAirport)
{
    public string TypeLabel => Type switch
    {
        ReservationType.Flight => "Vuelo",
        ReservationType.Lodging => "Hospedaje",
        _ => "Evento"
    };

    public string MainDetail => Type switch
    {
        ReservationType.Flight => FormatRoute(),
        ReservationType.Lodging => LocationName,
        _ => LocationName
    };

    public string SecondaryDetail => Type switch
    {
        ReservationType.Flight => FormatFlightDetail(),
        ReservationType.Lodging => Address,
        _ => Address
    };

    public string EndLabel => EndsOn.HasValue && EndsAt.HasValue
        ? $"{EndsOn:MMM d} {EndsAt:HH\\:mm}"
        : EndsOn.HasValue
            ? $"{EndsOn:MMM d}"
            : EndsAt.HasValue
                ? $"{EndsAt:HH\\:mm}"
                : string.Empty;

    public string StartDisplay => Type == ReservationType.Flight
        ? $"Horario de salida: {StartsAt:HH\\:mm}"
        : $"Hora: {StartsAt:HH\\:mm}";

    public string EndDisplay => string.IsNullOrWhiteSpace(EndLabel)
        ? string.Empty
        : Type == ReservationType.Flight
            ? $"Horario de llegada: {EndLabel}"
            : $"Hasta: {EndLabel}";

    public bool HasEnd => !string.IsNullOrWhiteSpace(EndLabel);
    public bool HasAirline => !string.IsNullOrWhiteSpace(Airline);
    public bool HasFlightNumber => !string.IsNullOrWhiteSpace(FlightNumber);
    public bool HasOriginAirport => !string.IsNullOrWhiteSpace(OriginAirport);
    public bool HasDestinationAirport => !string.IsNullOrWhiteSpace(DestinationAirport);

    private string FormatRoute()
    {
        var origin = string.IsNullOrWhiteSpace(OriginName) ? OriginAirport : OriginName;
        var destination = string.IsNullOrWhiteSpace(DestinationName) ? DestinationAirport : DestinationName;

        if (string.IsNullOrWhiteSpace(origin) && string.IsNullOrWhiteSpace(destination))
        {
            return LocationName;
        }

        return $"{origin} -> {destination}";
    }

    private string FormatFlightDetail()
    {
        var parts = new[] { Airline, FlightNumber, OriginAirport, DestinationAirport }
            .Where(value => !string.IsNullOrWhiteSpace(value));

        return string.Join(" · ", parts);
    }
}

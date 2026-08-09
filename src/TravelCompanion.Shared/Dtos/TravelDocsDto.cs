using TravelCompanion.Shared;

namespace TravelCompanion.Shared.Dtos;

public sealed record TravelDocsDto(
    Guid TripId,
    string TravelerName,
    string DestinationName,
    DateOnly StartsOn,
    DateOnly EndsOn,
    FlightDocsSectionDto? Flights,
    IReadOnlyList<TravelDocumentDto> HotelDocuments,
    IReadOnlyList<TravelDocumentDto> OtherDocuments,
    IReadOnlyList<TravelHotelDocDto> Hotels);

public sealed record FlightDocsSectionDto(
    string? Airline,
    string? PassengerName,
    string? Route,
    string? ConfirmationCode,
    IReadOnlyList<FlightJourneyDto> Journeys);

public sealed record FlightJourneyDto(
    string Id,
    string Label,
    string Route,
    IReadOnlyList<FlightLegDto> Legs);

public sealed record FlightLegDto(
    Guid ReservationId,
    DateOnly Date,
    TimeOnly DepartTime,
    DateOnly ArriveDate,
    TimeOnly? ArriveTime,
    string? FlightNumber,
    string? Duration,
    string? Cabin,
    string From,
    string To,
    string? ConnectionNote);

public sealed record TravelDocumentDto(
    Guid Id,
    TravelDocumentCategory Category,
    string Title,
    string Subtitle,
    string FileUrl,
    int SortOrder);

public sealed record TravelHotelDocDto(
    Guid ReservationId,
    string City,
    string Name,
    string DateRange,
    string? ConfirmationCode,
    string? Address);

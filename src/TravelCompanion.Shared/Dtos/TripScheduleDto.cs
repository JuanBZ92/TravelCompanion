using TravelCompanion.Shared;

namespace TravelCompanion.Shared.Dtos;

public sealed record TripScheduleDto(
    Guid TripId,
    string TravelerName,
    string DestinationName,
    DateOnly StartsOn,
    DateOnly EndsOn,
    IReadOnlyList<ScheduleItemDto> Items,
    int Revision = 0,
    IReadOnlyList<DayReviewDto>? DayReviews = null);

public sealed record ScheduleItemDto(
    Guid Id,
    Guid? RecommendationId,
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
    string? DestinationAirport,
    ScheduleItemKind PlanningKind = ScheduleItemKind.ManualEvent,
    ItineraryItemOwner Owner = ItineraryItemOwner.Yuku,
    ItineraryItemSource ItemSource = ItineraryItemSource.Manual,
    ItineraryTimePrecision TimePrecision = ItineraryTimePrecision.Exact,
    int SortOrder = 0,
    string? ProviderPlaceId = null,
    decimal? Latitude = null,
    decimal? Longitude = null,
    ItineraryFlexibility Flexibility = ItineraryFlexibility.Flexible,
    int? DurationMinutes = null,
    bool? ReminderEnabled = null,
    string? TimeZoneId = null)
{
    public string? PeriodKey { get; init; }
    public string? CuratedNotes { get; init; }
    public bool UsesFullCard => HasExactTime || PlanningKind != ScheduleItemKind.Recommendation || !RecommendationId.HasValue;
    public string EffectivePeriodKey => PeriodKey is "morning" or "midday" or "afternoon" or "night"
        ? PeriodKey : StartsAt.Hour switch { < 5 => "night", < 12 => "morning", < 15 => "midday", < 20 => "afternoon", _ => "night" };

    public string TypeLabel => Type switch
    {
        ReservationType.Flight => "Vuelo",
        ReservationType.Lodging => "Hospedaje",
        _ => PlanningKind switch
        {
            ScheduleItemKind.ConfirmedReservation => "Reserva",
            ScheduleItemKind.Recommendation => "Recomendacion",
            _ => "Evento"
        }
    };

    public bool IsConfirmedReservation => PlanningKind == ScheduleItemKind.ConfirmedReservation;
    public bool IsRecommendation => PlanningKind == ScheduleItemKind.Recommendation;
    public bool IsTravelerOwned => Owner == ItineraryItemOwner.Traveler;
    public bool HasExactTime => TimePrecision == ItineraryTimePrecision.Exact;
    public bool IsProtected => Owner == ItineraryItemOwner.Yuku
        || Type is ReservationType.Flight or ReservationType.Lodging
        || Flexibility is ItineraryFlexibility.FixedByTraveler or ItineraryFlexibility.ConfirmedReservation;

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

    public string StartDisplay => !HasExactTime
        ? $"Momento: {PeriodDisplay}"
        : Type == ReservationType.Flight
            ? $"Horario de salida: {StartsAt:HH\\:mm}"
            : $"Hora: {StartsAt:HH\\:mm}";

    public string PeriodDisplay => StartsAt.Hour switch
    {
        < 12 => "Mañana",
        < 15 => "Mediodía",
        < 20 => "Tarde",
        _ => "Noche"
    };

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

using TravelCompanion.Api.Models;
using TravelCompanion.Shared;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Api.Services;

internal static class PremiumDemoTripFactory
{
    private const string TimeZoneId = "Asia/Tokyo";

    private static readonly IReadOnlyList<CitySegment> Segments =
    [
        new("Tokyo", "tokyo", 1, 5, "YUKU Hotel Shinjuku (simulado)", "Shinjuku, Tokyo", 35.693800m, 139.703400m,
            "Tokio contemporáneo: barrios, gastronomía, diseño y vida nocturna."),
        new("Kyoto", "kyoto", 6, 9, "YUKU Machiya Kawaramachi (simulado)", "Kawaramachi, Kyoto", 35.003700m, 135.768800m,
            "Kioto a ritmo pausado: templos, artesanía, jardines y cocina local."),
        new("Osaka", "osaka", 10, 13, "YUKU Hotel Namba (simulado)", "Namba, Osaka", 34.668700m, 135.501300m,
            "Osaka abierta y gastronómica: mercados, arquitectura y noches animadas."),
        new("Fukuoka", "fukuoka", 14, 18, "YUKU Hotel Hakata (simulado)", "Hakata, Fukuoka", 33.590400m, 130.401700m,
            "Fukuoka junto al mar: cultura urbana, excursiones y mesas yatai.")
    ];

    private static readonly HashSet<(int DayNumber, string PeriodKey)> DoubleEventMoments =
    [
        (1, "night"),
        (3, "morning"),
        (5, "afternoon"),
        (7, "midday"),
        (9, "night"),
        (11, "morning"),
        (13, "afternoon"),
        (15, "midday"),
        (17, "night"),
        (18, "afternoon")
    ];

    private static readonly IReadOnlyList<ConfirmedItem> ConfirmedItems =
    [
        new(2, "night", new TimeOnly(19, 30), "Cena omakase confirmada", "Omakase de Shinjuku", "Shinjuku, Tokyo", "TKY-OMK-2202", "Menú degustación reservado para dos personas."),
        new(4, "midday", new TimeOnly(12, 30), "Almuerzo kaiseki confirmado", "Restaurante de Marunouchi", "Marunouchi, Tokyo", "TKY-KSK-2204", "Mesa confirmada; avisar alergias con antelación."),
        new(6, "morning", new TimeOnly(9, 30), "Shinkansen Tokyo → Kyoto", "Tokyo Station", "Tokyo Station, Tokyo", "JR-0609-2222", "Asientos reservados en vagón ordinario; llegada a Kyoto antes del mediodía.", IsTransfer: true),
        new(8, "night", new TimeOnly(19, 0), "Cena de temporada confirmada", "Gion", "Gion, Kyoto", "KYO-GIO-2208", "Cena con menú de temporada y horario confirmado."),
        new(10, "morning", new TimeOnly(9, 15), "Tren Kyoto → Osaka", "Kyoto Station", "Kyoto Station, Kyoto", "JR-1009-2222", "Billetes reservados con llegada a Osaka por la mañana.", IsTransfer: true),
        new(12, "midday", new TimeOnly(12, 0), "Taller de takoyaki confirmado", "Escuela de cocina de Namba", "Namba, Osaka", "OSA-TAK-2212", "Clase práctica con degustación incluida."),
        new(14, "morning", new TimeOnly(9, 10), "Vuelo Osaka → Fukuoka", "Aeropuerto de Itami", "Itami Airport, Osaka", "JAL-1410-2222", "Vuelo doméstico simulado con equipaje facturado.", ReservationType.Flight,
            Airline: "Japan Airlines", FlightNumber: "JL2053", OriginName: "Osaka", DestinationName: "Fukuoka", OriginAirport: "ITM", DestinationAirport: "FUK", IsTransfer: true),
        new(16, "night", new TimeOnly(19, 30), "Ruta yatai confirmada", "Nakasu", "Nakasu, Fukuoka", "FUK-YAT-2216", "Ruta guiada por tres puestos yatai con degustaciones."),
        new(18, "midday", new TimeOnly(12, 30), "Almuerzo de despedida confirmado", "Hakata", "Hakata, Fukuoka", "FUK-BYE-2218", "Mesa confirmada para el cierre del viaje.")
    ];

    public static IReadOnlyList<string> CitySlugs { get; } = Segments.Select(segment => segment.CitySlug).ToArray();

    public static Trip Create(
        Guid appUserId,
        string travelerName,
        Guid destinationId,
        DateOnly firstDate,
        IReadOnlyList<Recommendation> recommendations)
    {
        var recommendationsByCity = recommendations
            .Where(recommendation => !string.IsNullOrWhiteSpace(recommendation.CitySlug))
            .GroupBy(recommendation => recommendation.CitySlug!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.OrderBy(item => item.Title).ToList(), StringComparer.OrdinalIgnoreCase);

        foreach (var segment in Segments)
        {
            if (!recommendationsByCity.TryGetValue(segment.CitySlug, out var cityRecommendations) || cityRecommendations.Count < 2)
            {
                throw new InvalidOperationException($"Premium example requires at least two recommendations for {segment.City}.");
            }
        }

        var trip = new Trip
        {
            Id = Guid.NewGuid(),
            AppUserId = appUserId,
            DestinationId = destinationId,
            TravelerName = travelerName,
            StartsOn = firstDate,
            EndsOn = firstDate.AddDays(17),
            TimeZoneId = TimeZoneId,
            PublicationStatus = TripPublicationStatus.Published,
            ExperienceMode = ExperienceMode.CuratedPremium,
            PublishedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
            PlanRevision = 1
        };

        foreach (var segment in Segments)
        {
            var cityRecommendations = recommendationsByCity[segment.CitySlug];
            var recommendationCursor = 0;
            for (var dayNumber = segment.FirstDay; dayNumber <= segment.LastDay; dayNumber++)
            {
                var day = new TripDayPlan
                {
                    Id = Guid.NewGuid(),
                    TripId = trip.Id,
                    Trip = trip,
                    Date = firstDate.AddDays(dayNumber - 1),
                    DayNumber = dayNumber,
                    City = segment.City,
                    HotelBase = segment.HotelName,
                    BaseAddress = segment.HotelAddress,
                    BaseLatitude = segment.HotelLatitude,
                    BaseLongitude = segment.HotelLongitude,
                    Introduction = segment.Introduction
                };
                trip.DayPlans.Add(day);

                foreach (var period in TripPlanPeriods.All)
                {
                    var block = new TripDayBlock
                    {
                        Id = Guid.NewGuid(),
                        TripDayPlanId = day.Id,
                        TripDayPlan = day,
                        PeriodKey = period.Key,
                        SortOrder = period.SortOrder,
                        AutofillEnabled = false,
                        CuratedDescription = $"{period.Label} del día {dayNumber} en {segment.City}."
                    };
                    day.Blocks.Add(block);

                    var confirmedItem = ConfirmedItems.SingleOrDefault(item => item.DayNumber == dayNumber && item.PeriodKey == period.Key);
                    if (confirmedItem?.IsTransfer != true)
                    {
                        var mainRecommendation = SelectRecommendation(cityRecommendations, period.Key, ref recommendationCursor);
                        AddReservation(trip, block, CreateRecommendationEvent(trip, day, block, period, mainRecommendation, 0));

                        if (DoubleEventMoments.Contains((dayNumber, period.Key)))
                        {
                            var secondRecommendation = SelectRecommendation(cityRecommendations, period.Key, ref recommendationCursor, mainRecommendation.Id);
                            AddReservation(trip, block, CreateRecommendationEvent(trip, day, block, period, secondRecommendation, 1));
                        }
                    }

                    if (dayNumber == segment.FirstDay && period.Key == "afternoon")
                    {
                        AddReservation(trip, block, CreateLodgingReservation(trip, day, block, segment));
                    }

                    if (confirmedItem is not null)
                    {
                        AddReservation(trip, block, CreateConfirmedReservation(trip, day, block, confirmedItem));
                    }
                }
            }
        }

        Validate(trip);
        return trip;
    }

    private static Recommendation SelectRecommendation(
        IReadOnlyList<Recommendation> recommendations,
        string periodKey,
        ref int cursor,
        Guid? excludedId = null)
    {
        var preferredCategories = periodKey switch
        {
            "morning" => new[] { "Food", "Neighborhood", "Nature", "Wellness" },
            "midday" => new[] { "Food", "Culture", "Shopping" },
            "afternoon" => new[] { "Culture", "Shopping", "Viewpoint", "Nature" },
            "night" => new[] { "Food", "Nightlife", "Culture" },
            _ => []
        };
        var preferred = recommendations
            .Where(item => preferredCategories.Contains(item.Category, StringComparer.OrdinalIgnoreCase) && item.Id != excludedId)
            .ToList();
        var candidates = preferred.Count > 0
            ? preferred
            : recommendations.Where(item => item.Id != excludedId).ToList();
        var selected = candidates[cursor % candidates.Count];
        cursor++;
        return selected;
    }

    private static Reservation CreateRecommendationEvent(
        Trip trip,
        TripDayPlan day,
        TripDayBlock block,
        TripPlanPeriodDefinition period,
        Recommendation recommendation,
        int sortOrder) => new()
    {
        Id = Guid.NewGuid(),
        TripId = trip.Id,
        Trip = trip,
        TripDayBlockId = block.Id,
        TripDayBlock = block,
        RecommendationId = recommendation.Id,
        Type = ReservationType.Event,
        PlanningKind = ScheduleItemKind.Recommendation,
        Owner = ItineraryItemOwner.Yuku,
        ItemSource = ItineraryItemSource.YukuRecommendation,
        TimePrecision = ItineraryTimePrecision.PeriodOnly,
        SortOrder = sortOrder,
        Date = day.Date,
        StartsAt = sortOrder == 0 ? period.StartsAt : period.StartsAt.AddMinutes(75),
        TimeZoneId = TimeZoneId,
        Title = recommendation.Title,
        City = day.City,
        LocationName = recommendation.Title,
        Address = recommendation.Neighborhood,
        ConfirmationCode = string.Empty,
        Notes = recommendation.Description,
        Latitude = recommendation.Latitude,
        Longitude = recommendation.Longitude,
        ProviderPlaceId = recommendation.ProviderPlaceId,
        SourceName = recommendation.SourceName,
        SourceUrl = recommendation.SourceUrl
    };

    private static Reservation CreateLodgingReservation(
        Trip trip,
        TripDayPlan day,
        TripDayBlock block,
        CitySegment segment) => new()
    {
        Id = Guid.NewGuid(),
        TripId = trip.Id,
        Trip = trip,
        TripDayBlockId = block.Id,
        TripDayBlock = block,
        Type = ReservationType.Lodging,
        PlanningKind = ScheduleItemKind.ConfirmedReservation,
        Owner = ItineraryItemOwner.Yuku,
        ItemSource = ItineraryItemSource.Manual,
        TimePrecision = ItineraryTimePrecision.Exact,
        SortOrder = 2,
        Date = day.Date,
        StartsAt = new TimeOnly(15, 0),
        EndsOn = trip.StartsOn.AddDays(segment.LastDay),
        EndsAt = new TimeOnly(11, 0),
        TimeZoneId = TimeZoneId,
        Title = $"Estadía en {segment.HotelName}",
        City = segment.City,
        LocationName = segment.HotelName,
        Address = segment.HotelAddress,
        ConfirmationCode = $"HTL-{segment.CitySlug.ToUpperInvariant()}-2222",
        Notes = "Hotel simulado confirmado para todo el tramo de la ciudad.",
        Latitude = segment.HotelLatitude,
        Longitude = segment.HotelLongitude
    };

    private static Reservation CreateConfirmedReservation(
        Trip trip,
        TripDayPlan day,
        TripDayBlock block,
        ConfirmedItem item) => new()
    {
        Id = Guid.NewGuid(),
        TripId = trip.Id,
        Trip = trip,
        TripDayBlockId = block.Id,
        TripDayBlock = block,
        Type = item.Type,
        PlanningKind = ScheduleItemKind.ConfirmedReservation,
        Owner = ItineraryItemOwner.Yuku,
        ItemSource = ItineraryItemSource.Manual,
        TimePrecision = ItineraryTimePrecision.Exact,
        SortOrder = 2,
        Date = day.Date,
        StartsAt = item.StartsAt,
        EndsAt = item.StartsAt.AddMinutes(item.Type == ReservationType.Flight ? 80 : 90),
        TimeZoneId = TimeZoneId,
        Title = item.Title,
        City = day.City,
        LocationName = item.LocationName,
        Address = item.Address,
        ConfirmationCode = item.ConfirmationCode,
        Notes = item.Notes,
        Airline = item.Airline,
        FlightNumber = item.FlightNumber,
        OriginName = item.OriginName,
        DestinationName = item.DestinationName,
        OriginAirport = item.OriginAirport,
        DestinationAirport = item.DestinationAirport
    };

    private static void AddReservation(Trip trip, TripDayBlock block, Reservation reservation)
    {
        block.Reservations.Add(reservation);
        trip.Reservations.Add(reservation);
    }

    private static void Validate(Trip trip)
    {
        if (trip.DayPlans.Count != 18 || trip.EndsOn.DayNumber - trip.StartsOn.DayNumber != 17)
        {
            throw new InvalidOperationException("Premium example must contain exactly 18 days.");
        }

        if (trip.DayPlans.Select(day => day.City).Distinct(StringComparer.OrdinalIgnoreCase).Count() != 4)
        {
            throw new InvalidOperationException("Premium example must contain exactly four cities.");
        }

        if (trip.DayPlans.Any(day => string.IsNullOrWhiteSpace(day.HotelBase)
            || day.Blocks.Count != TripPlanPeriods.All.Count
            || day.Blocks.Any(block => block.Reservations.Count == 0)))
        {
            throw new InvalidOperationException("Every premium day needs a hotel and four populated periods.");
        }

        var blocks = trip.DayPlans.SelectMany(day => day.Blocks).ToList();
        if (!blocks.Any(block => block.Reservations.Count(item => item.PlanningKind != ScheduleItemKind.ConfirmedReservation) >= 2)
            || !blocks.Any(block => block.Reservations.Any(item => item.PlanningKind != ScheduleItemKind.ConfirmedReservation)
                && block.Reservations.Any(item => item.PlanningKind == ScheduleItemKind.ConfirmedReservation)))
        {
            throw new InvalidOperationException("Premium example needs double-event and event-plus-reservation moments.");
        }

        foreach (var city in trip.DayPlans.Select(day => day.City).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!trip.Reservations.Any(item => item.Type == ReservationType.Lodging
                && string.Equals(item.City, city, StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException($"Premium example needs a lodging reservation for {city}.");
            }
        }
    }

    private sealed record CitySegment(
        string City,
        string CitySlug,
        int FirstDay,
        int LastDay,
        string HotelName,
        string HotelAddress,
        decimal HotelLatitude,
        decimal HotelLongitude,
        string Introduction);

    private sealed record ConfirmedItem(
        int DayNumber,
        string PeriodKey,
        TimeOnly StartsAt,
        string Title,
        string LocationName,
        string Address,
        string ConfirmationCode,
        string Notes,
        ReservationType Type = ReservationType.Event,
        string? Airline = null,
        string? FlightNumber = null,
        string? OriginName = null,
        string? DestinationName = null,
        string? OriginAirport = null,
        string? DestinationAirport = null,
        bool IsTransfer = false);
}

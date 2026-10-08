using System.Globalization;
using TravelCompanion.Api.Models;
using TravelCompanion.Shared;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Api.Services;

public sealed partial class TravelChatService
{
    private sealed record FreeTimePlanningWindow(TravelTimeWindow Window, Reservation? NextFixed);

    private static bool HasRequestedWindow(GuidedPlanCriteriaDto? criteria) => criteria is not null
        && (criteria.WindowStartsAtLocal.HasValue || criteria.WindowEndsAtLocal.HasValue
            || criteria.WindowTimeZoneId is not null);

    private FreeTimePlanningWindow? ResolveFreeTimeWindow(Trip trip, DateOnly date,
        GuidedPlanCriteriaDto criteria)
    {
        if (criteria.WindowStartsAtLocal is not { } start || criteria.WindowEndsAtLocal is not { } end
            || string.IsNullOrWhiteSpace(criteria.WindowTimeZoneId)
            || !string.Equals(criteria.WindowTimeZoneId, trip.TimeZoneId, StringComparison.OrdinalIgnoreCase)
            || end - start > TimeSpan.FromHours(2)) return null;
        try
        {
            var zone = TimeZoneInfo.FindSystemTimeZoneById(trip.TimeZoneId);
            if (zone.IsInvalidTime(start) || zone.IsAmbiguousTime(start)
                || zone.IsInvalidTime(end) || zone.IsAmbiguousTime(end)) return null;
            var commitments = new List<(Reservation Reservation, TravelFixedCommitment Commitment)>();
            foreach (var item in trip.Reservations.Where(item => TravelTimeWindowPolicy.IsFixedCommitment(
                         item.Type, item.PlanningKind, item.TimePrecision, item.Flexibility, item.Owner)))
            {
                var localStart = item.Date.ToDateTime(item.StartsAt);
                var localEnd = TravelTimeWindowPolicy.CommitmentEnd(item.Type, item.Date, item.StartsAt,
                    item.EndsOn, item.EndsAt, item.DurationMinutes);
                if (localEnd <= localStart
                    || !TravelTimeWindowPolicy.TryConvertToTripTime(localStart, item.TimeZoneId, zone, out var tripStart)
                    || !TravelTimeWindowPolicy.TryConvertToTripTime(localEnd, item.TimeZoneId, zone, out var tripEnd)) return null;
                commitments.Add((item, new(tripStart, tripEnd)));
            }
            var window = TravelTimeWindowPolicy.Resolve(date, start, end, commitments.Select(item => item.Commitment),
                TimeZoneInfo.ConvertTime((timeProvider ?? TimeProvider.System).GetUtcNow(), zone).DateTime);
            if (window is null) return null;
            var next = commitments.Where(item => item.Commitment.StartsAtLocal == window.NextFixedAtLocal
                    && item.Commitment.StartsAtLocal <= window.EndsAtLocal)
                .OrderBy(item => item.Reservation.Id).Select(item => item.Reservation).FirstOrDefault();
            return new(window, next);
        }
        catch (TimeZoneNotFoundException) { return null; }
        catch (InvalidTimeZoneException) { return null; }
    }

    private TravelChatResponse FreeTimeWindowUnavailable(string conversationId, string locale) =>
        responseComposer.MissingContext(conversationId, "time_window", IsEnglish(locale)
            ? "There is no free time in this interval. Try a shorter duration or try again later."
            : "No hay un rato libre en este intervalo. Prueba una duración menor o vuelve más tarde.", []);

    private Reservation? ResolveFreeTimeSearchAnchor(Trip trip, DateOnly date, Guid reservationId)
    {
        try
        {
            var zone = TimeZoneInfo.FindSystemTimeZoneById(trip.TimeZoneId);
            var now = TimeZoneInfo.ConvertTime((timeProvider ?? TimeProvider.System).GetUtcNow(), zone).DateTime;
            if (date != DateOnly.FromDateTime(now)) return null;
            var future = new List<(Reservation Reservation, DateTime Start)>();
            foreach (var item in trip.Reservations.Where(item => item.TimePrecision == ItineraryTimePrecision.Exact
                         && item.Latitude is >= -90 and <= 90 && item.Longitude is >= -180 and <= 180
                         && (item.Latitude != 0 || item.Longitude != 0)))
            {
                if (!TravelTimeWindowPolicy.TryConvertToTripTime(item.Date.ToDateTime(item.StartsAt),
                        item.TimeZoneId, zone, out var start)) continue;
                if (start > now && DateOnly.FromDateTime(start) == date) future.Add((item, start));
            }
            var next = future.OrderBy(item => item.Start)
                .ThenBy(item => item.Reservation.PlanningKind == ScheduleItemKind.ConfirmedReservation
                    || item.Reservation.Flexibility is ItineraryFlexibility.ConfirmedReservation
                    or ItineraryFlexibility.FixedByTraveler || item.Reservation.Type == ReservationType.Flight ? 0 : 1)
                .ThenBy(item => item.Reservation.Id).Select(item => item.Reservation).FirstOrDefault();
            return next?.Id == reservationId ? next : null;
        }
        catch (TimeZoneNotFoundException) { return null; }
        catch (InvalidTimeZoneException) { return null; }
    }

    private TravelChatResponse FreeTimeAreaUnavailable(string conversationId, string locale) =>
        responseComposer.MissingContext(conversationId, "area", IsEnglish(locale)
            ? "That plan is no longer the next located plan for today. Choose the area again."
            : "Ese plan ya no es el próximo plan con ubicación de hoy. Vuelve a elegir la zona.", []);

    private static int FreeTimeOutboundMinutes(ScoredRecommendation item, TravelPlanningContext context)
    {
        var distance = DayDistance(context.CurrentLocation?.Latitude, context.CurrentLocation?.Longitude,
            item.Recommendation.Latitude, item.Recommendation.Longitude);
        return distance.HasValue ? Math.Max(1, (int)Math.Ceiling(distance.Value * 12)) : 10;
    }

    private static int FreeTimeReturnMinutes(ScoredRecommendation item, FreeTimePlanningWindow window)
    {
        var next = window.NextFixed;
        var distance = next is null ? null : DayDistance(item.Recommendation.Latitude, item.Recommendation.Longitude,
            next.Latitude, next.Longitude);
        // A modest buffer remains even when coordinates coincide; these are estimates, not routing guarantees.
        return distance.HasValue ? Math.Max(5, (int)Math.Ceiling(distance.Value * 12) + 5) : 10;
    }

    private static bool FitsFreeTime(ScoredRecommendation item, FreeTimePlanningWindow window, TravelPlanningContext context) =>
        item.Recommendation.SuggestedDurationMinutes > 0
        && (long)item.Recommendation.SuggestedDurationMinutes + FreeTimeOutboundMinutes(item, context)
            + FreeTimeReturnMinutes(item, window) <= window.Window.AvailableMinutes;

    private TravelCardDto ToFreeTimeCard(ScoredRecommendation item, TravelPlanningContext context,
        FreeTimePlanningWindow window, string locale, Reservation? searchAnchor = null)
    {
        var start = window.Window.StartsAtLocal.AddMinutes(FreeTimeOutboundMinutes(item, context));
        var end = start.AddMinutes(item.Recommendation.SuggestedDurationMinutes);
        var estimate = IsEnglish(locale)
            ? "Duration and travel times are estimates; check the place’s hours before going."
            : "Duración y traslados estimados; confirma el horario del lugar antes de ir.";
        var anchorName = string.IsNullOrWhiteSpace(searchAnchor?.LocationName)
            ? searchAnchor?.Title : searchAnchor.LocationName;
        return responseComposer.ToRecommendationCard(item, context) with
        {
            Subtitle = searchAnchor is not null
                ? IsEnglish(locale) ? $"Near your next plan · {anchorName}"
                    : $"Cerca de tu próximo plan · {anchorName}"
                : item.WalkingMinutes.HasValue
                ? IsEnglish(locale) ? $"About {item.WalkingMinutes} min walking · {item.Recommendation.Neighborhood}"
                    : $"Unos {item.WalkingMinutes} min a pie · {item.Recommendation.Neighborhood}"
                : item.Recommendation.Neighborhood,
            Description = RecommendationPresentation.ToDto(item.Recommendation, locale: locale).DisplayDescription,
            StartTime = start.ToString("HH:mm", CultureInfo.InvariantCulture),
            EndTime = end.ToString("HH:mm", CultureInfo.InvariantCulture),
            WhyItFits = IsEnglish(locale)
                ? [$"A visit of about {item.Recommendation.SuggestedDurationMinutes} minutes.",
                    "Estimated travel time fits within this interval."]
                : [$"Una visita de unos {item.Recommendation.SuggestedDurationMinutes} minutos.",
                    "Los traslados estimados caben en este intervalo."],
            Warnings = item.NegativeReasons.Select(reason => LocalizeFreeTimeWarning(reason, item, locale))
                .Append(estimate).Concat(searchAnchor is not null && context.CurrentLocation is null
                    ? [IsEnglish(locale)
                        ? "Your current position is unknown; allow extra time to reach this area."
                        : "No conocemos tu posición actual; prevé tiempo adicional para llegar a esta zona."]
                    : Array.Empty<string>()).ToArray()
        };
    }

    private static string LocalizeFreeTimeWarning(string reason, ScoredRecommendation item, string locale)
    {
        if (!IsEnglish(locale)) return reason;
        // Preserve every warning produced by the ranker; localize its existing Spanish messages here.
        return reason switch
        {
            "Puede quedar justo para el espacio entre reservas." => "Time between bookings may be tight.",
            "Coincide con algo que preferis evitar." => "Matches something you prefer to avoid.",
            "Puede quedar por encima de tu presupuesto." => "May exceed your budget.",
            "Puede chocar con tus restricciones alimentarias." => "May conflict with your dietary restrictions.",
            "No parece abierto durante tu ventana disponible." => "May be closed during this window.",
            "Se parece a una reserva o item que ya tenes en el itinerario." => "Resembles an item already in your itinerary.",
            _ when reason.StartsWith("Requiere cerca de ", StringComparison.Ordinal) =>
                $"Requires about {item.WalkingMinutes} minutes walking.",
            _ => reason
        };
    }
}

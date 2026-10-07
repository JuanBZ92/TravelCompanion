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

    private static FreeTimePlanningWindow? ResolveFreeTimeWindow(Trip trip, DateOnly date,
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
                TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, zone).DateTime);
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
            ? "There is no free time in this window. Change the start or duration."
            : "No hay un rato libre en este intervalo. Cambia la hora o la duración.", []);

    private static int FreeTimeOutboundMinutes(ScoredRecommendation item, TravelPlanningContext context)
    {
        if (item.WalkingMinutes.HasValue) return item.WalkingMinutes.Value;
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
        FreeTimePlanningWindow window, string locale)
    {
        var start = window.Window.StartsAtLocal.AddMinutes(FreeTimeOutboundMinutes(item, context));
        var end = start.AddMinutes(item.Recommendation.SuggestedDurationMinutes);
        var estimate = IsEnglish(locale)
            ? "Duration and travel times are estimates; check the place’s hours before going."
            : "Duración y traslados estimados; confirma el horario del lugar antes de ir.";
        return responseComposer.ToRecommendationCard(item, context) with
        {
            Subtitle = item.WalkingMinutes.HasValue
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
                .Append(estimate).ToArray()
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

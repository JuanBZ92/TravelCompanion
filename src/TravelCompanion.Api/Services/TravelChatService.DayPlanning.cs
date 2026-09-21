using Microsoft.EntityFrameworkCore;
using TravelCompanion.Api.Models;
using TravelCompanion.Shared;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Api.Services;

public sealed partial class TravelChatService
{
    private static readonly TimeOnly[] DayStopTimes =
        [new(9, 0), new(10, 30), new(13, 0), new(16, 0), new(19, 30)];

    private async Task<TravelChatResponse> CreateUnfilteredDayAsync(
        AppUser user, TravelChatRequest request, string conversationId, string locale, CancellationToken cancellationToken)
    {
        var english = IsEnglish(locale);
        var action = request.GuidedAction!;
        var date = request.Date ?? DateOnly.FromDateTime(DateTime.UtcNow);
        var trips = await dbContext.Trips.AsNoTracking()
            .Include(trip => trip.Destination)
            .Include(trip => trip.Reservations).ThenInclude(item => item.Recommendation)
            .Where(trip => trip.AppUserId == user.Id && trip.PublicationStatus == TripPublicationStatus.Published
                && trip.StartsOn <= date && trip.EndsOn >= date)
            .ToListAsync(cancellationToken);
        if (trips.Count == 0)
            return responseComposer.MissingContext(conversationId, "date", textProvider.NoActiveTripMessage(date, locale), []);

        var existing = trips.SelectMany(trip => trip.Reservations)
            .Where(item => IsReservationOnDate(item, date)).OrderBy(item => item.StartsAt).ToList();
        var selectedIds = (action.ReplaceReservationIds ?? []).Distinct().ToHashSet();
        var selected = existing.Where(item => selectedIds.Contains(item.Id)).ToList();
        if (selected.Count != selectedIds.Count
            || action.DistanceAdjustment is not (null or "closer" or "farther")
            || action.BudgetAdjustment is not (null or "cheaper" or "dearer")
            || selected.Any(item => !CanReplaceDayStop(item)))
            return responseComposer.MissingContext(conversationId, "selection",
                english ? "Select editable events from this day." : "Seleccioná eventos editables de este día.", []);

        var timeline = existing.Where(item => item.Type == ReservationType.Event)
            .Select(item => (Time: item.StartsAt, Title: item.Title,
                Lat: item.Latitude ?? item.Recommendation?.Latitude, Lon: item.Longitude ?? item.Recommendation?.Longitude)).ToList();
        var occupied = existing.Where(item => item.Type == ReservationType.Event)
            .Select(DaySlot).ToHashSet();
        var slots = selected.Count > 0
            ? selected.Select(item => (Slot: DaySlot(item), Original: (Reservation?)item)).ToList()
            : Enumerable.Range(0, 5).Where(slot => !occupied.Contains(slot)
                && !existing.Any(item => BlocksDaySlot(item, date, slot)))
                .Select(slot => (Slot: slot, Original: (Reservation?)null)).ToList();
        if (slots.Count == 0)
        {
            var editable = existing.Where(CanReplaceDayStop).Select(item => WithDayTransfer(new TravelCardDto(
                "existing_day_stop", item.Title, null, null, item.StartsAt.ToString("HH:mm"),
                item.EndsAt?.ToString("HH:mm"), item.Recommendation?.PriceLevel, null, null, [], [],
                item.RecommendationId?.ToString(), item.Id.ToString()) { IsDayPlan = true, IsPeriodOnly = item.TimePrecision != ItineraryTimePrecision.Exact },
                timeline, item.StartsAt, item.Latitude ?? item.Recommendation?.Latitude,
                item.Longitude ?? item.Recommendation?.Longitude, english)).ToList();
            return new(conversationId,
                english ? "Day already complete. Select the events you want to change." : "Día ya completo. Seleccioná los eventos que querés cambiar.",
                "day_complete", editable, [], null);
        }

        var city = ResolveCity(request.City, existing, trips);
        var cities = trips.Where(trip => !string.IsNullOrWhiteSpace(trip.BuilderSegmentsJson))
            .SelectMany(trip => TripCityScope.ForDate(
                System.Text.Json.JsonSerializer.Deserialize<List<BuilderTripSetupSegmentDto>>(trip.BuilderSegmentsJson!) ?? [], date))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (cities.Count == 0) cities.Add(city);
        var context = new TravelPlanningContext(city, date, new(8, 0), new(23, 0), null, null);
        var excluded = trips.SelectMany(trip => trip.Reservations).Where(item => item.RecommendationId.HasValue)
            .Select(item => item.RecommendationId!.Value.ToString()).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var ranked = new List<ScoredRecommendation>();
        var cityByRecommendation = new Dictionary<Guid, string>();
        foreach (var candidateCity in cities)
        {
            var result = await recommendationPlanningService.RankAsync(user,
                trips.Select(trip => trip.DestinationId).Distinct().ToList(), candidateCity,
                new TravelPreferenceProfile { UserId = user.Id }, existing, context with { City = candidateCity }, BalancedMode,
                new GuidedPlanCriteriaDto { IgnorePreferences = true }, excluded, cancellationToken);
            foreach (var candidate in result.RankedRecommendations.Where(item => cities.Count == 1
                || item.Recommendation.Neighborhood.Contains(candidateCity, StringComparison.OrdinalIgnoreCase)))
                if (cityByRecommendation.TryAdd(candidate.Recommendation.Id, candidateCity)) ranked.Add(candidate);
        }
        var cards = new List<TravelCardDto>();
        var used = new HashSet<Guid>();
        foreach (var (slot, original) in slots.OrderBy(item => item.Original?.StartsAt ?? DayStopTimes[item.Slot]))
        {
            var time = original?.StartsAt ?? DayStopTimes[slot];
            IEnumerable<ScoredRecommendation> options = ranked
                .Where(item => !used.Contains(item.Recommendation.Id)
                    && (slot is 1 or 3 || MatchesDaySlot(item.Recommendation, slot)));
            if (original is not null)
            {
                if (action.DistanceAdjustment is "closer" or "farther")
                {
                    var originalDistance = LargestAdjacentTransfer(timeline, time,
                        original.Latitude ?? original.Recommendation?.Latitude, original.Longitude ?? original.Recommendation?.Longitude).Distance;
                    options = options.Where(item => originalDistance.HasValue
                        && LargestAdjacentTransfer(timeline, time, item.Recommendation.Latitude, item.Recommendation.Longitude).Distance is { } distance
                        && (action.DistanceAdjustment == "closer" ? distance < originalDistance : distance > originalDistance));
                }
                if (action.BudgetAdjustment is "cheaper" or "dearer")
                {
                    var price = original.Recommendation?.IsPriceKnown == true ? DayPriceRank(original.Recommendation.PriceLevel) : null;
                    options = options.Where(item => price.HasValue && item.Recommendation.IsPriceKnown
                        && DayPriceRank(item.Recommendation.PriceLevel) is { } candidatePrice
                        && (action.BudgetAdjustment == "cheaper" ? candidatePrice < price : candidatePrice > price));
                }
            }
            var candidate = options
                .OrderBy(item => cityByRecommendation[item.Recommendation.Id] == (slot < 3 ? cities[0] : cities[^1]) ? 0 : 1)
                .ThenBy(item => slot is 1 or 3 && IsFoodRecommendation(item.Recommendation) ? 1 : 0)
                .ThenBy(item => StableRandomOrder(action.OptionId ?? conversationId, slot.ToString(), item.Recommendation.Id))
                .FirstOrDefault();
            if (candidate is null) continue;
            var recommendation = candidate.Recommendation;
            used.Add(recommendation.Id);
            var dto = RecommendationPresentation.ToDto(recommendation, locale: locale);
            cards.Add(new TravelCardDto("recommendation", dto.Title,
                DaySlotLabel(slot, english), dto.DisplayDescription, time.ToString("HH:mm"),
                time.AddMinutes(recommendation.SuggestedDurationMinutes).ToString("HH:mm"), recommendation.PriceLevel,
                null, null, [english ? "Available for this part of your day." : "Disponible para este momento del día."],
                [], recommendation.Id.ToString(), original?.Id.ToString())
            {
                IsPeriodOnly = original?.TimePrecision != ItineraryTimePrecision.Exact,
                IsDayPlan = true,
                ReplacesRecommendationId = original?.RecommendationId,
                ProviderPlaceId = recommendation.ProviderPlaceId
            });
            if (original is not null)
                timeline.Remove((original.StartsAt, original.Title,
                    original.Latitude ?? original.Recommendation?.Latitude, original.Longitude ?? original.Recommendation?.Longitude));
            timeline.Add((time, recommendation.Title, recommendation.Latitude, recommendation.Longitude));
        }
        for (var index = 0; index < cards.Count; index++)
        {
            var card = cards[index];
            var recommendation = ranked.First(item => item.Recommendation.Id.ToString() == card.RecommendationId).Recommendation;
            cards[index] = WithDayTransfer(card, timeline, TimeOnly.Parse(card.StartTime!), recommendation.Latitude, recommendation.Longitude, english);
        }
        var message = cards.Count == 0
            ? english ? "No matching alternatives found. Your existing plans are unchanged." : "No encontré alternativas compatibles. Tus planes siguen igual."
            : english ? $"Prepared {cards.Count} stops. Existing events are kept unless selected for replacement."
                : $"Preparé {cards.Count} paradas. Se conservan los eventos que no seleccionaste para cambiar.";
        if (cards.Count > 0 && cards.Count < slots.Count)
        {
            var missing = slots.Count - cards.Count;
            message += selected.Count > 0
                ? english ? $" {missing} selected events have no alternative and stay unchanged."
                    : $" {missing} eventos seleccionados no tienen alternativa y siguen igual."
                : english ? $" {missing} slots stay open because no suitable place is available."
                    : $" Quedan {missing} huecos libres porque no hay un lugar adecuado disponible.";
        }
        return new(conversationId, message, "day_plan", cards, [], null);
    }

    private static bool CanReplaceDayStop(Reservation item) => item.Type == ReservationType.Event
        && item.Owner == ItineraryItemOwner.Traveler && item.Flexibility == ItineraryFlexibility.Flexible
        && item.PlanningKind != ScheduleItemKind.ConfirmedReservation;

    private static int DaySlot(Reservation item)
    {
        if (item.StartsAt >= new TimeOnly(18, 0)) return 4;
        if (item.StartsAt >= new TimeOnly(15, 0)) return 3;
        if (item.StartsAt >= new TimeOnly(12, 0)) return 2;
        if (item.TimePrecision == ItineraryTimePrecision.PeriodOnly && item.StartsAt >= DayStopTimes[1]) return 1;
        if (item.Recommendation is not null) return IsFoodRecommendation(item.Recommendation) ? 0 : 1;
        return new[] { "cafe", "café", "coffee", "breakfast", "desayuno" }
            .Any(term => item.Title.Contains(term, StringComparison.OrdinalIgnoreCase)) ? 0 : 1;
    }

    private static bool BlocksDaySlot(Reservation item, DateOnly date, int slot)
    {
        if (item.Type == ReservationType.Lodging || item.TimePrecision != ItineraryTimePrecision.Exact) return false;
        var start = item.Date.ToDateTime(item.StartsAt);
        var end = item.EndsAt.HasValue
            ? (item.EndsOn ?? item.Date).ToDateTime(item.EndsAt.Value)
            : start.AddMinutes(item.DurationMinutes ?? 60);
        if (end <= start) end = end.AddDays(1);
        var time = date.ToDateTime(DayStopTimes[slot]);
        return time >= start && time < end;
    }

    private static bool MatchesDaySlot(Recommendation item, int slot) => slot switch
    {
        0 => ContainsRecommendationTerms(item, "cafe", "café", "coffee", "cafeteria", "breakfast", "desayuno"),
        2 or 4 => IsFoodRecommendation(item)
            && (!ContainsRecommendationTerms(item, "cafe", "café", "coffee", "breakfast", "desayuno")
                || ContainsRecommendationTerms(item, "restaurant", "restaurante", "lunch", "almuerzo", "dinner", "cena", "ramen", "sushi")),
        _ => !IsFoodRecommendation(item)
    };

    private static string DaySlotLabel(int slot, bool english) => english
        ? new[] { "Morning coffee", "Morning visit", "Lunch", "Afternoon place", "Dinner" }[slot]
        : new[] { "Café de mañana", "Visita de mañana", "Almuerzo", "Lugar de tarde", "Cena" }[slot];

    private static int? DayPriceRank(string? price) => price?.ToLowerInvariant() switch
    { "free" => 0, "low" => 1, "medium" => 2, "high" => 3, _ => null };

    private static TravelCardDto WithDayTransfer(TravelCardDto card,
        IReadOnlyList<(TimeOnly Time, string Title, decimal? Lat, decimal? Lon)> timeline,
        TimeOnly time, decimal? latitude, decimal? longitude, bool english)
    {
        var transfer = LargestAdjacentTransfer(timeline, time, latitude, longitude);
        return card with
        {
            DistanceKm = transfer.Distance,
            HasLongTransfer = transfer.Distance > 2,
            Warnings = transfer.Distance > 2 ? [english
                ? $"About {transfer.Distance:0.0} km in a straight line from {transfer.Title}. You can find a closer place."
                : $"Aprox. {transfer.Distance:0.0} km en línea recta desde {transfer.Title}. Podés buscar un lugar más cercano."] : []
        };
    }

    private static (double? Distance, string? Title) LargestAdjacentTransfer(
        IReadOnlyList<(TimeOnly Time, string Title, decimal? Lat, decimal? Lon)> timeline,
        TimeOnly time, decimal? latitude, decimal? longitude)
    {
        var previous = timeline.Where(item => item.Time < time).OrderByDescending(item => item.Time).FirstOrDefault();
        var next = timeline.Where(item => item.Time > time).OrderBy(item => item.Time).FirstOrDefault();
        return new[] { previous, next }
            .Select(item => (Distance: DayDistance(item.Lat, item.Lon, latitude, longitude), Title: (string?)item.Title))
            .Where(item => item.Distance.HasValue).OrderByDescending(item => item.Distance).FirstOrDefault();
    }

    private static double? DayDistance(decimal? lat1, decimal? lon1, decimal? lat2, decimal? lon2)
    {
        if (!lat1.HasValue || !lon1.HasValue || !lat2.HasValue || !lon2.HasValue) return null;
        const double radians = Math.PI / 180;
        var a = Math.Pow(Math.Sin((double)(lat2.Value - lat1.Value) * radians / 2), 2)
            + Math.Cos((double)lat1.Value * radians) * Math.Cos((double)lat2.Value * radians)
            * Math.Pow(Math.Sin((double)(lon2.Value - lon1.Value) * radians / 2), 2);
        return 6371 * 2 * Math.Asin(Math.Sqrt(Math.Clamp(a, 0, 1)));
    }
}

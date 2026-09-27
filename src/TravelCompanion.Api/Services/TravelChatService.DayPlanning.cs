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
        var adaptation = action.AdaptationReason;
        var personalized = action.PlanningMode == "personalized";
        var trips = await dbContext.Trips.AsNoTracking()
            .Include(trip => trip.Destination)
            .Include(trip => trip.Reservations).ThenInclude(item => item.Recommendation)
            .Where(trip => trip.AppUserId == user.Id && trip.PublicationStatus == TripPublicationStatus.Published
                && trip.StartsOn <= date && trip.EndsOn >= date
                && (adaptation == null && !personalized || trip.Id == action.TripId))
            .ToListAsync(cancellationToken);
        if (trips.Count == 0)
            return adaptation is null
                ? responseComposer.MissingContext(conversationId, "date", textProvider.NoActiveTripMessage(date, locale), [])
                : responseComposer.MissingContext(conversationId, "stale",
                    english ? "Your itinerary changed. Refresh the day and generate a new proposal."
                        : "El itinerario cambió. Actualizá el día y generá otra propuesta.", []);

        if (adaptation is not null)
        {
            if (adaptation is not ("late" or "walk_less" or "indoors")
                || (adaptation == "late" && action.DelayMinutes is not (>= 5 and <= 240))
                || (adaptation != "late" && action.DelayMinutes is not null)
                || action.RecommendationId is not null
                || action.ReplaceReservationIds.Count > 0
                || action.DraftDayStops.Count > 0
                || action.DistanceAdjustment is not null
                || action.BudgetAdjustment is not null
                || action.ExpectedRevision is null
                || action.TripId is null)
                return responseComposer.MissingContext(conversationId, "selection",
                    english ? "Choose a valid day adjustment." : "Elegí una adaptación válida para este día.", []);
            if (trips.Count != 1 || trips[0].Id != action.TripId
                || trips[0].PlanRevision != action.ExpectedRevision)
                return responseComposer.MissingContext(conversationId, "stale",
                    english ? "Your itinerary changed. Refresh the day and generate a new proposal."
                        : "El itinerario cambió. Actualizá el día y generá otra propuesta.", []);
        }
        if (personalized)
        {
            var criteria = request.Criteria;
            if (adaptation is not null || action.ExpectedRevision is null || action.TripId is null
                || action.RecommendationId is not null || action.ReplaceReservationIds.Count > 0
                || action.DraftDayStops.Count > 0 || action.DistanceAdjustment is not null
                || action.BudgetAdjustment is not null || criteria is null
                || criteria.TravelPace is not ("relaxed" or "balanced" or "efficient")
                || criteria.Budget is not ("low" or "medium" or "high")
                || criteria.Interests.Count > 3 || criteria.Interests.Any(value =>
                    string.IsNullOrWhiteSpace(value) || value.Length > 32)
                || trips.Count != 1 || trips[0].PlanRevision != action.ExpectedRevision)
                return responseComposer.MissingContext(conversationId, "stale",
                    english ? "Refresh the day and try again." : "Actualizá el día e intentá nuevamente.", []);
        }

        var existing = trips.SelectMany(trip => trip.Reservations)
            .Where(item => IsReservationOnDate(item, date)).OrderBy(item => item.StartsAt).ToList();
        var selectedIds = adaptation is null
            ? (action.ReplaceReservationIds ?? []).Distinct().ToHashSet()
            : existing.Where(item => item.Date == date && CanReplaceDayStop(item))
                .Select(item => item.Id).Take(5).ToHashSet();
        var selected = existing.Where(item => selectedIds.Contains(item.Id)).ToList();
        var drafts = action.DraftDayStops ?? [];
        var isDraftChange = action.RecommendationId is not null;
        var draftTarget = drafts.FirstOrDefault(item => item is not null && item.RecommendationId.ToString() == action.RecommendationId);
        if (selected.Count != selectedIds.Count
            || drafts.Count > 5
            || drafts.Any(item => item is null)
            || (isDraftChange && (draftTarget is null || selectedIds.Count > 0))
            || (!isDraftChange && drafts.Count > 0 && selectedIds.Count == 0)
            || drafts.Select(item => item.RecommendationId).Distinct().Count() != drafts.Count
            || drafts.Select(item => item.StartsAt).Distinct().Count() != drafts.Count
            || drafts.Any(item => item.ReservationId.HasValue && !existing.Any(saved =>
                    saved.Id == item.ReservationId && saved.StartsAt == item.StartsAt && CanReplaceDayStop(saved)))
            || action.DistanceAdjustment is not (null or "closer" or "farther")
            || action.BudgetAdjustment is not (null or "cheaper" or "dearer")
            || selected.Any(item => !CanReplaceDayStop(item)))
            return responseComposer.MissingContext(conversationId, "selection",
                english ? "Select editable events from this day." : "Seleccioná eventos editables de este día.", []);
        if (adaptation is not null && selected.Count == 0)
            return responseComposer.MissingContext(conversationId, "selection",
                english ? "There are no flexible activities to adjust. Confirmed bookings stay in place."
                    : "No hay actividades flexibles para adaptar. Las reservas confirmadas quedan en su lugar.", []);

        var timeline = existing.Where(item => item.Type == ReservationType.Event)
            .Select(item => (Time: item.StartsAt, Title: item.Title,
                Lat: item.Latitude ?? item.Recommendation?.Latitude, Lon: item.Longitude ?? item.Recommendation?.Longitude)).ToList();
        if (personalized && existing.FirstOrDefault(item => item.Type == ReservationType.Lodging
            && item.Latitude.HasValue && item.Longitude.HasValue) is { } hotel)
            timeline.Insert(0, (new TimeOnly(8, 0), hotel.Title, hotel.Latitude, hotel.Longitude));
        var occupied = existing.Where(item => item.Type == ReservationType.Event)
            .Select(DaySlot).ToHashSet();
        var slots = selected.Count > 0
            ? selected.Select(item => (Slot: DaySlot(item), Original: (Reservation?)item)).ToList()
            : Enumerable.Range(0, 5).Where(slot => !occupied.Contains(slot)
                && !existing.Any(item => BlocksDaySlot(item, date, slot)))
                .Select(slot => (Slot: slot, Original: (Reservation?)null)).ToList();
        if (personalized)
        {
            var goal = request.Criteria!.TravelPace switch { "relaxed" => 3, "efficient" => 5, _ => 4 };
            var preferredSlots = goal switch
            {
                3 => new[] { 1, 2, 3 },
                4 => new[] { 0, 1, 2, 3 },
                _ => new[] { 0, 1, 2, 3, 4 }
            };
            slots = slots.Where(item => preferredSlots.Contains(item.Slot))
                .Take(Math.Max(0, goal - existing.Count(item => item.Type == ReservationType.Event))).ToList();
            if (slots.Count == 0)
                return responseComposer.MissingContext(conversationId, "day",
                    english ? "This day is already full for your chosen pace."
                        : "Este día ya está completo para el ritmo elegido.", []);
        }
        if (slots.Count == 0 && !isDraftChange)
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
        var preferenceProfile = adaptation == "indoors" || personalized
            ? await userProfileService.GetProfileAsync(user.Id, cancellationToken)
                ?? new TravelPreferenceProfile { UserId = user.Id }
            : new TravelPreferenceProfile { UserId = user.Id };
        if (personalized)
        {
            preferenceProfile = new TravelPreferenceProfile
            {
                UserId = user.Id,
                FoodPreferences = [.. preferenceProfile.FoodPreferences],
                DietaryRestrictions = [.. preferenceProfile.DietaryRestrictions],
                Dislikes = [.. preferenceProfile.Dislikes],
                AvoidTouristTraps = preferenceProfile.AvoidTouristTraps,
                MaxWalkingMinutes = preferenceProfile.MaxWalkingMinutes,
                BudgetLevel = request.Criteria!.Budget!,
                TravelPace = request.Criteria.TravelPace!,
                Interests = request.Criteria.Interests.Select(value => value.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList()
            };
        }
        var cityByRecommendation = new Dictionary<Guid, string>();
        foreach (var candidateCity in cities)
        {
            var result = await recommendationPlanningService.RankAsync(user,
                trips.Select(trip => trip.DestinationId).Distinct().ToList(), candidateCity,
                preferenceProfile, existing, context with { City = candidateCity }, BalancedMode,
                personalized ? new GuidedPlanCriteriaDto(Budget: request.Criteria!.Budget)
                    : new GuidedPlanCriteriaDto { IgnorePreferences = adaptation != "indoors" }, excluded, cancellationToken);
            foreach (var candidate in result.RankedRecommendations.Where(item => cities.Count == 1
                || item.Recommendation.Neighborhood.Contains(candidateCity, StringComparison.OrdinalIgnoreCase)))
                if (cityByRecommendation.TryAdd(candidate.Recommendation.Id, candidateCity)) ranked.Add(candidate);
        }
        Reservation? draftOriginal = null;
        Reservation? savedDraftTarget = null;
        if (drafts.Count > 0)
        {
            // Use the same accessible, city-scoped catalog as ordinary planning, never client-supplied coordinates or prices.
            var catalog = ranked.ToDictionary(item => item.Recommendation.Id, item => item.Recommendation);
            if (drafts.Any(item => !catalog.ContainsKey(item.RecommendationId)))
                return responseComposer.MissingContext(conversationId, "selection",
                    english ? "This suggestion is no longer available. Generate a new plan."
                        : "Esta sugerencia ya no está disponible. Generá un nuevo plan.", []);
            foreach (var draft in drafts)
            {
                var place = catalog[draft.RecommendationId];
                if (draft.ReservationId is { } savedId)
                {
                    var saved = existing.Single(item => item.Id == savedId);
                    timeline.Remove((saved.StartsAt, saved.Title,
                        saved.Latitude ?? saved.Recommendation?.Latitude, saved.Longitude ?? saved.Recommendation?.Longitude));
                }
                timeline.Add((draft.StartsAt, place.Title, place.Latitude, place.Longitude));
            }
        }
        if (isDraftChange)
        {
            var selectedDraft = draftTarget!;
            var target = ranked.Single(item => item.Recommendation.Id == selectedDraft.RecommendationId).Recommendation;
            savedDraftTarget = existing.FirstOrDefault(item => item.Id == selectedDraft.ReservationId);
            draftOriginal = new Reservation
            {
                Id = savedDraftTarget?.Id ?? Guid.Empty, Title = target.Title, Date = date,
                StartsAt = selectedDraft.StartsAt, RecommendationId = target.Id, Recommendation = target,
                Latitude = target.Latitude, Longitude = target.Longitude,
                TimePrecision = savedDraftTarget?.TimePrecision ?? ItineraryTimePrecision.PeriodOnly,
                City = city, LocationName = target.Title, Address = string.Empty,
                ConfirmationCode = string.Empty, Notes = string.Empty
            };
            slots = [(DaySlot(draftOriginal), draftOriginal)];
        }
        var cards = new List<TravelCardDto>();
        var blockedByFixed = 0;
        var used = new HashSet<Guid>();
        var usedCategories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (slot, original) in slots.OrderBy(item => item.Original?.StartsAt ?? DayStopTimes[item.Slot]))
        {
            var time = original?.StartsAt ?? DayStopTimes[slot];
            if (adaptation == "late" && original is not null)
            {
                if (time.ToTimeSpan().TotalMinutes + action.DelayMinutes!.Value >= 24 * 60)
                {
                    blockedByFixed++;
                    continue;
                }
                time = time.AddMinutes(action.DelayMinutes!.Value);
                var proposedStart = date.ToDateTime(time);
                var proposedEnd = proposedStart.AddMinutes(original.DurationMinutes ?? 60);
                if (existing.Any(item => item.Id != original.Id && !CanReplaceDayStop(item)
                    && item.TimePrecision == ItineraryTimePrecision.Exact
                    && proposedStart < (item.EndsOn ?? item.Date).ToDateTime(item.EndsAt ?? item.StartsAt.AddMinutes(item.DurationMinutes ?? 60))
                    && proposedEnd > item.Date.ToDateTime(item.StartsAt)))
                {
                    blockedByFixed++;
                    continue;
                }
            }
            IEnumerable<ScoredRecommendation> options = ranked
                .Where(item => !used.Contains(item.Recommendation.Id)
                    && !drafts.Any(draft => draft.RecommendationId == item.Recommendation.Id)
                    && (slot is 1 or 3 || MatchesDaySlot(item.Recommendation, slot))
                    && (!personalized || HasDietaryEvidence(item.Recommendation, preferenceProfile.DietaryRestrictions))
                    && (!personalized || !OverlapsExactReservation(existing, date, time,
                        item.Recommendation.SuggestedDurationMinutes)));
            if (adaptation == "indoors")
                options = options.Where(item => IsIndoorCandidate(item.Recommendation));
            if (original is not null)
            {
                var distanceAdjustment = adaptation == "walk_less" ? "closer" : action.DistanceAdjustment;
                if (distanceAdjustment is "closer" or "farther")
                {
                    var originalDistance = LargestAdjacentTransfer(timeline, time,
                        original.Latitude ?? original.Recommendation?.Latitude, original.Longitude ?? original.Recommendation?.Longitude).Distance;
                    options = options.Where(item => originalDistance.HasValue
                        && LargestAdjacentTransfer(timeline, time, item.Recommendation.Latitude, item.Recommendation.Longitude).Distance is { } distance
                        && (distanceAdjustment == "closer" ? distance < originalDistance : distance > originalDistance));
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
                .ThenBy(item => adaptation == "walk_less" || action.DistanceAdjustment == "closer"
                    ? LargestAdjacentTransfer(timeline, time, item.Recommendation.Latitude, item.Recommendation.Longitude).Distance ?? double.MaxValue
                    : 0)
                .ThenByDescending(item => personalized
                    ? item.Score
                        - Math.Min(25, (LargestAdjacentTransfer(timeline, time,
                            item.Recommendation.Latitude, item.Recommendation.Longitude).Distance ?? 0) * 4)
                        - (usedCategories.Contains(item.Recommendation.Category) ? 8 : 0)
                    : 0)
                .ThenBy(item => StableRandomOrder(action.OptionId ?? conversationId, slot.ToString(), item.Recommendation.Id))
                .FirstOrDefault();
            if (candidate is null) continue;
            var recommendation = candidate.Recommendation;
            used.Add(recommendation.Id);
            usedCategories.Add(recommendation.Category);
            var dto = RecommendationPresentation.ToDto(recommendation, locale: locale);
            var reasons = personalized
                ? PersonalizedDayReasons(preferenceProfile, recommendation, timeline, time, english)
                : [english ? "Available for this part of your day." : "Disponible para este momento del día."];
            cards.Add(new TravelCardDto("recommendation", dto.Title,
                DaySlotLabel(slot, english), dto.DisplayDescription, time.ToString("HH:mm"),
                time.AddMinutes(recommendation.SuggestedDurationMinutes).ToString("HH:mm"), recommendation.PriceLevel,
                null, null, reasons,
                [], recommendation.Id.ToString(), original is { Id: var originalId } && originalId != Guid.Empty ? originalId.ToString() : null)
            {
                IsPeriodOnly = original?.TimePrecision != ItineraryTimePrecision.Exact,
                IsDayPlan = true,
                ReplacesRecommendationId = isDraftChange ? savedDraftTarget?.RecommendationId : original?.RecommendationId,
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
        if (cards.Count > 0 && (isDraftChange || selected.Count > 0))
            message = english ? "Alternative ready. Review it and save to update Today. Nothing has been changed yet."
                : "Alternativa lista. Revisala y guardala para actualizar Today. Todavía no se cambió nada.";
        if (cards.Count > 0 && cards.Count < slots.Count)
        {
            var missing = slots.Count - cards.Count;
            message += selected.Count > 0
                ? english ? $" {missing} selected events have no alternative and stay unchanged."
                    : $" {missing} eventos seleccionados no tienen alternativa y siguen igual."
                : english ? $" {missing} slots stay open because no suitable place is available."
                    : $" Quedan {missing} huecos libres porque no hay un lugar adecuado disponible.";
        }
        if (adaptation is not null)
        {
            var changed = cards.Count(card => card.ReservationId is not null);
            var kept = existing.Count - changed;
            var reordered = cards.Count(card => card.ReservationId is { } id
                && existing.First(item => item.Id.ToString() == id).StartsAt.ToString("HH:mm") != card.StartTime);
            for (var index = 0; index < cards.Count; index++)
            {
                var card = cards[index];
                if (card.ReservationId is not { } id) continue;
                var original = existing.First(item => item.Id.ToString() == id);
                cards[index] = card with { WhyItFits = [.. card.WhyItFits,
                    english ? $"Replaces {original.Title}." : $"Sustituye {original.Title}."] };
            }
            message = cards.Count == 0
                ? blockedByFixed > 0
                    ? english ? "The delay conflicts with a fixed booking. Nothing was changed."
                        : "El retraso se cruza con una reserva fija. No cambié nada."
                    : english ? "No safe adjustment was found. Your bookings and plans stay unchanged."
                        : "No encontré un ajuste seguro. Tus reservas y planes siguen igual."
                : english ? $"Proposal only: {kept} kept, {changed} replaced, {reordered} rescheduled. Confirmed bookings stay fixed. Review before saving."
                    : $"Propuesta sin guardar: {kept} conservadas, {changed} sustituidas y {reordered} reordenadas. Las reservas confirmadas quedan fijas. Revisá antes de guardar.";
            if (cards.Count > 0 && blockedByFixed > 0)
                message += english ? $" {blockedByFixed} stops could not move without conflicting with a fixed booking."
                    : $" {blockedByFixed} actividades no se pudieron mover por una reserva fija.";
        }
        if (personalized)
            message = cards.Count == 0
                ? english ? "No suitable places were found. Your plans stay unchanged."
                    : "No encontré lugares compatibles. Tus planes siguen igual."
                : english ? $"{cards.Count} suggestions for your pace and interests. Review before saving; existing plans stay in place."
                    : $"{cards.Count} sugerencias para tu ritmo y tus gustos. Revisalas antes de guardar; tus planes actuales se conservan.";
        if (personalized && await dbContext.Trips.AsNoTracking().AnyAsync(item =>
            item.Id == action.TripId && item.PlanRevision != action.ExpectedRevision, cancellationToken))
            return responseComposer.MissingContext(conversationId, "stale",
                english ? "Your itinerary changed. Refresh the day and generate a new proposal."
                    : "El itinerario cambió. Actualizá el día y generá otra propuesta.", []);
        return new(conversationId, message, "day_plan", cards, [], null);
    }

    private static IReadOnlyList<string> PersonalizedDayReasons(TravelPreferenceProfile profile,
        Recommendation recommendation,
        IReadOnlyList<(TimeOnly Time, string Title, decimal? Lat, decimal? Lon)> timeline,
        TimeOnly time, bool english)
    {
        var text = $"{recommendation.Title} {recommendation.Category} {string.Join(' ', recommendation.Tags)}";
        var interest = profile.Interests.FirstOrDefault(value => text.Contains(value, StringComparison.OrdinalIgnoreCase));
        if (interest is not null)
            return [english ? $"Matches your interest in {interest}." : $"Coincide con tu interés por {interest}."];
        var transfer = LargestAdjacentTransfer(timeline, time, recommendation.Latitude, recommendation.Longitude);
        if (transfer.Distance is <= 2 && transfer.Title is not null)
            return [english ? $"Near {transfer.Title} in a straight line." : $"Cerca de {transfer.Title} en línea recta."];
        if (recommendation.IsPriceKnown && RecommendationBudget.GetRank(recommendation.PriceLevel)
            <= RecommendationBudget.GetRank(profile.BudgetLevel))
            return [english ? "Within your chosen budget." : "Dentro del presupuesto que elegiste."];
        return [english ? "Fits a free part of your day." : "Encaja en un momento libre del día."];
    }

    private static bool OverlapsExactReservation(IReadOnlyList<Reservation> existing, DateOnly date,
        TimeOnly time, int durationMinutes)
    {
        var start = date.ToDateTime(time);
        var end = start.AddMinutes(Math.Max(30, durationMinutes));
        return existing.Any(item =>
        {
            if (item.Type == ReservationType.Lodging || item.TimePrecision != ItineraryTimePrecision.Exact)
                return false;
            var fixedStart = item.Date.ToDateTime(item.StartsAt);
            var fixedEnd = item.EndsAt.HasValue
                ? (item.EndsOn ?? item.Date).ToDateTime(item.EndsAt.Value)
                : fixedStart.AddMinutes(item.DurationMinutes ?? 60);
            if (fixedEnd <= fixedStart) fixedEnd = fixedEnd.AddDays(1);
            return start < fixedEnd && end > fixedStart;
        });
    }

    private static bool IsIndoorCandidate(Recommendation item)
    {
        var text = $"{item.Category} {string.Join(' ', item.Tags)}";
        return new[] { "museum", "museo", "gallery", "galería", "indoor", "interior",
            "restaurant", "restaurante", "cafe", "café", "shopping", "tienda", "store" }
            .Any(term => text.Contains(term, StringComparison.OrdinalIgnoreCase));
    }

    private static bool HasDietaryEvidence(Recommendation item, IReadOnlyList<string> restrictions)
    {
        if (restrictions.Count == 0 || !IsFoodRecommendation(item)) return true;
        var evidence = $"{item.Title} {item.Description} {string.Join(' ', item.Tags)}";
        return restrictions.All(restriction => evidence.Contains(restriction, StringComparison.OrdinalIgnoreCase));
    }

    private static bool CanReplaceDayStop(Reservation item) => ItineraryPlanningPolicy.CanReplace(item);

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

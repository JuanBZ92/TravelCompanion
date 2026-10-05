using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TravelCompanion.Api.Models;
using TravelCompanion.Shared;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Api.Services;

public sealed partial class TravelChatService
{
    internal async Task<DayPlanResponse> GenerateDayPlansAsync(AppUser user, DayPlanRequest request,
        Trip trip, TravelPreferenceProfile profile, TravelRecommendationPlanningService planning,
        CancellationToken cancellationToken)
    {
        var catalog = await planning.LoadUnlockedRecommendationsAsync(user, [trip.DestinationId],
            string.Empty, cancellationToken);
        var recommendations = catalog.ToDictionary(item => item.Id);
        var loaded = new LoadedDayPlanContext(trip, profile, planning, catalog);
        loaded.ExcludeSavedPlaces();
        var days = new List<DayPlanDayDto>();
        var locale = request.Locale ?? "es";
        for (var index = 0; index < request.DayCount; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var date = request.StartDate.AddDays(index);
            var criteria = new GuidedPlanCriteriaDto(Budget: profile.BudgetLevel)
            { TravelPace = profile.TravelPace, Interests = profile.Interests };
            var action = new GuidedTravelActionDto(GuidedTravelActions.FullDay,
                $"{request.OperationId:N}:{date:yyyy-MM-dd}")
            { PlanningMode = "personalized", TripId = trip.Id, ExpectedRevision = trip.PlanRevision };
            var response = await CreateUnfilteredDayAsync(user,
                new TravelChatRequest("Planificar", null, null, date, null, locale, action, criteria),
                request.OperationId.ToString("N"), locale, cancellationToken, loaded);
            var cities = loaded.Cities(date);
            var stops = response.Cards.Select(card => ToDayPlanStop(request.OperationId, date, card,
                recommendations[Guid.Parse(card.RecommendationId!)], cities)).ToList();
            loaded.Excluded.UnionWith(stops.Select(stop => stop.RecommendationId.ToString()));
            var expected = (profile.TravelPace switch
            {
                "relaxed" => new[] { "morning", "midday", "afternoon" },
                "efficient" => new[] { "morning", "morning", "midday", "afternoon", "night" },
                _ => new[] { "morning", "midday", "afternoon", "night" }
            });
            days.Add(new DayPlanDayDto(date, cities, stops,
                expected.GroupBy(period => period).SelectMany(group => Enumerable.Repeat(group.Key,
                    Math.Max(0, group.Count() - stops.Count(stop => stop.PeriodKey == group.Key)))).ToList()));
        }
        var count = days.Sum(day => day.Stops.Count);
        var english = IsEnglish(locale);
        return new(request.OperationId, trip.Id, trip.PlanRevision, days,
            count == 0
                ? english ? "No suitable new ideas were found. Try different interests or dates."
                    : "No encontramos ideas nuevas compatibles. Probá otros intereses o fechas."
                : english ? $"{count} {(count == 1 ? "idea" : "ideas")} for {days.Count} {(days.Count == 1 ? "day" : "days")}. Choose what to add."
                    : $"{count} {(count == 1 ? "idea" : "ideas")} para {days.Count} {(days.Count == 1 ? "día" : "días")}. Elegí qué añadir.");
    }

    internal async Task<DayPlanStopDto?> ReplaceDayPlanStopAsync(AppUser user, Guid operationId,
        DayPlanDayDto day, DayPlanStopDto target, Trip trip, TravelPreferenceProfile profile,
        TravelRecommendationPlanningService planning, ISet<Guid> seen, ISet<string> seenTitles,
        ISet<string> seenProviderIds, string locale, CancellationToken ct)
    {
        var catalog = await planning.LoadUnlockedRecommendationsAsync(user, [trip.DestinationId], string.Empty, ct);
        var slot = Array.FindIndex(DayStopTimes, time => time.ToString("HH:mm") == target.Card.StartTime);
        if (slot < 0) slot = target.PeriodKey switch { "morning" => 1, "midday" => 2, "afternoon" => 3, _ => 4 };
        var loaded = new LoadedDayPlanContext(trip, profile, planning, catalog)
        {
            ReplacementSlot = slot,
            VisibleStops = day.Stops.Where(stop => stop.Id != target.Id).ToList()
        };
        loaded.ExcludeSavedPlaces();
        seenTitles.UnionWith(catalog.Where(item => seen.Contains(item.Id) && !string.IsNullOrWhiteSpace(item.Title))
            .Select(item => item.Title.Trim()));
        seenProviderIds.UnionWith(catalog.Where(item => seen.Contains(item.Id)).Select(item => item.ProviderPlaceId).OfType<string>()
            .Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value.Trim()));
        foreach (var place in catalog.Where(item => seen.Contains(item.Id) || seenTitles.Contains(item.Title.Trim())
            || !string.IsNullOrWhiteSpace(item.ProviderPlaceId)
                && seenProviderIds.Contains(item.ProviderPlaceId.Trim())).ToList())
            loaded.ExcludePlace(place);
        // Preserve aliases too: imports can connect a title-only alias to another provider ID.
        foreach (var place in catalog.Where(loaded.IsExcludedPlace))
        {
            seen.Add(place.Id);
            if (!string.IsNullOrWhiteSpace(place.Title)) seenTitles.Add(place.Title.Trim());
            if (!string.IsNullOrWhiteSpace(place.ProviderPlaceId)) seenProviderIds.Add(place.ProviderPlaceId.Trim());
        }
        loaded.Excluded.UnionWith(seen.Select(id => id.ToString()));
        var criteria = new GuidedPlanCriteriaDto(Budget: profile.BudgetLevel)
        { TravelPace = profile.TravelPace, Interests = profile.Interests };
        var action = new GuidedTravelActionDto(GuidedTravelActions.FullDay, $"{operationId:N}:{day.Date:yyyy-MM-dd}")
        { PlanningMode = "personalized", TripId = trip.Id, ExpectedRevision = trip.PlanRevision };
        var response = await CreateUnfilteredDayAsync(user,
            new TravelChatRequest("Planificar", null, null, day.Date, null, locale, action, criteria),
            operationId.ToString("N"), locale, ct, loaded);
        var card = response.Cards.SingleOrDefault();
        if (card is null) return null;
        var recommendation = catalog.Single(item => item.Id.ToString() == card.RecommendationId);
        return ToDayPlanStop(operationId, day.Date, card, recommendation, loaded.Cities(day.Date));
    }

    private static DayPlanStopDto ToDayPlanStop(Guid operationId, DateOnly date, TravelCardDto card,
        Recommendation recommendation, IReadOnlyList<string> cities) =>
        new(StableStopId(operationId, date, recommendation.Id), recommendation.Id, card.PeriodKey!,
            card with { IsPeriodOnly = true, ReservationId = null })
        {
            Place = !string.IsNullOrWhiteSpace(recommendation.Neighborhood)
                ? recommendation.Neighborhood.Trim()
                : cities.FirstOrDefault(city => recommendation.Description.Contains(city, StringComparison.OrdinalIgnoreCase))
                    ?? cities.FirstOrDefault()
        };

    private static Guid StableStopId(Guid operationId, DateOnly date, Guid recommendationId) =>
        new(SHA256.HashData(Encoding.UTF8.GetBytes($"{operationId:N}:{date:yyyy-MM-dd}:{recommendationId:N}"))[..16]);

    internal static IReadOnlyList<DayPlanCityDayDto> GetDayPlanCityDays(Trip trip)
    {
        var segments = ReadDayPlanSegments(trip);
        return Enumerable.Range(0, trip.EndsOn.DayNumber - trip.StartsOn.DayNumber + 1)
            .Select(index => trip.StartsOn.AddDays(index))
            .Select(date => new DayPlanCityDayDto(date, ResolveDayPlanCities(trip, segments, date))).ToList();
    }

    private static IReadOnlyList<BuilderTripSetupSegmentDto> ReadDayPlanSegments(Trip trip) =>
        string.IsNullOrWhiteSpace(trip.BuilderSegmentsJson)
            ? [] : JsonSerializer.Deserialize<List<BuilderTripSetupSegmentDto>>(trip.BuilderSegmentsJson) ?? [];

    private static IReadOnlyList<string> ResolveDayPlanCities(Trip trip,
        IReadOnlyList<BuilderTripSetupSegmentDto> segments, DateOnly date)
    {
        var result = TripCityScope.ForDate(segments, date);
        if (result.Count > 0) return result;
        var existing = trip.Reservations.Where(item => IsReservationOnDate(item, date)).ToList();
        return [ResolveCity(null, existing, [trip])];
    }

    private sealed class LoadedDayPlanContext(Trip trip, TravelPreferenceProfile profile,
        TravelRecommendationPlanningService planning, IReadOnlyList<Recommendation> catalog)
    {
        private readonly IReadOnlyList<BuilderTripSetupSegmentDto> segments = ReadDayPlanSegments(trip);
        private readonly Dictionary<string, IReadOnlyList<Recommendation>> cities = new(StringComparer.OrdinalIgnoreCase);
        public Trip Trip { get; } = trip;
        public TravelPreferenceProfile Profile { get; } = profile;
        public TravelRecommendationPlanningService Planning { get; } = planning;
        public int? ReplacementSlot { get; init; }
        public IReadOnlyList<DayPlanStopDto> VisibleStops { get; init; } = [];
        public Recommendation? FindRecommendation(Guid id) => catalog.FirstOrDefault(item => item.Id == id);
        public HashSet<string> Excluded { get; } = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Recommendation[]> titles = catalog.Where(item => !string.IsNullOrWhiteSpace(item.Title))
            .GroupBy(item => item.Title.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Recommendation[]> providerIds = catalog.Where(item => !string.IsNullOrWhiteSpace(item.ProviderPlaceId))
            .GroupBy(item => item.ProviderPlaceId!.Trim(), StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
        private readonly HashSet<string> excludedTitles = trip.Reservations.Select(item => item.Title)
            .Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value.Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> excludedProviderIds = trip.Reservations.Select(item => item.ProviderPlaceId).OfType<string>()
            .Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value.Trim()).ToHashSet(StringComparer.Ordinal);
        public bool IsExcludedPlace(Recommendation recommendation) => Excluded.Contains(recommendation.Id.ToString())
            || excludedTitles.Contains(recommendation.Title.Trim())
            || !string.IsNullOrWhiteSpace(recommendation.ProviderPlaceId) && excludedProviderIds.Contains(recommendation.ProviderPlaceId.Trim());
        public void ExcludePlace(Recommendation recommendation)
        {
            var pending = new Queue<Recommendation>();
            pending.Enqueue(recommendation);
            while (pending.TryDequeue(out var place))
            {
                if (!Excluded.Add(place.Id.ToString())) continue;
                if (!string.IsNullOrWhiteSpace(place.Title) && excludedTitles.Add(place.Title.Trim()))
                    foreach (var alias in titles[place.Title.Trim()]) pending.Enqueue(alias);
                if (!string.IsNullOrWhiteSpace(place.ProviderPlaceId) && excludedProviderIds.Add(place.ProviderPlaceId.Trim()))
                    foreach (var alias in providerIds[place.ProviderPlaceId.Trim()]) pending.Enqueue(alias);
            }
        }
        public void ExcludeSavedPlaces()
        {
            var ids = Trip.Reservations.Select(item => item.RecommendationId).OfType<Guid>().ToHashSet();
            foreach (var place in catalog.Where(item => ids.Contains(item.Id) || IsExcludedPlace(item)).ToList()) ExcludePlace(place);
        }
        public IReadOnlyList<string> Cities(DateOnly date) => ResolveDayPlanCities(Trip, segments, date);
        public IReadOnlyList<Recommendation> ForCity(string city)
        {
            if (cities.TryGetValue(city, out var cached)) return cached;
            var matches = catalog.Where(item => item.Neighborhood.Contains(city, StringComparison.OrdinalIgnoreCase)
                || item.Description.Contains(city, StringComparison.OrdinalIgnoreCase)).ToList();
            return cities[city] = matches.Count > 0 ? matches : catalog;
        }
    }
}

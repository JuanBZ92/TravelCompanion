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
        var loaded = new LoadedDayPlanContext(trip, profile, planning, catalog);
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
            var stops = response.Cards.Select(card => new DayPlanStopDto(
                StableStopId(request.OperationId, date, Guid.Parse(card.RecommendationId!)),
                Guid.Parse(card.RecommendationId!), card.PeriodKey!,
                card with { IsPeriodOnly = true, ReservationId = null })).ToList();
            loaded.Excluded.UnionWith(stops.Select(stop => stop.RecommendationId.ToString()));
            var expected = (profile.TravelPace switch
            {
                "relaxed" => new[] { "morning", "midday", "afternoon" },
                "efficient" => new[] { "morning", "morning", "midday", "afternoon", "night" },
                _ => new[] { "morning", "midday", "afternoon", "night" }
            });
            days.Add(new DayPlanDayDto(date, loaded.Cities(date), stops,
                expected.GroupBy(period => period).SelectMany(group => Enumerable.Repeat(group.Key,
                    Math.Max(0, group.Count() - stops.Count(stop => stop.PeriodKey == group.Key)))).ToList()));
        }
        var count = days.Sum(day => day.Stops.Count);
        var english = IsEnglish(locale);
        return new(request.OperationId, trip.Id, trip.PlanRevision, days,
            count == 0
                ? english ? "No suitable new ideas were found. Try different interests or dates."
                    : "No encontramos ideas nuevas compatibles. Probá otros intereses o fechas."
                : english ? $"{count} ideas for {days.Count} days. Choose which ones to add."
                    : $"{count} ideas para {days.Count} días. Elegí cuáles añadir.");
    }

    private static Guid StableStopId(Guid operationId, DateOnly date, Guid recommendationId) =>
        new(SHA256.HashData(Encoding.UTF8.GetBytes($"{operationId:N}:{date:yyyy-MM-dd}:{recommendationId:N}"))[..16]);

    private sealed class LoadedDayPlanContext(Trip trip, TravelPreferenceProfile profile,
        TravelRecommendationPlanningService planning, IReadOnlyList<Recommendation> catalog)
    {
        private readonly IReadOnlyList<BuilderTripSetupSegmentDto> segments = string.IsNullOrWhiteSpace(trip.BuilderSegmentsJson)
            ? [] : JsonSerializer.Deserialize<List<BuilderTripSetupSegmentDto>>(trip.BuilderSegmentsJson) ?? [];
        private readonly Dictionary<string, IReadOnlyList<Recommendation>> cities = new(StringComparer.OrdinalIgnoreCase);
        public Trip Trip { get; } = trip;
        public TravelPreferenceProfile Profile { get; } = profile;
        public TravelRecommendationPlanningService Planning { get; } = planning;
        public HashSet<string> Excluded { get; } = new(StringComparer.OrdinalIgnoreCase);
        public IReadOnlyList<string> Cities(DateOnly date)
        {
            var result = TripCityScope.ForDate(segments, date);
            if (result.Count > 0) return result;
            var existing = Trip.Reservations.Where(item => IsReservationOnDate(item, date)).ToList();
            return [ResolveCity(null, existing, [Trip])];
        }
        public IReadOnlyList<Recommendation> ForCity(string city)
        {
            if (cities.TryGetValue(city, out var cached)) return cached;
            var matches = catalog.Where(item => item.Neighborhood.Contains(city, StringComparison.OrdinalIgnoreCase)
                || item.Description.Contains(city, StringComparison.OrdinalIgnoreCase)).ToList();
            return cities[city] = matches.Count > 0 ? matches : catalog;
        }
    }
}

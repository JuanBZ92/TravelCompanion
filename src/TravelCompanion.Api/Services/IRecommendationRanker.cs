using TravelCompanion.Api.Models;

namespace TravelCompanion.Api.Services;

public interface IRecommendationRanker
{
    IReadOnlyList<ScoredRecommendation> Rank(
        TravelPreferenceProfile profile,
        IReadOnlyList<Reservation> reservations,
        IReadOnlyList<Recommendation> recommendations,
        TravelPlanningContext context);
}

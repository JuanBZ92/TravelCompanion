using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Mobile.Services;

public sealed class PendingItineraryActionStore
{
    public RecommendationDto? Recommendation { get; private set; }
    public bool HasPendingItem => Recommendation is not null;

    public void Set(RecommendationDto recommendation) => Recommendation = recommendation;
    public RecommendationDto? Take()
    {
        var value = Recommendation;
        Recommendation = null;
        return value;
    }
    public void Clear() => Recommendation = null;
}

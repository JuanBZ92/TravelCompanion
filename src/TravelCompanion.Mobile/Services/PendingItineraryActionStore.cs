using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Mobile.Services;

public sealed class PendingItineraryActionStore
{
    public sealed record PendingItineraryAction(RecommendationDto Recommendation, DateOnly? Date, TimeOnly? SuggestedStartTime);

    public PendingItineraryAction? Action { get; private set; }
    public RecommendationDto? Recommendation => Action?.Recommendation;
    public bool HasPendingItem => Action is not null;

    public void Set(RecommendationDto recommendation, DateOnly? date = null, TimeOnly? suggestedStartTime = null) =>
        Action = new(recommendation, date, suggestedStartTime);
    public PendingItineraryAction? TakeAction()
    {
        var value = Action;
        Action = null;
        return value;
    }
    public RecommendationDto? Take()
    {
        return TakeAction()?.Recommendation;
    }
    public void Clear() => Action = null;
}

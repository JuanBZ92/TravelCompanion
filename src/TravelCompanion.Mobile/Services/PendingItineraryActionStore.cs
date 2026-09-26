using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Mobile.Services;

public sealed class PendingItineraryActionStore
{
    public sealed record PendingItineraryAction(RecommendationDto Recommendation, DateOnly? Date, TimeOnly? SuggestedStartTime);
    public sealed record PendingAdaptation(DateOnly Date, string? City, string Reason, int? DelayMinutes);

    public PendingItineraryAction? Action { get; private set; }
    public PendingAdaptation? Adaptation { get; private set; }
    public RecommendationDto? Recommendation => Action?.Recommendation;
    public bool HasPendingItem => Action is not null;

    public void Set(RecommendationDto recommendation, DateOnly? date = null, TimeOnly? suggestedStartTime = null)
    {
        Adaptation = null;
        Action = new(recommendation, date, suggestedStartTime);
    }
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
    public void SetAdaptation(DateOnly date, string? city, string reason, int? delayMinutes)
    {
        Action = null;
        Adaptation = new(date, city, reason, delayMinutes);
    }
    public PendingAdaptation? TakeAdaptation()
    {
        var value = Adaptation;
        Adaptation = null;
        return value;
    }
    public void Clear()
    {
        Action = null;
        Adaptation = null;
    }
}

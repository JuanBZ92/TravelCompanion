using TravelCompanion.Mobile.ViewModels;
using TravelCompanion.Shared;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Mobile.Services;

internal static class DayPlanCardActions
{
    public static GuidedTravelActionDto CreateAlternative(
        TravelChatCardViewModel card,
        IEnumerable<TravelChatCardViewModel> visibleCards,
        string? distance,
        string? budget)
    {
        var drafts = visibleCards.Append(card)
            .Where(item => item.IsDayPlanCard && !item.IsSaved && item.RecommendationId.HasValue
                && item.StartsAt.HasValue && item.PlanningDate == card.PlanningDate)
            .GroupBy(item => item.StartsAt).Select(group => group.Last())
            .Select(item => new DayPlanDraftStopDto(item.RecommendationId!.Value, item.StartsAt!.Value, item.ReservationId))
            .ToList();
        return new(GuidedTravelActions.FullDay, Guid.NewGuid().ToString("N"),
            card.IsSaved ? null : card.RecommendationId?.ToString())
        {
            ReplaceReservationIds = card.IsSaved && card.ReservationId.HasValue ? [card.ReservationId.Value] : [],
            DraftDayStops = drafts,
            DistanceAdjustment = distance,
            BudgetAdjustment = budget
        };
    }
}

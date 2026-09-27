using TravelCompanion.Mobile.ViewModels;
using TravelCompanion.Shared;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Mobile.Services;

public sealed record AssistantProposalRow(
    ScheduleItemDto? SavedItem,
    TravelChatCardViewModel? Suggestion,
    bool IsOngoingStay,
    TimeOnly SortTime)
{
    public string Title => Suggestion?.Title ?? SavedItem?.Title ?? string.Empty;
    public string? Reason => Suggestion?.WhyItFits.FirstOrDefault();
    public bool HasReason => !string.IsNullOrWhiteSpace(Reason);
    public bool IsSuggestion => Suggestion is not null;
    public bool IsSaved => SavedItem is not null || Suggestion?.IsSaved == true;
    public string Status => Text(IsSaved ? "AssistantProposalSaved" : "AssistantProposalIdea");
    public string When => IsOngoingStay ? Text("AssistantProposalStay") : SavedItem is { } saved
        ? saved.HasExactTime ? saved.StartsAt.ToString("HH:mm") : PeriodLabel(saved.EffectivePeriodKey)
        : Suggestion is { } card
            ? card.IsDayPlanCard && card.TimePrecision == ItineraryTimePrecision.Exact
                && card.StartsAt is { } start
                ? start.ToString("HH:mm") : PeriodLabel(card.StartsAt is { } flexibleStart
                    && flexibleStart != TimeOnly.MinValue
                    ? flexibleStart.Hour switch
                    {
                        < 12 => "morning", < 15 => "midday", < 20 => "afternoon", _ => "night"
                    }
                    : null)
            : string.Empty;

    private static string PeriodLabel(string? period) => period switch
    {
        "morning" => Text("AssistantProposalMorning"),
        "midday" => Text("AssistantProposalMidday"),
        "afternoon" => Text("AssistantProposalAfternoon"),
        "night" => Text("AssistantProposalNight"),
        _ => Text("AssistantProposalFlexible")
    };

    private static string Text(string key) => LocalizationResourceManager.Instance[key];
}

public static class AssistantDayProposalBuilder
{
    public static IReadOnlyList<AssistantProposalRow> Build(
        IReadOnlyList<ScheduleItemDto> items,
        IReadOnlyList<TravelChatCardViewModel> cards,
        DateOnly date)
    {
        var rows = items
            .Where(item => item.Date == date || item.Type == ReservationType.Lodging
                && item.Date < date && item.EndsOn >= date)
            .Select(item => new AssistantProposalRow(item, null,
                item.Date < date, item.Date < date ? TimeOnly.MinValue : item.StartsAt))
            .ToList();
        var savedIds = rows.Select(row => row.SavedItem!.Id).ToHashSet();
        var savedRecommendationIds = rows.Select(row => row.SavedItem!.RecommendationId)
            .Where(id => id.HasValue).Select(id => id!.Value).ToHashSet();
        foreach (var card in cards)
        {
            if (card.IsExistingDayStop || !card.RecommendationId.HasValue
                || savedRecommendationIds.Contains(card.RecommendationId.Value)
                || card.ReservationId is { } id && savedIds.Contains(id)) continue;
            rows.Add(new AssistantProposalRow(null, card, false,
                card.StartsAt is { } start && start != TimeOnly.MinValue
                    ? start : TimeOnly.MaxValue));
        }
        return rows.OrderBy(row => row.SortTime)
            .ThenBy(row => row.IsSuggestion ? 1 : 0)
            .ThenBy(row => row.Title, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }
}

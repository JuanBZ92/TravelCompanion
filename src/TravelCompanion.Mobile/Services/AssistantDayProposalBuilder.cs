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
            ? card.TimePrecision == ItineraryTimePrecision.Exact
                && card.StartsAt is { } start && start != TimeOnly.MinValue
                ? start.ToString("HH:mm") : PeriodLabel(card.PeriodKey)
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

    public static IReadOnlyList<AssistantProposalRow> BuildQuickSearch(
        IReadOnlyList<ScheduleItemDto> items,
        IReadOnlyList<TravelChatCardViewModel> cards,
        DateOnly date,
        bool preserveOptionPositions = false)
    {
        var dayItems = items.Where(item => item.Date == date).ToList();
        var results = new List<AssistantProposalRow>();
        var seenRecommendations = new HashSet<Guid>();
        foreach (var card in cards)
        {
            if (card.IsExistingDayStop || card.RecommendationId is not { } recommendationId
                || !seenRecommendations.Add(recommendationId)) continue;

            var saved = dayItems.FirstOrDefault(item => item.RecommendationId == recommendationId
                || card.ReservationId == item.Id);
            results.Add(saved is not null
                ? new AssistantProposalRow(saved, null, false, saved.StartsAt)
                : new AssistantProposalRow(null, card, false, card.StartsAt ?? TimeOnly.MaxValue));
        }

        // A flexible moment is a label, not a time window. Keep the proposed order
        // unless every idea has an exact time that can anchor saved plans.
        // Express options are alternatives to choose from, not consecutive stops in a day plan.
        if (preserveOptionPositions || results.Any(row => GetWindow(row) is null)) return results;

        var anchors = results.Select(GetWindow).Where(window => window.HasValue)
            .Select(window => window.GetValueOrDefault()).ToList();
        var resultSavedIds = results.Where(row => row.SavedItem is not null)
            .Select(row => row.SavedItem!.Id).ToHashSet();
        var rows = new List<AssistantProposalRow>(results);
        if (anchors.Count >= 2)
        {
            foreach (var item in dayItems)
            {
                if (resultSavedIds.Contains(item.Id)) continue;
                var row = new AssistantProposalRow(item, null, false, item.StartsAt);
                var window = GetWindow(row);
                if (window is null) continue;
                if (anchors.Any(anchor => anchor.End < window.Value.Start)
                    && anchors.Any(anchor => anchor.Start > window.Value.End))
                    rows.Add(row);
            }
        }

        return rows.Select((row, index) => (Row: row, Index: index, Window: GetWindow(row)))
            .OrderBy(entry => entry.Window?.Start ?? int.MaxValue)
            .ThenBy(entry => entry.Index)
            .Select(entry => entry.Row)
            .ToList();
    }

    private static TimeWindow? GetWindow(AssistantProposalRow row)
    {
        if (row.IsOngoingStay) return null;
        if (row.SavedItem is { } saved)
        {
            if (!saved.HasExactTime) return null;
            if (saved.Type != ReservationType.Lodging && saved.EndsOn is null
                && saved.EndsAt is { } overnightEnd && overnightEnd < saved.StartsAt) return null;

            var start = Minutes(saved.StartsAt);
            var savedEndMinute = saved.Type == ReservationType.Lodging ? start
                : saved.EndsOn > saved.Date ? 1440
                : saved.EndsAt is { } savedEndsAt && savedEndsAt >= saved.StartsAt
                    ? Minutes(savedEndsAt) : start;
            return new TimeWindow(start, savedEndMinute);
        }

        var card = row.Suggestion;
        if (card?.StartsAt is not { } startsAt || startsAt == TimeOnly.MinValue) return null;
        if (card.TimePrecision != ItineraryTimePrecision.Exact) return null;
        if (card.EndsAt is { } suggestionEndsAt && suggestionEndsAt < startsAt) return null;
        return new TimeWindow(Minutes(startsAt),
            card.EndsAt is { } suggestionEnd ? Minutes(suggestionEnd) : Minutes(startsAt));
    }

    private static int Minutes(TimeOnly time) => time.Hour * 60 + time.Minute;

    private readonly record struct TimeWindow(int Start, int End);
}

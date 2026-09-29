using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Mobile.Services;

public static class DayConflictCursor
{
    public static string Key(DayReviewIssueDto issue) => issue.Kind + ":" + string.Join(",", issue.ItemIds.Order());
    public static int Select(IReadOnlyList<DayReviewIssueDto> issues, string? previousKey, int previousIndex)
    {
        for (var i = 0; i < issues.Count; i++) if (Key(issues[i]) == previousKey) return i;
        return Math.Clamp(previousIndex, 0, Math.Max(0, issues.Count - 1));
    }
}

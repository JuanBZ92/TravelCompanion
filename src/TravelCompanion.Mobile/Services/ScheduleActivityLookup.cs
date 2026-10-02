using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Mobile.Services;

public static class ScheduleActivityLookup
{
    public static IReadOnlyList<ScheduleItemDto> SearchJournalActivities(IEnumerable<ScheduleItemDto> items,
        DateOnly? date = null, string? query = null)
    {
        var search = query?.Trim() ?? "";
        return items.Where(item => (!date.HasValue || item.Date == date.Value)
                && $"{item.Title} {item.City}".Contains(search, StringComparison.CurrentCultureIgnoreCase))
            .OrderBy(item => item.Date).ThenBy(item => item.StartsAt).ThenBy(item => item.Title)
            .ThenBy(item => item.Id).ToArray();
    }

    // Editorial ownership controls editing, not access to the activity's detail and personal journal.
    public static ScheduleItemDto? FindAssignedItem(IEnumerable<ScheduleItemDto> items,
        DateOnly date, string periodKey, Guid recommendationId) => items.FirstOrDefault(item =>
            item.Date == date && item.RecommendationId == recommendationId
            && string.Equals(item.EffectivePeriodKey, periodKey, StringComparison.OrdinalIgnoreCase));
}

using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Mobile.Services;

public static class ScheduleActivityLookup
{
    // Editorial ownership controls editing, not access to the activity's detail and personal journal.
    public static ScheduleItemDto? FindAssignedItem(IEnumerable<ScheduleItemDto> items,
        DateOnly date, string periodKey, Guid recommendationId) => items.FirstOrDefault(item =>
            item.Date == date && item.RecommendationId == recommendationId
            && string.Equals(item.EffectivePeriodKey, periodKey, StringComparison.OrdinalIgnoreCase));
}

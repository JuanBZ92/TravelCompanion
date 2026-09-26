using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Shared;

public static class UpcomingActivitySelector
{
    public static DateTime GetTripNow(string? timeZoneId, DateTimeOffset instant)
    {
        try
        {
            return TimeZoneInfo.ConvertTime(instant, TimeZoneInfo.FindSystemTimeZoneById(
                string.IsNullOrWhiteSpace(timeZoneId) ? "Asia/Tokyo" : timeZoneId)).DateTime;
        }
        catch (TimeZoneNotFoundException) { return TimeZoneInfo.ConvertTime(instant, TimeZoneInfo.FindSystemTimeZoneById("Asia/Tokyo")).DateTime; }
        catch (InvalidTimeZoneException) { return TimeZoneInfo.ConvertTime(instant, TimeZoneInfo.FindSystemTimeZoneById("Asia/Tokyo")).DateTime; }
    }

    public static ScheduleItemDto? Select(IReadOnlyList<ScheduleItemDto> items, DateOnly selectedDate,
        string? timeZoneId, DateTimeOffset instant)
    {
        var now = GetTripNow(timeZoneId, instant);
        if (selectedDate != DateOnly.FromDateTime(now)) return null;

        var timed = items
            .Where(item => item.HasExactTime &&
                (item.Date == selectedDate || item.Date < selectedDate && item.EndsOn >= selectedDate))
            .Where(item => End(item) >= now)
            .OrderBy(item => Start(item) <= now ? 0 : 1)
            .ThenBy(item => item.IsConfirmedReservation
                || item.Flexibility is ItineraryFlexibility.FixedByTraveler or ItineraryFlexibility.ConfirmedReservation
                || item.Type == ReservationType.Flight ? 0 : 1)
            .ThenBy(item => Start(item))
            .FirstOrDefault();
        if (timed is not null) return timed;

        return items.Where(item => item.Date == selectedDate && !item.HasExactTime
                && now.TimeOfDay < PeriodEnd(item).ToTimeSpan())
            .OrderBy(item => item.SortOrder).FirstOrDefault();
    }

    private static TimeOnly PeriodEnd(ScheduleItemDto item) => item.EffectivePeriodKey switch
    {
        "morning" => new TimeOnly(12, 0),
        "midday" => new TimeOnly(15, 0),
        "afternoon" => new TimeOnly(20, 0),
        _ => TimeOnly.MaxValue
    };

    private static DateTime Start(ScheduleItemDto item) => item.Date.ToDateTime(item.StartsAt);
    private static DateTime End(ScheduleItemDto item) => (item.EndsOn ?? item.Date)
        .ToDateTime(item.EndsAt ?? item.StartsAt.AddMinutes(item.DurationMinutes ?? 60));
}

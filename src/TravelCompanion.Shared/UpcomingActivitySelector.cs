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

    public static bool IsInProgress(ScheduleItemDto item, string? timeZoneId, DateTimeOffset instant)
    {
        var now = GetTripNow(timeZoneId, instant);
        return Start(item) <= now && End(item) >= now;
    }

    public static ScheduleItemDto? Select(IReadOnlyList<ScheduleItemDto> items, DateOnly selectedDate,
        string? timeZoneId, DateTimeOffset instant)
    {
        var now = GetTripNow(timeZoneId, instant);
        if (selectedDate != DateOnly.FromDateTime(now)) return null;

        return items
            .Where(item => item.HasExactTime &&
                (item.Date == selectedDate || item.Date < selectedDate && item.EndsOn >= selectedDate))
            .Where(item => End(item) >= now)
            .OrderBy(item => Start(item) <= now ? 0 : 1)
            .ThenBy(item => Start(item))
            .ThenBy(item => item.IsConfirmedReservation
                || item.Flexibility is ItineraryFlexibility.FixedByTraveler or ItineraryFlexibility.ConfirmedReservation
                || item.Type == ReservationType.Flight ? 0 : 1)
            .ThenBy(item => item.Id)
            .FirstOrDefault();
    }

    private static DateTime Start(ScheduleItemDto item) => item.Date.ToDateTime(item.StartsAt);
    private static DateTime End(ScheduleItemDto item) => item.Type == ReservationType.Lodging
        ? Start(item).AddHours(1) // The checkout date describes the stay, not the check-in activity.
        : (item.EndsOn ?? item.Date).ToDateTime(item.EndsAt ?? item.StartsAt.AddMinutes(item.DurationMinutes ?? 60));
}

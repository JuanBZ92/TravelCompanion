using TravelCompanion.Shared;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Mobile.Services;

public static class AssistantFreeTimeWindow
{
    // Express searches start now, or immediately after a commitment already in progress.
    // The next fixed commitment and midnight still bound the available interval.
    public static TravelTimeWindow? ResolveSoon(TripScheduleDto schedule, int minutes, DateTimeOffset now)
    {
        try
        {
            var zone = TimeZoneInfo.FindSystemTimeZoneById(schedule.TimeZoneId);
            var localNow = TimeZoneInfo.ConvertTime(now, zone).DateTime;
            var date = DateOnly.FromDateTime(localNow);
            if (date < schedule.StartsOn || date > schedule.EndsOn || minutes < 1) return null;
            var start = new DateTime(localNow.Year, localNow.Month, localNow.Day, localNow.Hour,
                localNow.Minute, 0, DateTimeKind.Unspecified);
            if (localNow > start) start = start.AddMinutes(1);
            var commitments = Commitments(schedule, zone);
            if (commitments is null) return null;
            foreach (var commitment in commitments.OrderBy(item => item.StartsAtLocal))
                if (commitment.StartsAtLocal <= start && commitment.EndsAtLocal > start)
                    start = commitment.EndsAtLocal;
            if (DateOnly.FromDateTime(start) != date) return null;
            var end = start.AddMinutes(minutes);
            if (DateOnly.FromDateTime(end) != date) end = date.ToDateTime(new TimeOnly(23, 59));
            return TravelTimeWindowPolicy.Resolve(date, start, end, commitments, localNow);
        }
        catch (TimeZoneNotFoundException) { return null; }
        catch (InvalidTimeZoneException) { return null; }
    }

    public static ScheduleItemDto? NextLocatedPlan(TripScheduleDto schedule, DateTimeOffset now)
    {
        try
        {
            var zone = TimeZoneInfo.FindSystemTimeZoneById(schedule.TimeZoneId);
            var localNow = TimeZoneInfo.ConvertTime(now, zone).DateTime;
            var date = DateOnly.FromDateTime(localNow);
            var future = new List<(ScheduleItemDto Item, DateTime Start)>();
            foreach (var item in schedule.Items.Where(item => item.HasExactTime
                         && item.Latitude is >= -90 and <= 90 && item.Longitude is >= -180 and <= 180
                         && (item.Latitude != 0 || item.Longitude != 0)))
            {
                if (!TravelTimeWindowPolicy.TryConvertToTripTime(item.Date.ToDateTime(item.StartsAt),
                        item.TimeZoneId, zone, out var start)) continue;
                if (start > localNow && DateOnly.FromDateTime(start) == date) future.Add((item, start));
            }
            return future.OrderBy(item => item.Start)
                .ThenBy(item => item.Item.IsConfirmedReservation
                    || item.Item.Flexibility is ItineraryFlexibility.ConfirmedReservation or ItineraryFlexibility.FixedByTraveler
                    || item.Item.Type == ReservationType.Flight ? 0 : 1)
                .ThenBy(item => item.Item.Id).Select(item => item.Item).FirstOrDefault();
        }
        catch (TimeZoneNotFoundException) { return null; }
        catch (InvalidTimeZoneException) { return null; }
    }

    public static DateTime? TripStart(TripScheduleDto schedule, ScheduleItemDto item)
    {
        try
        {
            var zone = TimeZoneInfo.FindSystemTimeZoneById(schedule.TimeZoneId);
            return TravelTimeWindowPolicy.TryConvertToTripTime(item.Date.ToDateTime(item.StartsAt),
                item.TimeZoneId, zone, out var start) ? start : null;
        }
        catch (TimeZoneNotFoundException) { return null; }
        catch (InvalidTimeZoneException) { return null; }
    }

    public static TravelTimeWindow? Resolve(TripScheduleDto schedule, DateOnly date, TimeSpan startsAt,
        int minutes, DateTimeOffset now)
    {
        if (startsAt < TimeSpan.Zero || startsAt >= TimeSpan.FromDays(1) || minutes < 1) return null;
        try
        {
            var zone = TimeZoneInfo.FindSystemTimeZoneById(schedule.TimeZoneId);
            var start = date.ToDateTime(TimeOnly.MinValue).Add(startsAt);
            var end = start.AddMinutes(minutes);
            if (DateOnly.FromDateTime(end) != date) end = date.ToDateTime(new TimeOnly(23, 59));
            var commitments = Commitments(schedule, zone);
            if (commitments is null) return null;
            return TravelTimeWindowPolicy.Resolve(date, start, end, commitments,
                TimeZoneInfo.ConvertTime(now, zone).DateTime);
        }
        catch (TimeZoneNotFoundException) { return null; }
        catch (InvalidTimeZoneException) { return null; }
    }

    private static List<TravelFixedCommitment>? Commitments(TripScheduleDto schedule, TimeZoneInfo zone)
    {
        var commitments = new List<TravelFixedCommitment>();
        foreach (var item in schedule.Items.Where(item => TravelTimeWindowPolicy.IsFixedCommitment(
                     item.Type, item.PlanningKind, item.TimePrecision, item.Flexibility, item.Owner)))
        {
            var localStart = item.Date.ToDateTime(item.StartsAt);
            var localEnd = TravelTimeWindowPolicy.CommitmentEnd(item.Type, item.Date, item.StartsAt,
                item.EndsOn, item.EndsAt, item.DurationMinutes);
            if (!TravelTimeWindowPolicy.TryConvertToTripTime(localStart, item.TimeZoneId, zone, out var tripStart)
                || !TravelTimeWindowPolicy.TryConvertToTripTime(localEnd, item.TimeZoneId, zone, out var tripEnd)) return null;
            commitments.Add(new(tripStart, tripEnd));
        }
        return commitments;
    }
}

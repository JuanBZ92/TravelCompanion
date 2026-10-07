using TravelCompanion.Shared;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Mobile.Services;

public static class AssistantFreeTimeWindow
{
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
            return TravelTimeWindowPolicy.Resolve(date, start, end, commitments,
                TimeZoneInfo.ConvertTime(now, zone).DateTime);
        }
        catch (TimeZoneNotFoundException) { return null; }
        catch (InvalidTimeZoneException) { return null; }
    }
}

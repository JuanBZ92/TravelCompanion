namespace TravelCompanion.Shared;

public sealed record TravelFixedCommitment(DateTime StartsAtLocal, DateTime EndsAtLocal);
public sealed record TravelTimeWindow(DateTime StartsAtLocal, DateTime EndsAtLocal, DateTime? NextFixedAtLocal)
{
    public int AvailableMinutes => (int)(EndsAtLocal - StartsAtLocal).TotalMinutes;
}

// Inputs are wall-clock times in the trip's zone, never device-local or UTC.
public static class TravelTimeWindowPolicy
{
    public static DateTime CommitmentEnd(ReservationType type, DateOnly date, TimeOnly startsAt,
        DateOnly? endsOn, TimeOnly? endsAt, int? durationMinutes)
    {
        var start = date.ToDateTime(startsAt);
        // A hotel stay only blocks check-in, rather than every hour of the stay.
        if (type == ReservationType.Lodging) return start.AddHours(1);
        if (!endsAt.HasValue) return start.AddMinutes(Math.Max(1, durationMinutes ?? 60));
        var end = (endsOn ?? date).ToDateTime(endsAt.Value);
        return end <= start && !endsOn.HasValue ? end.AddDays(1) : end;
    }

    public static bool IsFixedCommitment(ReservationType type, ScheduleItemKind kind,
        ItineraryTimePrecision precision, ItineraryFlexibility flexibility, ItineraryItemOwner owner) =>
        precision == ItineraryTimePrecision.Exact && (kind != ScheduleItemKind.Recommendation
            || owner == ItineraryItemOwner.Yuku || type is ReservationType.Flight or ReservationType.Lodging
            || flexibility is ItineraryFlexibility.FixedByTraveler or ItineraryFlexibility.ConfirmedReservation);

    public static TravelTimeWindow? Resolve(DateOnly date, DateTime startsAtLocal, DateTime endsAtLocal,
        IEnumerable<TravelFixedCommitment> commitments, DateTime nowAtTrip)
    {
        if (startsAtLocal.Kind != DateTimeKind.Unspecified || endsAtLocal.Kind != DateTimeKind.Unspecified
            || DateOnly.FromDateTime(startsAtLocal) != date || DateOnly.FromDateTime(endsAtLocal) != date
            || endsAtLocal <= startsAtLocal || date < DateOnly.FromDateTime(nowAtTrip)) return null;
        if (date == DateOnly.FromDateTime(nowAtTrip) && startsAtLocal < nowAtTrip)
        {
            var roundedNow = new DateTime(nowAtTrip.Year, nowAtTrip.Month, nowAtTrip.Day,
                nowAtTrip.Hour, nowAtTrip.Minute, 0, DateTimeKind.Unspecified);
            startsAtLocal = nowAtTrip > roundedNow ? roundedNow.AddMinutes(1) : roundedNow;
        }
        if (endsAtLocal <= startsAtLocal) return null;
        var relevant = commitments.Where(item => item.EndsAtLocal > startsAtLocal).ToArray();
        if (relevant.Any(item => item.StartsAtLocal <= startsAtLocal && item.EndsAtLocal > startsAtLocal)) return null;
        var next = relevant.Where(item => item.StartsAtLocal > startsAtLocal)
            .OrderBy(item => item.StartsAtLocal).FirstOrDefault();
        if (next is not null && next.StartsAtLocal < endsAtLocal) endsAtLocal = next.StartsAtLocal;
        return endsAtLocal - startsAtLocal >= TimeSpan.FromMinutes(1)
            ? new(startsAtLocal, endsAtLocal, next?.StartsAtLocal) : null;
    }

    public static bool TryConvertToTripTime(DateTime local, string? sourceZoneId, TimeZoneInfo tripZone,
        out DateTime tripLocal)
    {
        tripLocal = default;
        try
        {
            var source = string.IsNullOrWhiteSpace(sourceZoneId) ? tripZone : TimeZoneInfo.FindSystemTimeZoneById(sourceZoneId);
            local = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
            if (source.IsInvalidTime(local) || source.IsAmbiguousTime(local)) return false;
            tripLocal = TimeZoneInfo.ConvertTimeFromUtc(TimeZoneInfo.ConvertTimeToUtc(local, source), tripZone);
            tripLocal = DateTime.SpecifyKind(tripLocal, DateTimeKind.Unspecified);
            return true;
        }
        catch (TimeZoneNotFoundException) { return false; }
        catch (InvalidTimeZoneException) { return false; }
    }
}

namespace TravelCompanion.Mobile.Services;

internal static class ReservationLocalTime
{
    public static DateTime? ConvertStart(DateOnly date, TimeOnly time, string? sourceZoneId, TimeZoneInfo phoneZone)
    {
        if (string.IsNullOrWhiteSpace(sourceZoneId)) return null;
        try
        {
            var source = TimeZoneInfo.FindSystemTimeZoneById(sourceZoneId);
            var start = date.ToDateTime(time, DateTimeKind.Unspecified);
            if (source.IsInvalidTime(start) || source.IsAmbiguousTime(start)) return null;
            return TimeZoneInfo.ConvertTimeFromUtc(TimeZoneInfo.ConvertTimeToUtc(start, source), phoneZone);
        }
        catch (TimeZoneNotFoundException) { return null; }
        catch (InvalidTimeZoneException) { return null; }
    }
}

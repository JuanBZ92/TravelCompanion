using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Shared;

public static class ItineraryRoutePolicy
{
    public static bool HasCoordinatesOrPlace(ScheduleItemDto item) =>
        !string.IsNullOrWhiteSpace(item.ProviderPlaceId) || item.Latitude.HasValue && item.Longitude.HasValue;

    public static bool TryBounds(ScheduleItemDto item, string tripZoneId,
        out DateTimeOffset start, out DateTimeOffset end)
    {
        start = end = default;
        if (!item.HasExactTime) return false;
        try
        {
            var zone = TimeZoneInfo.FindSystemTimeZoneById(item.TimeZoneId ?? tripZoneId);
            var localStart = item.Date.ToDateTime(item.StartsAt, DateTimeKind.Unspecified);
            var localEnd = TravelTimeWindowPolicy.CommitmentEnd(item.Type, item.Date, item.StartsAt,
                item.EndsOn, item.EndsAt, item.DurationMinutes);
            // Ambiguous wall times cannot support a reliable departure estimate.
            if (zone.IsInvalidTime(localStart) || zone.IsAmbiguousTime(localStart)
                || zone.IsInvalidTime(localEnd) || zone.IsAmbiguousTime(localEnd)) return false;
            start = new(TimeZoneInfo.ConvertTimeToUtc(localStart, zone));
            end = new(TimeZoneInfo.ConvertTimeToUtc(localEnd, zone));
            return end >= start;
        }
        catch (TimeZoneNotFoundException) { return false; }
        catch (InvalidTimeZoneException) { return false; }
        catch (ArgumentException) { return false; }
    }

    public static ScheduleItemDto? Previous(IEnumerable<ScheduleItemDto> items,
        ScheduleItemDto destination, string tripZoneId)
    {
        if (!TryBounds(destination, tripZoneId, out var target, out _)) return null;
        return items.Where(item => item.Id != destination.Id && item.Type != ReservationType.Lodging)
            .Select(item => (Item: item, Valid: TryBounds(item, tripZoneId, out var start, out var end), Start: start, End: end))
            .Where(value => value.Valid && value.Start < target && value.End <= target && value.Start >= target.AddHours(-24))
            .OrderByDescending(value => value.Start).ThenBy(value => value.Item.Id)
            .Select(value => value.Item).FirstOrDefault();
    }
}

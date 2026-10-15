using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Shared.Tests;

public sealed class ItineraryRoutePolicyTests
{
    [Fact]
    public void Previous_is_chronological_stable_and_excludes_flexible_and_overlapping_items()
    {
        var target = Item(new(2026, 10, 20), new(12, 0));
        var earlier = Item(target.Date, new(9, 0));
        var previous = Item(target.Date, new(10, 0));
        var overlapping = Item(target.Date, new(11, 30)) with { DurationMinutes = 90 };
        var flexible = Item(target.Date, new(11, 0)) with { TimePrecision = ItineraryTimePrecision.PeriodOnly };
        Assert.Equal(previous, ItineraryRoutePolicy.Previous([overlapping, earlier, flexible, previous], target, "Asia/Tokyo"));
        var tie = previous with { Id = Guid.Empty };
        Assert.Equal(tie, ItineraryRoutePolicy.Previous([previous, tie], target, "Asia/Tokyo"));
    }

    [Fact]
    public void Previous_compares_actual_instants_across_zones_and_midnight()
    {
        var target = Item(new(2026, 10, 21), new(2, 0));
        var flight = Item(new(2026, 10, 20), new(23, 0)) with { Type = ReservationType.Flight, EndsAt = new(1, 0) };
        Assert.Equal(flight, ItineraryRoutePolicy.Previous([flight], target, "Asia/Tokyo"));
        var futureInUtc = Item(new(2026, 10, 20), new(23, 0)) with { TimeZoneId = "Europe/Madrid" };
        Assert.Null(ItineraryRoutePolicy.Previous([futureInUtc], target, "Asia/Tokyo"));
        Assert.True(ItineraryRoutePolicy.TryBounds(flight, "Asia/Tokyo", out var start, out var end));
        Assert.Equal(TimeSpan.FromHours(2), end - start);
    }

    [Fact]
    public void Hotel_bounds_cover_checkin_and_dst_ambiguity_is_not_silently_guessed()
    {
        var hotel = Item(new(2026, 10, 20), new(15, 0)) with { Type = ReservationType.Lodging, EndsOn = new(2026, 10, 24) };
        Assert.True(ItineraryRoutePolicy.TryBounds(hotel, "Asia/Tokyo", out var start, out var end));
        Assert.Equal(TimeSpan.FromHours(1), end - start);
        Assert.False(ItineraryRoutePolicy.TryBounds(Item(new(2026, 10, 25), new(2, 30)), "Europe/Madrid", out _, out _));
        Assert.False(ItineraryRoutePolicy.TryBounds(Item(new(2026, 3, 29), new(2, 30)), "Europe/Madrid", out _, out _));
    }

    private static ScheduleItemDto Item(DateOnly date, TimeOnly starts) => new(Guid.NewGuid(), null,
        ReservationType.Event, date, starts, null, null, "Synthetic activity", "Tokyo", "Place", "Address", "", "",
        null, null, null, null, null, null);
}

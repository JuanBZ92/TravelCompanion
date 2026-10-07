using TravelCompanion.Mobile.Services;
using TravelCompanion.Shared;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Mobile.Tests;

public sealed class AssistantFreeTimeWindowTests
{
    private static readonly DateOnly Day = new(2026, 10, 8);
    private static readonly DateTimeOffset BeforeTrip = new(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);
    private static ScheduleItemDto Item(TimeOnly start, TimeOnly end) => new(Guid.NewGuid(), null,
        ReservationType.Event, Day, start, null, end, "Booking", "Tokyo", "Place", "Address", "", "",
        null, null, null, null, null, null);
    private static TripScheduleDto Schedule(params ScheduleItemDto[] items) => new(Guid.NewGuid(), "Traveler",
        "Japan", Day, Day.AddDays(30), items) { TimeZoneId = "Asia/Tokyo" };

    [Fact]
    public void Window_ends_at_next_fixed_booking_and_keeps_its_time_visible()
    {
        var result = AssistantFreeTimeWindow.Resolve(Schedule(Item(new(11, 0), new(12, 0))), Day,
            TimeSpan.FromHours(10), 120, BeforeTrip);
        Assert.Equal(60, result!.AvailableMinutes);
        Assert.Equal(Day.ToDateTime(new(11, 0)), result.NextFixedAtLocal);
    }

    [Fact]
    public void Flexible_placeholder_does_not_block_free_time_but_confirmed_booking_does()
    {
        var flexible = Item(new(10, 0), new(11, 0)) with
        {
            PlanningKind = ScheduleItemKind.Recommendation, Owner = ItineraryItemOwner.Traveler,
            TimePrecision = ItineraryTimePrecision.PeriodOnly
        };
        Assert.NotNull(AssistantFreeTimeWindow.Resolve(Schedule(flexible), Day, TimeSpan.FromHours(10), 60, BeforeTrip));
        var fixedItem = flexible with { TimePrecision = ItineraryTimePrecision.Exact,
            Flexibility = ItineraryFlexibility.ConfirmedReservation };
        Assert.Null(AssistantFreeTimeWindow.Resolve(Schedule(fixedItem), Day, TimeSpan.FromHours(10), 60, BeforeTrip));
    }

    [Fact]
    public void Overnight_commitment_blocks_the_following_morning()
    {
        var overnight = Item(new(23, 0), new(10, 30)) with { Date = Day.AddDays(-1) };
        Assert.Null(AssistantFreeTimeWindow.Resolve(Schedule(overnight), Day, TimeSpan.FromHours(10), 60, BeforeTrip));
    }

    [Fact]
    public void Hotel_stay_blocks_check_in_only_and_not_the_entire_stay()
    {
        var hotel = Item(new(15, 0), new(10, 0)) with { Type = ReservationType.Lodging,
            Date = Day.AddDays(-1), EndsOn = Day.AddDays(3) };
        Assert.NotNull(AssistantFreeTimeWindow.Resolve(Schedule(hotel), Day, TimeSpan.FromHours(10), 60, BeforeTrip));
    }

    [Fact]
    public void Uses_trip_clock_and_converts_booking_source_zone()
    {
        var utcBooking = Item(new(2, 0), new(3, 0)) with { TimeZoneId = "UTC" };
        var result = AssistantFreeTimeWindow.Resolve(Schedule(utcBooking), Day, TimeSpan.FromHours(10), 120, BeforeTrip);
        Assert.Equal(60, result!.AvailableMinutes);
        // It is already 10:30 in Tokyo although the device UTC date is the same.
        var today = AssistantFreeTimeWindow.Resolve(Schedule(), Day, TimeSpan.FromHours(10), 60,
            new(2026, 10, 8, 1, 30, 0, TimeSpan.Zero));
        Assert.Equal(Day.ToDateTime(new(10, 30)), today!.StartsAtLocal);
        Assert.Equal(30, today.AvailableMinutes);
    }

    [Theory]
    [InlineData("bad-zone")]
    [InlineData("expired")]
    public void Invalid_zone_or_expired_window_is_not_submittable(string reason)
    {
        var schedule = reason == "bad-zone" ? Schedule() with { TimeZoneId = "No/SuchZone" } : Schedule();
        var now = reason == "expired" ? new DateTimeOffset(2026, 10, 9, 0, 0, 0, TimeSpan.Zero) : BeforeTrip;
        Assert.Null(AssistantFreeTimeWindow.Resolve(schedule, Day, TimeSpan.FromHours(10), 60, now));
    }
}

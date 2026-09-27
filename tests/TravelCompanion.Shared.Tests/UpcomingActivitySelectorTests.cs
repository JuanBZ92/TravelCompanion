using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Shared.Tests;

public sealed class UpcomingActivitySelectorTests
{
    private static readonly DateOnly Day = new(2026, 9, 27);

    [Fact]
    public void Uses_trip_date_at_utc_midnight_and_ignores_other_selected_days()
    {
        var instant = new DateTimeOffset(2026, 9, 26, 15, 30, 0, TimeSpan.Zero);
        var item = Item(Day, new TimeOnly(9, 0));

        Assert.Equal(Day, DateOnly.FromDateTime(UpcomingActivitySelector.GetTripNow("Asia/Tokyo", instant)));
        Assert.Equal(item, UpcomingActivitySelector.Select([item], Day, "Asia/Tokyo", instant));
        Assert.Null(UpcomingActivitySelector.Select([item], Day.AddDays(1), "Asia/Tokyo", instant));
    }

    [Fact]
    public void Keeps_overnight_flight_current_after_midnight()
    {
        var flight = Item(Day.AddDays(-1), new TimeOnly(23, 30)) with
        {
            EndsOn = Day, EndsAt = new TimeOnly(2, 0), Type = ReservationType.Flight
        };
        var instant = new DateTimeOffset(2026, 9, 26, 16, 0, 0, TimeSpan.Zero);

        Assert.Equal(flight, UpcomingActivitySelector.Select([flight], Day, "Asia/Tokyo", instant));
    }

    [Fact]
    public void Prefers_fixed_booking_to_flexible_plan_and_shows_quiet_end()
    {
        var flexible = Item(Day, new TimeOnly(8, 0)) with { TimePrecision = ItineraryTimePrecision.PeriodOnly };
        var booking = Item(Day, new TimeOnly(10, 0)) with { Flexibility = ItineraryFlexibility.ConfirmedReservation };
        var morning = new DateTimeOffset(2026, 9, 26, 23, 0, 0, TimeSpan.Zero);
        var night = new DateTimeOffset(2026, 9, 27, 12, 0, 0, TimeSpan.Zero);

        Assert.Equal(booking, UpcomingActivitySelector.Select([flexible, booking], Day, "Asia/Tokyo", morning));
        Assert.Null(UpcomingActivitySelector.Select([flexible, booking], Day, "Asia/Tokyo", night));
        Assert.Null(UpcomingActivitySelector.Select([booking], Day, "Asia/Tokyo", night));
    }

    [Fact]
    public void Hotel_checkout_does_not_hide_later_activity_or_next_day()
    {
        var hotel = Item(Day, new TimeOnly(15, 0)) with
        {
            Type = ReservationType.Lodging,
            PlanningKind = ScheduleItemKind.ConfirmedReservation,
            EndsOn = Day.AddDays(5),
            EndsAt = new TimeOnly(11, 0)
        };
        var plan = Item(Day, new TimeOnly(17, 0)) with { EndsAt = new TimeOnly(18, 0) };

        Assert.Equal(hotel, UpcomingActivitySelector.Select([hotel, plan], Day, "Asia/Tokyo",
            new DateTimeOffset(2026, 9, 27, 6, 30, 0, TimeSpan.Zero)));
        Assert.Equal(plan, UpcomingActivitySelector.Select([hotel, plan], Day, "Asia/Tokyo",
            new DateTimeOffset(2026, 9, 27, 7, 5, 0, TimeSpan.Zero)));
        Assert.Equal(plan, UpcomingActivitySelector.Select([hotel, plan], Day, "Asia/Tokyo",
            new DateTimeOffset(2026, 9, 27, 8, 0, 0, TimeSpan.Zero)));
        Assert.Null(UpcomingActivitySelector.Select([hotel], Day.AddDays(1), "Asia/Tokyo",
            new DateTimeOffset(2026, 9, 28, 1, 0, 0, TimeSpan.Zero)));
    }

    private static ScheduleItemDto Item(DateOnly date, TimeOnly time) => new(
        Guid.NewGuid(), null, ReservationType.Event, date, time, null, null, "Plan", "Tokyo",
        "Lugar", "Dirección", "", "", null, null, null, null, null, null);
}

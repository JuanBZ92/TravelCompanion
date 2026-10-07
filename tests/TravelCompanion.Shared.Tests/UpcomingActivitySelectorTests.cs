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
        Assert.Null(UpcomingActivitySelector.Select([flexible], Day, "Asia/Tokyo", morning));
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

    [Fact]
    public void Earlier_exact_flexible_plan_precedes_later_confirmed_booking()
    {
        var flexible = Item(Day, new TimeOnly(12, 0));
        var booking = Item(Day, new TimeOnly(18, 0)) with { PlanningKind = ScheduleItemKind.ConfirmedReservation };
        var morning = new DateTimeOffset(2026, 9, 26, 23, 0, 0, TimeSpan.Zero);

        Assert.Equal(flexible, UpcomingActivitySelector.Select([booking, flexible], Day, "Asia/Tokyo", morning));
    }

    [Theory]
    [InlineData(ScheduleItemKind.ConfirmedReservation, ItineraryFlexibility.Flexible, ReservationType.Event)]
    [InlineData(ScheduleItemKind.ManualEvent, ItineraryFlexibility.FixedByTraveler, ReservationType.Event)]
    [InlineData(ScheduleItemKind.ManualEvent, ItineraryFlexibility.Flexible, ReservationType.Flight)]
    public void Booking_fixed_plan_or_flight_wins_only_when_start_times_tie(
        ScheduleItemKind kind, ItineraryFlexibility flexibility, ReservationType type)
    {
        var flexible = Item(Day, new TimeOnly(12, 0)) with { Id = Guid.Parse("00000000-0000-0000-0000-000000000001") };
        var priority = Item(Day, new TimeOnly(12, 0)) with
        {
            Id = Guid.Parse("00000000-0000-0000-0000-000000000002"),
            PlanningKind = kind, Flexibility = flexibility, Type = type
        };
        var morning = new DateTimeOffset(2026, 9, 26, 23, 0, 0, TimeSpan.Zero);

        Assert.Equal(priority, UpcomingActivitySelector.Select([flexible, priority], Day, "Asia/Tokyo", morning));
        Assert.Equal(priority, UpcomingActivitySelector.Select([priority, flexible], Day, "Asia/Tokyo", morning));
    }

    [Fact]
    public void Equal_time_and_priority_use_stable_id_regardless_of_input_order()
    {
        var first = Item(Day, new TimeOnly(12, 0)) with { Id = Guid.Parse("00000000-0000-0000-0000-000000000001") };
        var second = first with { Id = Guid.Parse("00000000-0000-0000-0000-000000000002") };
        var morning = new DateTimeOffset(2026, 9, 26, 23, 0, 0, TimeSpan.Zero);

        Assert.Equal(first, UpcomingActivitySelector.Select([second, first], Day, "Asia/Tokyo", morning));
        Assert.Equal(first, UpcomingActivitySelector.Select([first, second], Day, "Asia/Tokyo", morning));
    }

    [Fact]
    public void In_progress_precedes_future_and_uses_earliest_start_within_its_group()
    {
        var ongoing = Item(Day, new TimeOnly(10, 0)) with { EndsAt = new TimeOnly(11, 0) };
        var laterOngoingBooking = Item(Day, new TimeOnly(10, 15)) with
        {
            EndsAt = new TimeOnly(11, 0), PlanningKind = ScheduleItemKind.ConfirmedReservation
        };
        var futureFlight = Item(Day, new TimeOnly(10, 45)) with { Type = ReservationType.Flight };
        var instant = new DateTimeOffset(2026, 9, 27, 1, 30, 0, TimeSpan.Zero);

        Assert.Equal(ongoing, UpcomingActivitySelector.Select(
            [futureFlight, laterOngoingBooking, ongoing], Day, "Asia/Tokyo", instant));
    }

    [Fact]
    public void In_progress_changes_at_start_and_after_the_inclusive_duration_end()
    {
        var item = Item(Day, new TimeOnly(12, 0)) with { DurationMinutes = 90 };
        var start = new DateTimeOffset(2026, 9, 27, 3, 0, 0, TimeSpan.Zero);

        Assert.False(UpcomingActivitySelector.IsInProgress(item, "Asia/Tokyo", start.AddTicks(-1)));
        Assert.True(UpcomingActivitySelector.IsInProgress(item, "Asia/Tokyo", start));
        Assert.True(UpcomingActivitySelector.IsInProgress(item, "Asia/Tokyo", start.AddMinutes(30)));
        Assert.True(UpcomingActivitySelector.IsInProgress(item, "Asia/Tokyo", start.AddMinutes(90)));
        Assert.False(UpcomingActivitySelector.IsInProgress(item, "Asia/Tokyo", start.AddMinutes(90).AddTicks(1)));
    }

    [Fact]
    public void In_progress_preserves_overnight_flight_and_one_hour_hotel_check_in()
    {
        var flight = Item(Day.AddDays(-1), new TimeOnly(23, 30)) with
        {
            EndsOn = Day, EndsAt = new TimeOnly(2, 0), Type = ReservationType.Flight
        };
        Assert.True(UpcomingActivitySelector.IsInProgress(flight, "Asia/Tokyo",
            new DateTimeOffset(2026, 9, 26, 16, 0, 0, TimeSpan.Zero)));
        Assert.False(UpcomingActivitySelector.IsInProgress(flight, "Asia/Tokyo",
            new DateTimeOffset(2026, 9, 26, 17, 0, 1, TimeSpan.Zero)));

        var hotel = Item(Day, new TimeOnly(15, 0)) with
        {
            Type = ReservationType.Lodging, EndsOn = Day.AddDays(5), EndsAt = new TimeOnly(11, 0)
        };
        Assert.True(UpcomingActivitySelector.IsInProgress(hotel, "Asia/Tokyo",
            new DateTimeOffset(2026, 9, 27, 7, 0, 0, TimeSpan.Zero)));
        Assert.False(UpcomingActivitySelector.IsInProgress(hotel, "Asia/Tokyo",
            new DateTimeOffset(2026, 9, 27, 7, 0, 1, TimeSpan.Zero)));
    }

    private static ScheduleItemDto Item(DateOnly date, TimeOnly time) => new(
        Guid.NewGuid(), null, ReservationType.Event, date, time, null, null, "Plan", "Tokyo",
        "Lugar", "Dirección", "", "", null, null, null, null, null, null);
}

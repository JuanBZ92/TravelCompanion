using TravelCompanion.Mobile.Services;
using TravelCompanion.Shared;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Mobile.Tests;

public sealed class TripDayContextTests
{
    private static readonly DateOnly Today = new(2026, 10, 7);
    private static DateTimeOffset At(int hour, int minute = 0) => new(2026, 10, 7, hour, minute, 0, TimeSpan.FromHours(9));
    private static ScheduleItemDto Item(int hour = 12) => new(Guid.NewGuid(), null, ReservationType.Event,
        Today, new(hour, 0), Today, new((hour + 1) % 24, 0), "Paseo por Gion", "Kyoto", "Gion", "Kyoto, Japan",
        "", "", null, null, null, null, null, null);
    private static TripScheduleDto Trip(params ScheduleItemDto[] items) => new(Guid.NewGuid(), "QA", "Japan",
        Today.AddDays(-1), Today.AddDays(3), items);

    [Fact]
    public void Future_activity_excludes_tomorrow_and_keeps_detail_metadata()
    {
        var item = Item();
        var result = TripDayOverview.ResolveContext(Trip(item), Today, At(9));
        Assert.Same(item, result.Activity);
        Assert.False(result.IsInProgress);
        Assert.True(result.HasContent);
        Assert.Null(result.Tomorrow);
    }

    [Fact]
    public void Same_activity_transitions_from_future_to_in_progress_then_tomorrow()
    {
        var item = Item();
        var trip = Trip(item);
        var future = TripDayOverview.ResolveContext(trip, Today, At(11, 59));
        var active = TripDayOverview.ResolveContext(trip, Today, At(12));
        var ended = TripDayOverview.ResolveContext(trip, Today, At(13, 1));
        Assert.Equal(item.Id, future.Activity!.Id);
        Assert.Equal(item.Id, active.Activity!.Id);
        Assert.False(future.IsInProgress);
        Assert.True(active.IsInProgress);
        Assert.NotEqual(future, active); // The UI must notify even when the identity stays the same.
        Assert.Null(ended.Activity);
        Assert.False(ended.IsInProgress);
        Assert.Equal(Today.AddDays(1), ended.Tomorrow);
    }

    [Fact]
    public void Ongoing_activity_precedes_future_and_final_completion_reveals_tomorrow()
    {
        var active = Item(10) with { EndsAt = new(14, 0) };
        var future = Item(13);
        var trip = Trip(future, active);
        Assert.Equal(active.Id, TripDayOverview.ResolveContext(trip, Today, At(12)).Activity!.Id);
        Assert.Null(TripDayOverview.ResolveContext(trip, Today, At(14, 1)).Activity);
        Assert.Equal(Today.AddDays(1), TripDayOverview.ResolveContext(trip, Today, At(14, 1)).Tomorrow);
    }

    [Fact]
    public void Confirmed_empty_day_can_show_tomorrow_but_initial_absence_cannot()
    {
        Assert.Equal(Today.AddDays(1), TripDayOverview.ResolveContext(Trip(), Today, At(12)).Tomorrow);
        Assert.Equal(TripDayContext.Empty, TripDayOverview.ResolveContext(null, Today, At(12)));
        Assert.False(TripDayOverview.ResolveContext(null, Today, At(12)).HasContent);
    }

    [Fact]
    public void Flexible_ideas_stay_in_schedule_without_blocking_tomorrow()
    {
        var idea = Item(18) with { TimePrecision = ItineraryTimePrecision.PeriodOnly };
        var trip = Trip(idea);
        var result = TripDayOverview.ResolveContext(trip, Today, At(9));
        Assert.Null(result.Activity);
        Assert.Equal(Today.AddDays(1), result.Tomorrow);
        Assert.Same(idea, Assert.Single(trip.Items));
    }

    [Fact]
    public void Last_day_shows_upcoming_activity_but_no_tomorrow_after_it_ends()
    {
        var trip = Trip(Item()) with { EndsOn = Today };
        Assert.NotNull(TripDayOverview.ResolveContext(trip, Today, At(9)).Activity);
        var ended = TripDayOverview.ResolveContext(trip, Today, At(14));
        Assert.Equal(TripDayContext.Empty, ended);
        Assert.False(ended.HasContent);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(1)]
    public void Other_selected_dates_hide_both_contexts(int offset)
    {
        var selected = Today.AddDays(offset);
        Assert.False(TripDayOverview.ResolveContext(Trip(Item()), selected, At(9)).HasContent);
        Assert.False(TripDayOverview.ResolveContext(Trip(), selected, At(9)).HasContent);
    }

    [Fact]
    public void Missing_selection_or_today_outside_trip_hides_context()
    {
        Assert.False(TripDayOverview.ResolveContext(Trip(Item()), null, At(9)).HasContent);
        var futureTrip = Trip() with { StartsOn = Today.AddDays(1) };
        Assert.False(TripDayOverview.ResolveContext(futureTrip, Today, At(9)).HasContent);
    }

    [Fact]
    public void Tokyo_midnight_switches_context_date_and_preserves_overnight_flight()
    {
        var tomorrow = Today.AddDays(1);
        var flight = Item(23) with { Type = ReservationType.Flight, EndsOn = tomorrow, EndsAt = new(1, 0) };
        var trip = Trip(flight);
        var before = new DateTimeOffset(2026, 10, 7, 14, 59, 0, TimeSpan.Zero);
        var after = before.AddMinutes(2);
        Assert.Equal(flight.Id, TripDayOverview.ResolveContext(trip, Today, before).Activity!.Id);
        Assert.False(TripDayOverview.ResolveContext(trip, Today, after).HasContent);
        var current = TripDayOverview.ResolveContext(trip, tomorrow, after);
        Assert.Equal(flight.Id, current.Activity!.Id);
        Assert.True(current.IsInProgress);
        Assert.Null(current.Tomorrow);
        Assert.Equal(tomorrow.AddDays(1), TripDayOverview.ResolveContext(trip, tomorrow, after.AddHours(2)).Tomorrow);
    }

    [Fact]
    public void Context_uses_trip_timezone_rather_than_device_or_utc_date()
    {
        var instant = new DateTimeOffset(2026, 10, 6, 23, 0, 0, TimeSpan.Zero);
        Assert.True(TripDayOverview.ResolveContext(Trip(), Today, instant).HasContent);
        var utcTrip = Trip() with { TimeZoneId = "UTC" };
        Assert.False(TripDayOverview.ResolveContext(utcTrip, Today, instant).HasContent);
        Assert.True(TripDayOverview.ResolveContext(utcTrip, Today.AddDays(-1), instant).HasContent);
    }

    [Fact]
    public void Hotel_check_in_does_not_block_tomorrow_until_checkout()
    {
        var hotel = Item(15) with { Type = ReservationType.Lodging, EndsOn = Today.AddDays(3), EndsAt = new(10, 0) };
        var trip = Trip(hotel);
        Assert.True(TripDayOverview.ResolveContext(trip, Today, At(15, 30)).IsInProgress);
        Assert.Equal(Today.AddDays(1), TripDayOverview.ResolveContext(trip, Today, At(16, 1)).Tomorrow);
    }

    [Fact]
    public void Adding_editing_and_removing_timed_items_recomputes_context_from_confirmed_schedule()
    {
        var trip = Trip();
        Assert.NotNull(TripDayOverview.ResolveContext(trip, Today, At(9)).Tomorrow);
        var item = Item();
        var added = trip with { Items = [item], Revision = 1 };
        Assert.Equal(item.Id, TripDayOverview.ResolveContext(added, Today, At(9)).Activity!.Id);
        var edited = added with { Items = [item with { StartsAt = new(7, 0), EndsAt = new(8, 0) }], Revision = 2 };
        Assert.NotNull(TripDayOverview.ResolveContext(edited, Today, At(9)).Tomorrow);
        var removed = added with { Items = [], Revision = 3 };
        Assert.NotNull(TripDayOverview.ResolveContext(removed, Today, At(9)).Tomorrow);
    }

    [Fact]
    public void Clearing_context_then_loading_another_trip_does_not_reuse_old_activity()
    {
        var first = Trip(Item());
        var second = Trip();
        Assert.NotNull(TripDayOverview.ResolveContext(first, Today, At(9)).Activity);
        Assert.Equal(TripDayContext.Empty, TripDayOverview.ResolveContext(null, Today, At(9)));
        var result = TripDayOverview.ResolveContext(second, Today, At(9));
        Assert.Null(result.Activity);
        Assert.NotNull(result.Tomorrow);
    }
}

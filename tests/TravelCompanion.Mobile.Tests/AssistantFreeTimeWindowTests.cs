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

    [Fact]
    public void Express_moves_after_ongoing_commitments_and_stops_before_the_next_one()
    {
        var schedule = Schedule(Item(new(9, 45), new(10, 30)), Item(new(11, 0), new(12, 0)));
        var result = AssistantFreeTimeWindow.ResolveSoon(schedule, 60, new(2026, 10, 8, 1, 0, 0, TimeSpan.Zero));
        Assert.Equal(Day.ToDateTime(new(10, 30)), result!.StartsAtLocal);
        Assert.Equal(Day.ToDateTime(new(11, 0)), result.EndsAtLocal);
        Assert.Equal(30, result.AvailableMinutes);
    }

    [Fact]
    public void Express_never_spills_into_tomorrow_or_skips_an_overnight_commitment()
    {
        var result = AssistantFreeTimeWindow.ResolveSoon(Schedule(), 60,
            new(2026, 10, 8, 14, 30, 0, TimeSpan.Zero));
        Assert.Equal(Day.ToDateTime(new(23, 59)), result!.EndsAtLocal);
        Assert.Equal(29, result.AvailableMinutes);
        Assert.Null(AssistantFreeTimeWindow.ResolveSoon(Schedule(Item(new(23, 0), new(1, 0))), 60,
            new(2026, 10, 8, 14, 30, 0, TimeSpan.Zero)));
    }

    [Fact]
    public void Next_located_plan_converts_source_zones_and_excludes_started_unlocated_and_flexible_ideas()
    {
        var utc = Item(new(2, 0), new(3, 0)) with { TimeZoneId = "UTC", Latitude = 35.7m, Longitude = 139.7m };
        var tokyo = Item(new(10, 45), new(11, 0)) with { Latitude = 35.7m, Longitude = 139.7m };
        var noLocation = Item(new(10, 15), new(10, 30));
        var flexible = tokyo with { Id = Guid.NewGuid(), StartsAt = new(10, 5), TimePrecision = ItineraryTimePrecision.PeriodOnly };
        var started = tokyo with { Id = Guid.NewGuid(), StartsAt = new(9, 30) };
        var result = AssistantFreeTimeWindow.NextLocatedPlan(Schedule(utc, tokyo, noLocation, flexible, started),
            new(2026, 10, 8, 1, 0, 0, TimeSpan.Zero));
        Assert.Equal(tokyo.Id, result!.Id);
        Assert.Equal(Day.ToDateTime(new(11, 0)), AssistantFreeTimeWindow.TripStart(Schedule(), utc));
    }

    [Fact]
    public void Next_located_plan_respects_trip_midnight_and_stable_ties()
    {
        var first = Item(new(15, 30), new(16, 0)) with { Date = Day.AddDays(-1), TimeZoneId = "UTC",
            Latitude = 35.7m, Longitude = 139.7m, Id = Guid.Parse("00000000-0000-0000-0000-000000000001") };
        var confirmed = first with { Id = Guid.Parse("00000000-0000-0000-0000-000000000002"),
            PlanningKind = ScheduleItemKind.ConfirmedReservation };
        var result = AssistantFreeTimeWindow.NextLocatedPlan(Schedule(first, confirmed),
            new(2026, 10, 7, 15, 0, 0, TimeSpan.Zero));
        Assert.Equal(confirmed.Id, result!.Id);
        Assert.Equal(Day.ToDateTime(new(0, 30)), AssistantFreeTimeWindow.TripStart(Schedule(), first));
    }

    [Fact]
    public void Express_exact_time_is_visible_without_reclassifying_the_option_as_a_day_plan()
    {
        var card = new TravelCompanion.Mobile.ViewModels.TravelChatCardViewModel(new("recommendation", "Cafe", "", "",
            "10:10", "10:25", "", null, null, [], [], Guid.NewGuid().ToString(), null));
        var row = Assert.Single(AssistantDayProposalBuilder.BuildQuickSearch([], [card], Day));
        Assert.Equal("10:10", row.When);
        Assert.False(card.IsDayPlanCard);
    }

    [Fact]
    public void Express_options_keep_their_positions_and_do_not_mix_in_intermediate_saved_plans()
    {
        var first = new TravelCompanion.Mobile.ViewModels.TravelChatCardViewModel(new("recommendation", "First", "", "",
            "10:30", "10:45", "", null, null, [], [], Guid.NewGuid().ToString(), null));
        var second = new TravelCompanion.Mobile.ViewModels.TravelChatCardViewModel(new("recommendation", "Second", "", "",
            "10:05", "10:15", "", null, null, [], [], Guid.NewGuid().ToString(), null));
        var rows = AssistantDayProposalBuilder.BuildQuickSearch([Item(new(10, 20), new(10, 25))],
            [first, second], Day, preserveOptionPositions: true);
        Assert.Equal(new[] { "First", "Second" }, rows.Select(row => row.Title));
        Assert.All(rows, row => Assert.True(row.IsSuggestion));
    }
}

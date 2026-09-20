using TravelCompanion.Shared;

namespace TravelCompanion.Shared.Tests;

public sealed class ReservationReminderPolicyTests
{
    [Fact]
    public void City_change_notifies_previous_day_at_nine_local_once_and_disappears_if_removed()
    {
        var id = Guid.NewGuid();
        (Guid Id, DateOnly Date, string City)[] days =
        [
            (Guid.NewGuid(), new(2026, 10, 5), " Tokyo "),
            (Guid.NewGuid(), new(2026, 10, 6), "tokyo"),
            (id, new(2026, 10, 7), "Kyoto")
        ];
        var now = DateTimeOffset.Parse("2026-10-01T00:00:00Z");
        var reminder = Assert.Single(ReservationReminderPolicy.CityChanges(days.Reverse(), "Asia/Tokyo", now, false));
        Assert.Equal(DateTimeOffset.Parse("2026-10-06T00:00:00Z"), reminder.NotifyAtUtc);
        Assert.Null(reminder.ReservationId);
        Assert.Equal(id, reminder.TripDayId);
        Assert.Contains("Kyoto", reminder.Body);
        Assert.Empty(ReservationReminderPolicy.CityChanges(days.Take(2), "Asia/Tokyo", now, false));
        Assert.Empty(ReservationReminderPolicy.CityChanges(days, "Asia/Tokyo", reminder.NotifyAtUtc, false));
    }

    [Theory]
    [InlineData(ReservationType.Event, "Reserva")]
    [InlineData(ReservationType.Flight, "Vuelo")]
    [InlineData(ReservationType.Lodging, "Check-in")]
    public void Tokyo_19_means_reminders_at_16_and_1815_local(ReservationType type, string label)
    {
        var reminders = ReservationReminderPolicy.Create(Guid.NewGuid(), type, new(2026, 10, 6), new(19, 0),
            "Asia/Tokyo", "Test", new DateTimeOffset(2026, 10, 6, 0, 0, 0, TimeSpan.Zero), false);
        Assert.Equal(2, reminders.Count);
        Assert.Equal(new DateTimeOffset(2026, 10, 6, 7, 0, 0, TimeSpan.Zero), reminders[0].NotifyAtUtc);
        Assert.Equal(new DateTimeOffset(2026, 10, 6, 9, 15, 0, TimeSpan.Zero), reminders[1].NotifyAtUtc);
        Assert.StartsWith(label, reminders[0].Title);
        Assert.Contains("45 minutos", reminders[1].Body);
    }

    [Fact]
    public void Midnight_check_in_has_previous_day_reminders_and_no_daily_repeats()
    {
        var reminders = ReservationReminderPolicy.Create(Guid.NewGuid(), ReservationType.Lodging,
            new(2026, 10, 6), new(0, 30), "UTC", "Hotel", DateTimeOffset.Parse("2026-10-05T01:00:00Z"), true);
        Assert.Equal(DateTimeOffset.Parse("2026-10-05T21:30:00Z"), reminders[0].NotifyAtUtc);
        Assert.Equal(DateTimeOffset.Parse("2026-10-05T23:45:00Z"), reminders[1].NotifyAtUtc);
    }

    [Fact]
    public void Only_flights_and_check_in_receive_the_extra_24_hour_reminder()
    {
        var now = DateTimeOffset.Parse("2026-10-01T00:00:00Z");
        foreach (var type in new[] { ReservationType.Flight, ReservationType.Lodging, ReservationType.Event })
        {
            var reminders = ReservationReminderPolicy.Create(Guid.NewGuid(), type, new(2026, 10, 6), new(19, 0),
                "Asia/Tokyo", "Test", now, false);
            Assert.Equal(type == ReservationType.Event ? 2 : 3, reminders.Count);
            if (type != ReservationType.Event)
            {
                Assert.Equal(DateTimeOffset.Parse("2026-10-05T10:00:00Z"), reminders[0].NotifyAtUtc);
                Assert.Contains("24 horas", reminders[0].Body);
            }
        }
    }

    [Fact]
    public void Late_creation_skips_past_reminders_and_edits_keep_stable_identity()
    {
        var id = Guid.NewGuid();
        var now = DateTimeOffset.Parse("2026-10-06T17:00:00Z");
        var before = ReservationReminderPolicy.Create(id, ReservationType.Flight, new(2026, 10, 6), new(19, 0), "UTC", "Flight", now, true);
        Assert.Single(before);
        var after = ReservationReminderPolicy.Create(id, ReservationType.Flight, new(2026, 10, 6), new(20, 0), "UTC", "Flight", now, true);
        Assert.Equal(before[0].Id, after[0].Id);
        Assert.NotEqual(before[0].NotifyAtUtc, after[0].NotifyAtUtc);
    }

    [Theory]
    [InlineData(ReservationType.Event)]
    [InlineData(ReservationType.Flight)]
    [InlineData(ReservationType.Lodging)]
    public void Period_only_never_schedules_reminders(ReservationType type) => Assert.False(
        ReservationReminderPolicy.IsEligible(type, ItineraryTimePrecision.PeriodOnly,
            ScheduleItemKind.ConfirmedReservation, ItineraryFlexibility.ConfirmedReservation));

    [Fact]
    public void Flexible_and_fixed_activities_are_not_confirmed_reservations()
    {
        Assert.False(ReservationReminderPolicy.IsEligible(ReservationType.Event, ItineraryTimePrecision.Exact,
            ScheduleItemKind.Recommendation, ItineraryFlexibility.Flexible));
        Assert.False(ReservationReminderPolicy.IsEligible(ReservationType.Event, ItineraryTimePrecision.Exact,
            ScheduleItemKind.ManualEvent, ItineraryFlexibility.FixedByTraveler));
        Assert.True(ReservationReminderPolicy.IsEligible(ReservationType.Event, ItineraryTimePrecision.Exact,
            ScheduleItemKind.ManualEvent, ItineraryFlexibility.ConfirmedReservation));
    }

    [Theory]
    [InlineData("Not/AZone", 10, 6, 19)]
    [InlineData("Europe/Madrid", 3, 29, 2)]
    [InlineData("Europe/Madrid", 10, 25, 2)]
    public void Unknown_or_ambiguous_times_are_not_guessed(string zone, int month, int day, int hour) => Assert.Empty(
        ReservationReminderPolicy.Create(Guid.NewGuid(), ReservationType.Event, new(2026, month, day), new(hour, 30),
            zone, "Test", DateTimeOffset.Parse("2026-01-01T00:00:00Z"), true));
}

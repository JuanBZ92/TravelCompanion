using TravelCompanion.Mobile.Services;
using TravelCompanion.Shared;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Mobile.Tests;

public sealed class JournalActivitySearchTests
{
    private static readonly DateOnly Day = new(2026, 10, 2);

    [Fact]
    public void Search_keeps_selected_date_and_matches_title_or_city()
    {
        var temple = Activity("Templo", "Kioto", Day, 9);
        var museum = Activity("Museo", "Tokio", Day, 12);
        var tomorrow = temple with { Id = Guid.NewGuid(), Date = Day.AddDays(1) };
        var items = new[] { museum, tomorrow, temple };

        Assert.Equal(new[] { temple }, ScheduleActivityLookup.SearchJournalActivities(items, Day, " TEMPLO "));
        Assert.Equal(new[] { museum }, ScheduleActivityLookup.SearchJournalActivities(items, Day, "tokio"));
        Assert.Empty(ScheduleActivityLookup.SearchJournalActivities(items, Day, "hotel"));
        Assert.Empty(ScheduleActivityLookup.SearchJournalActivities(items, Day.AddDays(-1)));
    }

    [Fact]
    public void Clearing_search_keeps_day_filter_and_chronological_order()
    {
        var early = Activity("Museo", "Tokio", Day, 9);
        var late = Activity("Cena", "Tokio", Day, 20);
        var other = Activity("Tren", "Kioto", Day.AddDays(1), 7);
        var items = new[] { other, late, early };

        Assert.Equal(new[] { early, late }, ScheduleActivityLookup.SearchJournalActivities(items, Day, " "));
        Assert.Equal(new[] { early, late, other }, ScheduleActivityLookup.SearchJournalActivities(items));
        Assert.Equal(new[] { other }, ScheduleActivityLookup.SearchJournalActivities(items, other.Date));
    }

    private static ScheduleItemDto Activity(string title, string city, DateOnly date, int hour) => new(
        Guid.NewGuid(), null, ReservationType.Event, date, new TimeOnly(hour, 0), null, null,
        title, city, "", "", "", "", null, null, null, null, null, null);
}

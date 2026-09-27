using TravelCompanion.Mobile.Services;
using TravelCompanion.Shared;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Mobile.Tests;

public sealed class JournalAndNavigationTests
{
    [Fact]
    public void Journal_excludes_editorial_notes_and_placeholders_and_preserves_personal_text()
    {
        var item = new ScheduleItemDto(Guid.NewGuid(), null, ReservationType.Event,
            new(2026, 10, 1), new(12, 0), null, null, "Cafe", "Tokyo", "", "", "",
            " Mi café favorito.\nVolver con Ana. ", null, null, null, null, null, null,
            Owner: ItineraryItemOwner.Traveler);
        var entries = JournalEntries.Build([
            item,
            item with { Notes = "Guardado desde Travel Assistant." },
            item with { Notes = "Descripción editorial", CuratedNotes = "Descripción editorial" },
            item with { Notes = " " },
            item with { Owner = ItineraryItemOwner.Yuku },
            item with { Id = Guid.NewGuid(), Date = new(2026, 9, 30), Notes = "Primer día" }
        ]);
        Assert.Equal(2, entries.Count);
        Assert.Equal("Primer día", entries[0].Notes);
        Assert.Equal("Mi café favorito.\nVolver con Ana.", entries[1].Notes);
        Assert.Equal(item.Id, entries[1].Item.Id);
    }

    [Fact]
    public void Tab_back_walks_the_history_then_stops_at_root_and_clears_on_logout()
    {
        var history = new NavigationTrail();
        history.Visit("schedule"); history.Visit("assistant"); history.Visit("assistant");
        history.Visit("journal");
        Assert.Equal("assistant", history.Previous);
        history.Pop();
        Assert.Equal("schedule", history.Previous);
        history.Pop();
        Assert.Null(history.Previous);
        history.Visit("docs"); history.Clear();
        Assert.Null(history.Previous);
    }

    [Fact]
    public void Closing_requires_two_root_back_presses_within_two_seconds()
    {
        var guard = new ExitBackPressGuard();
        var now = DateTimeOffset.UtcNow;
        Assert.False(guard.ShouldExit(now));
        Assert.False(guard.ShouldExit(now.AddSeconds(3)));
        Assert.True(guard.ShouldExit(now.AddSeconds(4)));
        Assert.False(guard.ShouldExit(now.AddSeconds(5)));
        guard.Reset();
        Assert.False(guard.ShouldExit(now.AddSeconds(6)));
    }
}

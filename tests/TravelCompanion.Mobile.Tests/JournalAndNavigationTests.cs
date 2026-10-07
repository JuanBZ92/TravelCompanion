using TravelCompanion.Mobile.Services;
using TravelCompanion.Shared;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Mobile.Tests;

public sealed class JournalAndNavigationTests
{
    [Fact]
    public void RevalidatingUnchangedRowsRetainsInstancesAndDoesNotResetList()
    {
        var first = JournalMemory.NewFree(Guid.NewGuid(), new(2026, 10, 2));
        var second = JournalMemory.NewFree(first.Note.TripId, new(2026, 10, 3));
        var rows = new System.Collections.ObjectModel.ObservableCollection<JournalMemory> { first, second };
        var changes = new List<System.Collections.Specialized.NotifyCollectionChangedAction>();
        rows.CollectionChanged += (_, e) => changes.Add(e.Action);
        JournalEntries.ReconcileRows(rows, new[] { first, second }, x => x.Key);
        Assert.Empty(changes); Assert.Same(first, rows[0]); Assert.Same(second, rows[1]);
        var edited = second with { Photos = [new(Guid.NewGuid())] };
        JournalEntries.ReconcileRows(rows, new[] { edited, first }, x => x.Key);
        Assert.Equal([edited, first], rows); Assert.Same(first, rows[1]);
        Assert.DoesNotContain(System.Collections.Specialized.NotifyCollectionChangedAction.Reset, changes);
        Assert.Equal(2, rows.Select(x => x.Key).Distinct().Count());
    }

    [Fact]
    public void DeserializedPhotoArraysAreEquivalentButTextCoverAndStatusChangesAreNot()
    {
        var original = JournalMemory.NewFree(Guid.NewGuid(), new(2026, 10, 2)) with { Photos = [new(Guid.NewGuid()), new(Guid.NewGuid())] };
        var recovered = System.Text.Json.JsonSerializer.Deserialize<JournalMemory>(System.Text.Json.JsonSerializer.Serialize(original))!;
        Assert.True(JournalEntries.SameContent(original, recovered));
        Assert.False(JournalEntries.SameContent(original, recovered with { CoverId = original.Images[1].Id }));
        Assert.False(JournalEntries.SameContent(original, recovered with { FreePending = new("", "", original.Date, "Changed", 0, Guid.NewGuid()) }));
        Assert.False(JournalEntries.SameContent(original, recovered with { Photos = original.Images.Reverse().ToArray() }));
    }

    [Fact]
    public void LongJournalLoadsOnlyVisibleRowsAndBoundsInactiveThumbnailCache()
    {
        Assert.Equal([499, 500, 501, 502, 503, 504], JournalEntries.VisibleRows(1000, 500, 503));
        Assert.Empty(JournalEntries.VisibleRows(0, 0, 3)); Assert.Empty(JournalEntries.VisibleRows(1000, -1, -1));
        var cache = new JournalThumbnailCache<object>(3); var ids = Enumerable.Range(0, 6).Select(_ => Guid.NewGuid()).ToArray();
        foreach (var id in ids) cache.Add(id, new object());
        Assert.True(cache.TryGet(ids[0], out var kept));
        var evicted = cache.Trim(new HashSet<Guid> { ids[4], ids[5] });
        Assert.Equal(3, cache.Count); Assert.DoesNotContain(ids[0], evicted);
        Assert.True(cache.TryGet(ids[0], out var recovered)); Assert.Same(kept, recovered);
        cache.Clear(); Assert.False(cache.TryGet(ids[0], out _));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(7)]
    public void PhotoFanKeepsChosenCoverInFrontAndOriginalViewerIndices(int photoCount)
    {
        var photos = Enumerable.Range(0, photoCount).Select(_ => new JournalPhoto(Guid.NewGuid())).ToArray();
        var memory = JournalMemory.NewFree(Guid.NewGuid(), new(2026, 10, 2)) with
            { Photos = photos, CoverId = photos[^1].Id };
        var fan = JournalEntries.PhotoPreviewIndices(memory);
        Assert.Equal(Math.Min(3, photoCount), fan.Count);
        Assert.Equal(photoCount - 1, fan[0]);
        Assert.Equal(memory.CoverId, memory.Images[fan[0]].Id);
        Assert.Equal(fan.Count, fan.Distinct().Count());
        Assert.Equal(photos.Select(x => x.Id), memory.Images.Select(x => x.Id));
        Assert.Equal(photoCount, memory.Images.Length);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MissingCoverFallsBackToFirstPhotoWithoutChangingSavedCover(bool invalidCover)
    {
        var photos = Enumerable.Range(0, 5).Select(_ => new JournalPhoto(Guid.NewGuid())).ToArray();
        Guid? cover = invalidCover ? Guid.NewGuid() : null;
        var memory = JournalMemory.NewFree(Guid.NewGuid(), new(2026, 10, 2)) with { Photos = photos, CoverId = cover };
        Assert.Equal([0, 1, 2], JournalEntries.PhotoPreviewIndices(memory));
        Assert.Equal(0, Assert.Single(JournalEntries.PhotoPreviewIndices(memory, 1)));
        Assert.Equal(cover, memory.CoverId);
    }

    [Fact]
    public void ReaderPreviewsKeepChosenCoverAndHiddenPhotosKeepTheirViewerIndices()
    {
        var photos = Enumerable.Range(0, 10).Select(_ => new JournalPhoto(Guid.NewGuid())).ToArray();
        var memory = JournalMemory.NewFree(Guid.NewGuid(), new(2026, 10, 2)) with
            { Photos = photos, CoverId = photos[6].Id };
        Assert.Equal([6, 0, 1], JournalEntries.PhotoPreviewIndices(memory));
        Assert.Equal(2, JournalEntries.PhotoPreviewIndices(memory, 4)[3]);
        Assert.Equal(10, memory.Images.Length);
        Assert.Empty(JournalEntries.PhotoPreviewIndices(memory with { Photos = [] }));
    }

    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(1, 1, 0)]
    [InlineData(3, 3, 0)]
    [InlineData(4, 3, 1)]
    [InlineData(10, 3, 7)]
    public void ReaderLimitsPreviewsToThreeAndKeepsExtraPhotosAvailable(int photoCount, int expectedPreviews, int expectedMore)
    {
        var memory = JournalMemory.NewFree(Guid.NewGuid(), new(2026, 10, 2)) with
            { Photos = Enumerable.Range(0, photoCount).Select(_ => new JournalPhoto(Guid.NewGuid())).ToArray() };
        var previews = JournalEntries.PhotoPreviewIndices(memory);
        Assert.Equal(expectedPreviews, previews.Count);
        var hidden = Enumerable.Range(0, memory.Images.Length).Except(previews).ToArray();
        Assert.Equal(expectedMore, hidden.Length);
        if (expectedMore > 0) Assert.Equal(hidden[0], JournalEntries.PhotoPreviewIndices(memory, 4)[3]);
    }

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

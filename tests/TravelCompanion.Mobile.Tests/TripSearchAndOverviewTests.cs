using TravelCompanion.Mobile.Services;
using TravelCompanion.Shared;
using TravelCompanion.Shared.Dtos;
namespace TravelCompanion.Mobile.Tests;

public sealed class TripSearchAndOverviewTests
{
    private static ScheduleItemDto Item() => new(Guid.NewGuid(), null, ReservationType.Event,
        new(2026, 10, 20), new(9, 0), null, null, "Café de Kyoto", "Kyoto", "Gion", "", "", "", null, null, null, null, null, null, Owner: ItineraryItemOwner.Traveler);
    [Fact]
    public void Search_handles_accents_words_dates_and_type_without_reading_files()
    {
        var booking = Item(); var memory = JournalMemory.NewFree(Guid.NewGuid(), booking.Date) with { IsDraft = false,
            FreeEntry = new(Guid.NewGuid(), Guid.NewGuid(), "Tarde en Kyoto", "Gion", booking.Date, "Volver al jardín", 1, DateTimeOffset.UtcNow, false) };
        var index = new TripSearchIndex([
            new("booking", TripSearchKind.Reservation, booking.Title, "Kyoto · Gion", booking.Date, booking),
            new("memory", TripSearchKind.Memory, memory.Title, memory.City, memory.Date, memory),
            new("document", TripSearchKind.Document, "Billete del tren", "Transporte", null, new LocalTripDocument(Guid.NewGuid(), "Billete", ".pdf", 200, DateTimeOffset.UtcNow))]);
        Assert.Equal("booking", Assert.Single(index.Find("cafe kyoto")).Key);
        Assert.Equal("memory", Assert.Single(index.Find("jardin")).Key);
        Assert.Equal(2, index.Find("20/10").Count);
        Assert.Single(index.Find("2026-10-20", TripSearchKind.Memory));
        Assert.Empty(index.Find("kyoto", TripSearchKind.Document));
        Assert.Equal("document", Assert.Single(index.Find("tren")).Key);
    }
    [Fact]
    public void Today_distinguishes_empty_flexible_and_no_upcoming_reservations()
    {
        var item = Item();
        Assert.Equal("TodayEmpty", TripDayOverview.TodayMessage([], item.Date));
        Assert.Equal("TodayFlexible", TripDayOverview.TodayMessage([], item.Date, true));
        Assert.Equal("TodayFlexible", TripDayOverview.TodayMessage([item with { TimePrecision = ItineraryTimePrecision.PeriodOnly }], item.Date));
        Assert.Equal("TodayNoUpcoming", TripDayOverview.TodayMessage([item], item.Date));
    }
    [Fact]
    public void Tomorrow_is_inside_trip_and_selects_first_distinct_booking_separately_from_hotel()
    {
        var first = Item(); var later = first with { Id = Guid.NewGuid(), StartsAt = new(12, 0) };
        var hotel = first with { Id = Guid.NewGuid(), Type = ReservationType.Lodging, Date = first.Date.AddDays(-1), EndsOn = first.Date.AddDays(1) };
        var trip = new TripScheduleDto(Guid.NewGuid(), "Ana", "Japan", first.Date.AddDays(-1), first.Date.AddDays(1), [later, hotel, first, first], 1);
        Assert.Equal(first.Date, TripDayOverview.Tomorrow(trip, first.Date.AddDays(-1)));
        Assert.Null(TripDayOverview.Tomorrow(trip, trip.EndsOn));
        Assert.Equal(first.Id, TripDayOverview.FirstBooking(trip, first.Date)!.Id);
        Assert.Equal(hotel.Id, TripDayOverview.Hotel(trip, first.Date)!.Id);
        Assert.Equal([first.Id, hotel.Id], TripDayOverview.DocumentReservations(trip, first.Date));
    }

    [Fact]
    public void Tomorrow_downloaded_curated_document_still_requires_access_but_personal_attachment_does_not()
    {
        var file = new LocalTripDocument(Guid.NewGuid(), "Hotel confirmation", ".pdf", 200, DateTimeOffset.UtcNow,
            SourceUrl: "/trip-files/hotel.pdf");
        var link = new ReservationDocumentLink(Guid.NewGuid(), file.Id, null, file.Title);
        Assert.True(TripDayOverview.CanOpenDocument(link, file, hasCuratedAccess: true));
        Assert.False(TripDayOverview.CanOpenDocument(link, file, hasCuratedAccess: false));
        Assert.True(TripDayOverview.CanOpenDocument(link, file with { SourceUrl = null }, hasCuratedAccess: false));
        Assert.False(TripDayOverview.CanOpenDocument(link, null, hasCuratedAccess: true));
        Assert.False(TripDayOverview.CanOpenDocument(link, file with { Id = Guid.NewGuid() }, hasCuratedAccess: true));
    }

    [Fact]
    public void Tomorrow_linked_curated_url_requires_access_even_if_a_local_file_is_available()
    {
        var file = new LocalTripDocument(Guid.NewGuid(), "Ticket", ".pdf", 200, DateTimeOffset.UtcNow);
        var link = new ReservationDocumentLink(Guid.NewGuid(), null, "/trip-files/ticket.pdf", "Ticket");
        Assert.True(TripDayOverview.CanOpenDocument(link, null, hasCuratedAccess: true));
        Assert.False(TripDayOverview.CanOpenDocument(link, file, hasCuratedAccess: false));
        Assert.False(TripDayOverview.CanOpenDocument(null, file, hasCuratedAccess: true));
    }

    [Fact]
    public void Tomorrow_does_not_repeat_a_file_linked_to_both_first_booking_and_hotel()
    {
        var local = new LocalTripDocument(Guid.NewGuid(), "Confirmation", ".pdf", 200, DateTimeOffset.UtcNow);
        var first = new TripDayDocument(new(Guid.NewGuid(), local.Id, null, local.Title), local);
        var hotel = first with { Link = first.Link with { ReservationId = Guid.NewGuid() } };
        Assert.Same(first, Assert.Single(TripDayOverview.DistinctDocuments([first, hotel])));

        var url = "/trip-files/confirmation.pdf";
        var downloaded = first with { Local = local with { SourceUrl = url } };
        var curated = new TripDayDocument(new(Guid.NewGuid(), null, url, local.Title), null);
        Assert.Same(downloaded, Assert.Single(TripDayOverview.DistinctDocuments([downloaded, curated])));
    }

    [Fact]
    public void Search_keeps_activity_and_its_linked_memory_with_the_same_source_identifier()
    {
        var booking = Item();
        var memory = new JournalMemory(new(booking.Id, Guid.NewGuid(), booking.Title, booking.City,
            booking.Date, "Recordar el jardín secreto", 1, DateTimeOffset.UtcNow));
        var key = "activity-" + booking.Id;
        var index = new TripSearchIndex([
            new(key, TripSearchKind.Reservation, booking.Title, booking.City, booking.Date, booking),
            new(memory.Key, TripSearchKind.Memory, memory.Title, memory.City, memory.Date, memory)]);
        Assert.Equal(2, index.Find("kyoto").Count);
        Assert.Equal(TripSearchKind.Memory, Assert.Single(index.Find("jardin secreto")).Kind);
    }

    [Fact]
    public async Task Search_source_failure_retains_previous_documents_and_refreshes_other_sources()
    {
        var loader = new TripSearchSourceLoader();
        var file = new TripSearchResult("ticket", TripSearchKind.Document, "Train ticket", "Transport", null, new object());
        await loader.LoadAsync([new("documents", _ => Task.FromResult<IReadOnlyList<TripSearchResult>>([file]))],
            () => true, CancellationToken.None);
        var booking = Item();
        var row = new TripSearchResult("activity-" + booking.Id, TripSearchKind.Reservation, booking.Title,
            booking.City, booking.Date, booking);
        var failures = new List<string>();
        var result = await loader.LoadAsync([
            new("reservations", _ => Task.FromResult<IReadOnlyList<TripSearchResult>>([row])),
            new("documents", _ => throw new IOException("Unreadable local metadata"))],
            () => true, CancellationToken.None, failed: (source, _) => failures.Add(source));
        Assert.True(result.HasIncompleteSources);
        Assert.Equal(2, result.Index.Find("").Count);
        Assert.Same(file, Assert.Single(result.Index.Find("ticket")));
        Assert.Equal(["documents"], failures);
    }

    [Fact]
    public async Task Search_does_not_hide_itinerary_when_documents_and_memories_fail_on_first_load()
    {
        var loader = new TripSearchSourceLoader();
        var row = new TripSearchResult("reservation", TripSearchKind.Reservation, "Morning walk", "Tokyo", null, new object());
        var result = await loader.LoadAsync([
            new("reservations", _ => Task.FromResult<IReadOnlyList<TripSearchResult>>([row])),
            new("documents", _ => throw new IOException()),
            new("memories", _ => throw new IOException())], () => true, CancellationToken.None);
        Assert.True(result.HasIncompleteSources);
        Assert.Same(row, Assert.Single(result.Index.Find("")));
    }

    [Fact]
    public async Task Search_successful_empty_source_removes_old_deleted_rows()
    {
        var loader = new TripSearchSourceLoader();
        var file = new TripSearchResult("ticket", TripSearchKind.Document, "Deleted ticket", "", null, new object());
        await loader.LoadAsync([new("documents", _ => Task.FromResult<IReadOnlyList<TripSearchResult>>([file]))],
            () => true, CancellationToken.None);
        var result = await loader.LoadAsync([new("documents", _ => Task.FromResult<IReadOnlyList<TripSearchResult>>([]))],
            () => true, CancellationToken.None);
        Assert.False(result.HasIncompleteSources);
        Assert.Empty(result.Index.Find(""));
    }

    [Fact]
    public async Task Tomorrow_failed_refresh_keeps_documents_and_refreshes_offline_status_independently()
    {
        var loader = new TripDayMetadataLoader();
        var file = new LocalTripDocument(Guid.NewGuid(), "Hotel booking", ".pdf", 200, DateTimeOffset.UtcNow);
        var document = new TripDayDocument(new(Guid.NewGuid(), file.Id, null, file.Title), file);
        await loader.RefreshAsync(_ => Task.FromResult<IReadOnlyList<TripDayDocument>>([document]),
            _ => Task.FromResult(false), () => true, CancellationToken.None);
        var failures = new List<string>();
        var incomplete = await loader.RefreshAsync(_ => throw new IOException("Unreadable local metadata"),
            _ => Task.FromResult(true), () => true, CancellationToken.None,
            failed: (source, _) => failures.Add(source));
        Assert.True(incomplete);
        Assert.Same(document, Assert.Single(loader.Documents!));
        Assert.True(loader.OfflineReady);
        Assert.Equal(["documents"], failures);
    }

    [Fact]
    public async Task Tomorrow_successful_empty_refresh_removes_deleted_document_but_failed_offline_read_keeps_last_state()
    {
        var loader = new TripDayMetadataLoader();
        var document = new TripDayDocument(new(Guid.NewGuid(), Guid.NewGuid(), null, "Ticket"), null);
        await loader.RefreshAsync(_ => Task.FromResult<IReadOnlyList<TripDayDocument>>([document]),
            _ => Task.FromResult(true), () => true, CancellationToken.None);
        var incomplete = await loader.RefreshAsync(_ => Task.FromResult<IReadOnlyList<TripDayDocument>>([]),
            _ => throw new IOException(), () => true, CancellationToken.None);
        Assert.True(incomplete);
        Assert.Empty(loader.Documents!);
        Assert.True(loader.OfflineReady);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Tomorrow_cancel_or_scope_change_discards_late_metadata(bool changeScope)
    {
        var loader = new TripDayMetadataLoader();
        using var cancellation = new CancellationTokenSource();
        var delayed = new TaskCompletionSource<IReadOnlyList<TripDayDocument>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var current = true;
        var published = 0;
        var task = loader.RefreshAsync(_ => delayed.Task, _ => Task.FromResult(true),
            () => current, cancellation.Token, () => published++);
        if (changeScope) current = false;
        else cancellation.Cancel();
        delayed.SetResult([new(new(Guid.NewGuid(), Guid.NewGuid(), null, "Old account"), null)]);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
        Assert.Null(loader.Documents);
        Assert.Null(loader.OfflineReady);
        Assert.Equal(0, published);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Search_discards_late_metadata_after_cancel_or_scope_change(bool changeScope)
    {
        var loader = new TripSearchSourceLoader();
        using var cancellation = new CancellationTokenSource();
        var delayed = new TaskCompletionSource<IReadOnlyList<TripSearchResult>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var current = true;
        var published = 0;
        var task = loader.LoadAsync([new("documents", _ => delayed.Task)], () => current,
            cancellation.Token, _ => published++);
        if (changeScope) current = false;
        else cancellation.Cancel();
        delayed.SetResult([new("late", TripSearchKind.Document, "Previous account", "", null, new object())]);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
        Assert.Equal(0, published);
    }
}

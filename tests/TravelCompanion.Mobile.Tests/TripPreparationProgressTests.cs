using TravelCompanion.Mobile.Services;
using TravelCompanion.Shared;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Mobile.Tests;

public sealed class TripPreparationProgressTests
{
    private static readonly Guid Trip = Guid.NewGuid();
    private static readonly DateOnly Start = new(2026, 10, 1);
    private static TripScheduleDto Schedule(params ScheduleItemDto[] items) => new(Trip, "Test", "Japan", Start, Start.AddDays(2), items, 3);
    private static OfflineTripManifest Manifest(params OfflineTripResource[] resources) => new(Trip, 3, DateTimeOffset.UtcNow, resources, CatalogVersion: 5);
    private static ScheduleItemDto Item(DateOnly date, ReservationType type = ReservationType.Event) => new(
        Guid.NewGuid(), null, type, date, new(10, 0), null, new(11, 0), "Visit", "Tokyo", "Visit", "", "", "", null, null, null, null, null, null);

    [Fact]
    public void Counts_distinct_activity_days_inside_the_trip_only()
    {
        var result = TripPreparationProgress.Create(Trip, Schedule(Item(Start), Item(Start), Item(Start.AddDays(1)),
            Item(Start.AddDays(2), ReservationType.Lodging), Item(Start.AddDays(10))), null);
        Assert.Equal(2, result.ActivityDays);
        Assert.Equal("OfflineNotPrepared", result.OfflineStatusKey);
    }

    [Fact]
    public void Another_trip_cannot_supply_progress_or_downloads()
    {
        var result = TripPreparationProgress.Create(Guid.NewGuid(), Schedule(Item(Start)), Manifest(new OfflineTripResource("itinerary", "Trip", "available")));
        Assert.False(result.HasSchedule);
        Assert.Equal(0, result.ActivityDays);
        Assert.Equal(0, result.DownloadedResources);
        Assert.Equal("OfflineNotPrepared", result.OfflineStatusKey);
    }

    [Theory]
    [InlineData(false, 5, "OfflineReady")]
    [InlineData(true, 5, "OfflineIncomplete")]
    [InlineData(false, 6, "OfflineUpdate")]
    public void Distinguishes_complete_interrupted_and_outdated_downloads(bool interrupted, long catalog, string status)
    {
        var manifest = Manifest(new("itinerary", "Trip", "available"), new("website", "Web", "external")) with { Interrupted = interrupted };
        var result = TripPreparationProgress.Create(Trip, Schedule(), manifest, catalog);
        Assert.Equal(status, result.OfflineStatusKey);
        Assert.Equal(1, result.DownloadedResources);
        Assert.Equal(1, result.DownloadableResources);
    }

    [Fact]
    public void Missing_document_prevents_ready_state_and_remains_in_total()
    {
        var result = TripPreparationProgress.Create(Trip, Schedule(), Manifest(new("itinerary", "Trip", "available"), new("pdf", "Ticket", "pending")));
        Assert.Equal("OfflineIncomplete", result.OfflineStatusKey);
        Assert.Equal(1, result.DownloadedResources);
        Assert.Equal(2, result.DownloadableResources);
    }
}

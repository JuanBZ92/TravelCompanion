using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Shared.Tests;

public sealed class ScheduleReviewAnalyzerTests
{
    private static readonly DateOnly Date = new(2026, 10, 1);

    [Fact]
    public void Detects_confirmed_overlaps()
    {
        var first = Item("Museo", new TimeOnly(10, 0), new TimeOnly(12, 0));
        var second = Item("Almuerzo", new TimeOnly(11, 30), new TimeOnly(13, 0));

        var review = ScheduleReviewAnalyzer.AnalyzeDay(Date, [first, second]);

        Assert.Equal(DayReviewStatuses.NeedsAttention, review.Status);
        var issue = Assert.Single(review.Issues);
        Assert.Equal(DayReviewIssueKinds.Overlap, issue.Kind);
        Assert.Equal(-30, issue.AvailableMinutes);
        Assert.Equal([first.Id, second.Id], issue.ItemIds);
    }

    [Fact]
    public void Detects_tight_transfer_from_known_coordinates()
    {
        var first = Item("Asakusa", new TimeOnly(10, 0), new TimeOnly(11, 0), 35.7148m, 139.7967m);
        var second = Item("Shibuya", new TimeOnly(11, 20), new TimeOnly(12, 0), 35.6595m, 139.7005m);

        var review = ScheduleReviewAnalyzer.AnalyzeDay(Date, [first, second]);

        var issue = Assert.Single(review.Issues);
        Assert.Equal(DayReviewIssueKinds.TightTransfer, issue.Kind);
        Assert.Equal(20, issue.AvailableMinutes);
        Assert.True(issue.RecommendedMinutes > issue.AvailableMinutes);
    }

    [Fact]
    public void Marks_a_day_with_reasonable_gaps_as_balanced()
    {
        var first = Item("Templo", new TimeOnly(9, 0), new TimeOnly(10, 0), 35.7148m, 139.7967m);
        var second = Item("Café", new TimeOnly(12, 0), new TimeOnly(13, 0), 35.7100m, 139.8000m);

        var review = ScheduleReviewAnalyzer.AnalyzeDay(Date, [first, second]);

        Assert.Equal(DayReviewStatuses.Balanced, review.Status);
        Assert.Empty(review.Issues);
    }

    [Fact]
    public void Flags_five_timed_plans_as_a_packed_day()
    {
        var items = Enumerable.Range(0, 5)
            .Select(index => Item(
                $"Plan {index + 1}",
                new TimeOnly(8 + index * 2, 0),
                new TimeOnly(9 + index * 2, 0)))
            .ToList();

        var review = ScheduleReviewAnalyzer.AnalyzeDay(Date, items);

        Assert.Contains(review.Issues, issue => issue.Kind == DayReviewIssueKinds.PackedDay);
    }

    private static ScheduleItemDto Item(
        string title,
        TimeOnly startsAt,
        TimeOnly endsAt,
        decimal? latitude = null,
        decimal? longitude = null) => new(
            Guid.NewGuid(), null, ReservationType.Event, Date, startsAt, null, endsAt,
            title, "Tokyo", title, string.Empty, string.Empty, string.Empty,
            null, null, null, null, null, null,
            ScheduleItemKind.ConfirmedReservation,
            ItineraryItemOwner.Traveler,
            ItineraryItemSource.Manual,
            ItineraryTimePrecision.Exact,
            Latitude: latitude,
            Longitude: longitude);
}

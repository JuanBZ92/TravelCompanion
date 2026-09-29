using TravelCompanion.Mobile.Services;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Mobile.Tests;

public class DayConflictCursorTests
{
    private static DayReviewIssueDto Issue(params Guid[] ids) => new("overlap", "warning", "Title", "Message", ids);
    [Fact]
    public void KeepsConflictAfterItsDescriptionOrPositionChanges()
    {
        var issue = Issue(Guid.NewGuid(), Guid.NewGuid());
        var updated = issue with { Message = "Shorter overlap", ItemIds = issue.ItemIds.Reverse().ToArray() };
        Assert.Equal(1, DayConflictCursor.Select([Issue(Guid.NewGuid()), updated], DayConflictCursor.Key(issue), 0));
    }
    [Fact]
    public void ResolvedConflictSelectsNextRemainingOrLast()
    {
        var first = Issue(Guid.NewGuid()); var second = Issue(Guid.NewGuid()); var third = Issue(Guid.NewGuid());
        Assert.Equal(1, DayConflictCursor.Select([first, third], DayConflictCursor.Key(second), 1));
        Assert.Equal(0, DayConflictCursor.Select([first], DayConflictCursor.Key(third), 2));
    }
    [Fact]
    public void ResolvingAllConflictsKeepsSafeEmptyIndex() => Assert.Equal(0, DayConflictCursor.Select([], "gone", 3));
    [Fact]
    public void DifferentIssueTypesOnSamePlansRemainDistinct()
    {
        var issue = Issue(Guid.NewGuid());
        Assert.NotEqual(DayConflictCursor.Key(issue), DayConflictCursor.Key(issue with { Kind = "period_order_conflict" }));
    }
}

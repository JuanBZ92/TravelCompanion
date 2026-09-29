using TravelCompanion.Mobile.Services;
using TravelCompanion.Shared;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Mobile.Tests;

public class DayConfirmationAndExpenseSelectionTests
{
    private static readonly DateOnly Date = new(2026, 9, 29);
    private static ScheduleItemDto Item() => new(Guid.NewGuid(), null, ReservationType.Event, Date, new(10, 0), null, new(11, 0),
        "Museum", "Tokyo", "Museum", "", "", "", null, null, null, null, null, null);
    [Fact]
    public void ConfirmationChangesWhenDayChangesButNotWhenAnotherDayChanges()
    {
        var item = Item(); var other = Item() with { Date = Date.AddDays(1) };
        var original = DayReviewConfirmation.Fingerprint([item, other], Date);
        Assert.Equal(original, DayReviewConfirmation.Fingerprint([other with { Title = "Different" }, item], Date));
        Assert.NotEqual(original, DayReviewConfirmation.Fingerprint([item with { StartsAt = new(9, 0) }], Date));
        Assert.NotEqual(original, DayReviewConfirmation.Fingerprint([item with { PeriodKey = "afternoon" }], Date));
        Assert.NotEqual(original, DayReviewConfirmation.Fingerprint([], Date));
    }
    [Fact]
    public void ConfirmationIsScopedByAccountTripAndDate()
    {
        var user = Guid.NewGuid(); var trip = Guid.NewGuid(); var key = DayReviewConfirmation.Key(user, trip, Date);
        Assert.NotEqual(key, DayReviewConfirmation.Key(Guid.NewGuid(), trip, Date));
        Assert.NotEqual(key, DayReviewConfirmation.Key(user, Guid.NewGuid(), Date));
        Assert.NotEqual(key, DayReviewConfirmation.Key(user, trip, Date.AddDays(1)));
    }
    [Fact]
    public void ActivityPickerIncludesOnlyExpenseDateAndCanBeEmpty()
    {
        var today = new ExpenseActivityDto(Guid.NewGuid(), "Museum", Date, ExpenseCategory.Activities);
        var tomorrow = today with { Id = Guid.NewGuid(), Date = Date.AddDays(1) };
        Assert.Equal(today, Assert.Single(ExpenseActivitySelection.ForDate([today, tomorrow], Date)));
        Assert.Empty(ExpenseActivitySelection.ForDate([today, tomorrow], Date.AddDays(2)));
    }
}

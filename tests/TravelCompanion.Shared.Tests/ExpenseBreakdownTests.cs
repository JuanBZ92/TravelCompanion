using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Shared.Tests;

public sealed class ExpenseBreakdownTests
{
    [Fact]
    public void BreakdownIncludesLocalPendingAmountsAndMarksMissingOrStaleConversions()
    {
        var trip = Guid.NewGuid(); var date = new DateOnly(2026, 10, 6);
        ExpenseDto Item(decimal? rate, string baseCurrency = "EUR") => new(Guid.NewGuid(), trip, 1000, "JPY", date,
            ExpenseCategory.Food, "Café", null, null, rate, date, "cached", baseCurrency, 0, false, Guid.Empty);
        var result = ExpensePolicy.Breakdown([Item(.006m), Item(null), Item(.007m, "USD"), Item(.006m) with { Deleted = true }], "EUR");
        Assert.Equal("EUR", result.Currency);
        Assert.Equal(6, Assert.Single(result.Categories).Total); Assert.Equal(2, result.Categories[0].Pending);
        Assert.Equal(6, Assert.Single(result.Days).Total); Assert.Equal(2, result.Days[0].Pending);
    }
    [Fact]
    public void DaysAreChronologicalAndZeroConvertedTotalsAreRepresented()
    {
        var trip = Guid.NewGuid(); var date = new DateOnly(2026, 10, 6);
        ExpenseDto Item(DateOnly day) => new(Guid.NewGuid(), trip, 10, "USD", day, ExpenseCategory.Transport,
            "Tren", null, null, null, null, null, "EUR", 0, false, Guid.Empty);
        var result = ExpensePolicy.Breakdown([Item(date.AddDays(1)), Item(date)], "EUR");
        Assert.Equal([date, date.AddDays(1)], result.Days.Select(x => x.Date));
        Assert.All(result.Days, day => { Assert.Equal(0, day.Total); Assert.Equal(1, day.Pending); });
    }
}

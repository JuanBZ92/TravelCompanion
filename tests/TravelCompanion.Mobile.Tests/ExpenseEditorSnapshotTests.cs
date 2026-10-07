using TravelCompanion.Mobile.Services;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Mobile.Tests;

public sealed class ExpenseEditorSnapshotTests
{
    private static ExpenseEditorSnapshot Initial => ExpenseEditorSnapshot.Create("1000", "JPY", new(2026, 10, 6), ExpenseCategory.Food, "Café", null, null);
    [Fact]
    public void UntouchedWhitespaceAndQuoteRefreshDoNotMakeTheFormDirty() =>
        Assert.Equal(Initial, ExpenseEditorSnapshot.Create(" 1000 ", "JPY", new(2026, 10, 6), ExpenseCategory.Food, " Café ", "", null));
    [Fact]
    public void EachEditableFieldIncludingActivityAndManualRateMakesTheFormDirty()
    {
        var initial = Initial;
        foreach (var changed in new[] { initial with { Amount = "2000" }, initial with { Currency = "EUR" },
            initial with { Date = initial.Date.AddDays(1) }, initial with { Category = ExpenseCategory.Drinks },
            initial with { Concept = "Tren" }, initial with { ManualRate = ".006" }, initial with { ActivityId = Guid.NewGuid() } })
            Assert.NotEqual(initial, changed);
        Assert.Equal(initial, initial with { Amount = "2000" } with { Amount = "1000" });
    }
}

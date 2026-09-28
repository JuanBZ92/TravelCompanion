using TravelCompanion.Shared.Dtos;
namespace TravelCompanion.Shared.Tests;

public sealed class ExpensePolicyTests
{
    [Theory]
    [InlineData("123,45", true)] [InlineData("123.45", true)] [InlineData("-1", false)]
    [InlineData("0", false)] [InlineData("1,234.56", false)] [InlineData("1000000000", false)]
    public void ParsesUnambiguousPositiveAmounts(string input, bool valid) => Assert.Equal(valid, ExpensePolicy.TryAmount(input, out _));
    [Fact]
    public void RejectsFractionsForYenAndInvalidCategory()
    {
        var request = new SaveExpenseRequest(1.5m, "JPY", new(2026, 9, 29), ExpenseCategory.Food, "", null, null, 0, 0, Guid.NewGuid());
        Assert.Throws<ArgumentException>(() => ExpensePolicy.Validate(request));
        Assert.Throws<ArgumentException>(() => ExpensePolicy.Validate(request with { Amount = 1, Category = (ExpenseCategory)99 }));
    }
    [Fact]
    public void RoundsOncePerExpenseAndExcludesDeletedAndPendingConversions()
    {
        var item = new ExpenseDto(Guid.NewGuid(), Guid.NewGuid(), 100, "JPY", new(2026, 9, 29), ExpenseCategory.Other,
            "", null, null, .00505m, new(2026, 9, 29), "manual", "EUR", 1, false, Guid.NewGuid());
        Assert.Equal(.51m, ExpensePolicy.Total([item, item with { Deleted = true }, item with { Rate = null }]));
    }
}

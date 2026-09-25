using TravelCompanion.Mobile.Services;

namespace TravelCompanion.Mobile.Tests;

public class SuggestionScrollOffsetTests
{
    [Theory]
    [InlineData(0, 400, 80, 48, 0)]
    [InlineData(0, 400, 330, 48, 150)]
    [InlineData(150, 400, 330, 48, 150)]
    [InlineData(200, 400, 80, 48, 200)]
    [InlineData(0, 120, 100, 48, 88)]
    public void RevealsSuggestionsWithMinimumForwardScroll(double current, double viewport,
        double top, double height, double expected)
        => Assert.Equal(expected, SuggestionScrollOffset.Calculate(current, viewport, top, height));
}

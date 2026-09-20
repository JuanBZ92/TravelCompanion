using TravelCompanion.Mobile.Services;

namespace TravelCompanion.Mobile.Tests;

public sealed class ItineraryDurationInputTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Empty_duration_is_optional(string? input)
    {
        Assert.True(ItineraryDurationInput.TryParse(input, out var minutes));
        Assert.Null(minutes);
    }

    [Theory]
    [InlineData("15", 15)]
    [InlineData("60", 60)]
    [InlineData("1440", 1440)]
    public void Supplied_duration_is_preserved(string input, int expected)
    {
        Assert.True(ItineraryDurationInput.TryParse(input, out var minutes));
        Assert.Equal(expected, minutes);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("14")]
    [InlineData("1441")]
    [InlineData("abc")]
    public void Invalid_supplied_duration_is_rejected(string input)
    {
        Assert.False(ItineraryDurationInput.TryParse(input, out _));
    }
}

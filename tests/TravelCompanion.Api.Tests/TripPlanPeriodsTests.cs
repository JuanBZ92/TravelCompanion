using TravelCompanion.Api.Services;

namespace TravelCompanion.Api.Tests;

public sealed class TripPlanPeriodsTests
{
    [Theory]
    [InlineData(8, 0, "morning")]
    [InlineData(9, 0, "morning")]
    [InlineData(11, 59, "morning")]
    [InlineData(12, 0, "midday")]
    [InlineData(14, 29, "midday")]
    [InlineData(14, 30, "afternoon")]
    [InlineData(18, 29, "afternoon")]
    [InlineData(18, 30, "night")]
    [InlineData(23, 59, "night")]
    public void Resolve_maps_times_to_expected_period(int hour, int minute, string expectedPeriodKey)
    {
        var period = TripPlanPeriods.Resolve(new TimeOnly(hour, minute));

        Assert.Equal(expectedPeriodKey, period.Key);
    }

    [Fact]
    public void Resolve_handles_subminute_times_at_period_edges()
    {
        Assert.Equal("morning", TripPlanPeriods.Resolve(new TimeOnly(11, 59, 59)).Key);
        Assert.Equal("night", TripPlanPeriods.Resolve(new TimeOnly(23, 59, 59)).Key);
    }
}

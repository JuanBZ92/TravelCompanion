using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Shared.Tests;

public class TripCityScopeTests
{
    [Theory]
    [InlineData(3, "Tokyo")]
    [InlineData(4, "Tokyo,Fukuoka")]
    [InlineData(5, "Fukuoka")]
    [InlineData(8, "")]
    public void Includes_both_cities_on_transfer_date(int day, string expected)
    {
        BuilderTripSetupSegmentDto[] segments =
        [
            new("Fukuoka", new(2026, 10, 4), new(2026, 10, 7)),
            new("Tokyo", new(2026, 10, 1), new(2026, 10, 4))
        ];
        Assert.Equal(expected, string.Join(',', TripCityScope.ForDate(segments, new(2026, 10, day))));
    }

    [Fact]
    public void Trims_and_deduplicates_city_names()
    {
        var date = new DateOnly(2026, 10, 4);
        BuilderTripSetupSegmentDto[] segments = [new(" Tokyo ", date, date), new("tokyo", date, date)];
        Assert.Equal(["Tokyo"], TripCityScope.ForDate(segments, date));
    }
}

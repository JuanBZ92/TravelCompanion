using TravelCompanion.Mobile.Services;

namespace TravelCompanion.Mobile.Tests;

public sealed class ReservationLocalTimeTests
{
    [Theory]
    [InlineData("Europe/Madrid", "Europe/Madrid", 15, 21)]
    [InlineData("Europe/Madrid", "Asia/Tokyo", 22, 21)]
    [InlineData("Asia/Tokyo", "Europe/Madrid", 8, 21)]
    [InlineData("Asia/Tokyo", "Asia/Tokyo", 15, 21)]
    public void Editing_preserves_the_instant(string source, string phone, int hour, int day)
    {
        var result = ReservationLocalTime.ConvertStart(new(2026, 9, 21), new(15, 0), source,
            TimeZoneInfo.FindSystemTimeZoneById(phone));
        Assert.Equal(new DateTime(2026, 9, day, hour, 0, 0), result);
    }

    [Fact]
    public void Conversion_can_cross_midnight()
    {
        Assert.Equal(new DateTime(2026, 9, 20, 18, 0, 0),
            ReservationLocalTime.ConvertStart(new(2026, 9, 21), new(1, 0), "Asia/Tokyo",
                TimeZoneInfo.FindSystemTimeZoneById("Europe/Madrid")));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("Bad/Zone")]
    public void Unknown_zone_is_not_reinterpreted_as_local(string? zone) => Assert.Null(
        ReservationLocalTime.ConvertStart(new(2026, 9, 21), new(15, 0), zone, TimeZoneInfo.Utc));
}

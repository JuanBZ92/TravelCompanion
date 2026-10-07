using System.Text.Json;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Shared.Tests;

public sealed class TravelTimeWindowPolicyTests
{
    [Fact]
    public void Old_guided_criteria_json_remains_compatible_without_window_fields()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var criteria = JsonSerializer.Deserialize<GuidedPlanCriteriaDto>("{\"category\":\"culture\",\"maxDurationMinutes\":60}", options)!;
        Assert.Null(criteria.WindowStartsAtLocal);
        Assert.Null(criteria.WindowEndsAtLocal);
        Assert.Null(criteria.WindowTimeZoneId);
        Assert.DoesNotContain("window", JsonSerializer.Serialize(criteria, options), StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(2026, 3, 29, 2, 30)]
    [InlineData(2026, 10, 25, 2, 30)]
    public void Ambiguous_or_invalid_dst_booking_is_rejected(int year, int month, int day, int hour, int minute)
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Madrid");
        Assert.False(TravelTimeWindowPolicy.TryConvertToTripTime(new(year, month, day, hour, minute, 0),
            "Europe/Madrid", zone, out _));
    }

    [Fact]
    public void Window_requires_trip_wall_times_and_same_date()
    {
        var date = new DateOnly(2026, 10, 8);
        var start = date.ToDateTime(new(10, 0));
        Assert.Null(TravelTimeWindowPolicy.Resolve(date, DateTime.SpecifyKind(start, DateTimeKind.Utc),
            start.AddHours(1), [], start.AddDays(-1)));
        Assert.Null(TravelTimeWindowPolicy.Resolve(date, start, start.AddDays(1), [], start.AddDays(-1)));
    }
}

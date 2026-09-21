using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Shared;

public static class TripCityScope
{
    public static IReadOnlyList<string> ForDate(IEnumerable<BuilderTripSetupSegmentDto> segments, DateOnly date) =>
        segments.Where(segment => segment.StartsOn <= date && date <= segment.EndsOn)
            .OrderBy(segment => segment.StartsOn)
            .Select(segment => segment.City.Trim())
            .Where(city => city.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
}

using TravelCompanion.Api.Models;
using TravelCompanion.Api.Services;
using TravelCompanion.Shared;

namespace TravelCompanion.Api.Tests;

public sealed class PremiumDemoTripFactoryTests
{
    [Fact]
    public void Create_builds_complete_eighteen_day_four_city_trip()
    {
        var firstDate = new DateOnly(2026, 10, 1);
        var trip = PremiumDemoTripFactory.Create(
            Guid.NewGuid(),
            "YUKU Premium",
            Guid.NewGuid(),
            firstDate,
            CreateRecommendations());

        Assert.Equal(firstDate, trip.StartsOn);
        Assert.Equal(firstDate.AddDays(17), trip.EndsOn);
        Assert.Equal(18, trip.DayPlans.Count);
        Assert.Equal(new[] { "Tokyo", "Kyoto", "Osaka", "Fukuoka" },
            trip.DayPlans.Select(day => day.City).Distinct().ToArray());
        Assert.Equal(new[] { 5, 4, 4, 5 },
            trip.DayPlans.GroupBy(day => day.City).Select(group => group.Count()).ToArray());
        Assert.All(trip.DayPlans, day =>
        {
            Assert.False(string.IsNullOrWhiteSpace(day.HotelBase));
            Assert.Equal(4, day.Blocks.Count);
            Assert.All(day.Blocks, block => Assert.NotEmpty(block.Reservations));
        });

        var blocks = trip.DayPlans.SelectMany(day => day.Blocks).ToList();
        Assert.Equal(72, blocks.Count);
        Assert.Equal(10, blocks.Count(block =>
            block.Reservations.Count(item => item.PlanningKind != ScheduleItemKind.ConfirmedReservation) == 2));
        Assert.Equal(10, blocks.Count(block =>
            block.Reservations.Any(item => item.PlanningKind != ScheduleItemKind.ConfirmedReservation)
            && block.Reservations.Any(item => item.PlanningKind == ScheduleItemKind.ConfirmedReservation)));
        Assert.Equal(3, blocks.Count(block => block.Reservations.Count == 1
            && block.Reservations[0].PlanningKind == ScheduleItemKind.ConfirmedReservation));

        var lodgingCities = trip.Reservations
            .Where(item => item.Type == ReservationType.Lodging)
            .Select(item => item.City)
            .ToArray();
        Assert.Equal(new[] { "Tokyo", "Kyoto", "Osaka", "Fukuoka" }, lodgingCities);
    }

    private static IReadOnlyList<Recommendation> CreateRecommendations() =>
        PremiumDemoTripFactory.CitySlugs
            .SelectMany(citySlug => new[]
            {
                CreateRecommendation(citySlug, "Food", 1),
                CreateRecommendation(citySlug, "Culture", 2),
                CreateRecommendation(citySlug, "Nature", 3)
            })
            .ToList();

    private static Recommendation CreateRecommendation(string citySlug, string category, int index) => new()
    {
        Id = Guid.NewGuid(),
        DestinationId = Guid.NewGuid(),
        Title = $"{citySlug}-{category}-{index}",
        Category = category,
        Neighborhood = citySlug,
        CitySlug = citySlug,
        Description = $"Plan de {category} en {citySlug}.",
        Latitude = 35m + index,
        Longitude = 135m + index
    };
}

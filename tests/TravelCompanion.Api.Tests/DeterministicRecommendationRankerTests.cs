using TravelCompanion.Api.Models;
using TravelCompanion.Api.Services;
using TravelCompanion.Shared;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Api.Tests;

public sealed class DeterministicRecommendationRankerTests
{
    [Fact]
    public void Rank_prefers_city_match_that_fits_available_window()
    {
        var ranker = new DeterministicRecommendationRanker();
        var profile = new TravelPreferenceProfile
        {
            UserId = Guid.NewGuid().ToString(),
            Interests = ["Food", "Culture"],
            MaxWalkingMinutes = 25
        };
        var context = new TravelPlanningContext(
            "Tokyo",
            new DateOnly(2026, 10, 6),
            new TimeOnly(11, 0),
            new TimeOnly(13, 0),
            120,
            new GeoPointDto(35.665000m, 139.770000m));
        var nearbyFood = CreateRecommendation(
            "Tsukiji Outer Market",
            "Food",
            "Chuo, Tokyo",
            90,
            35.665486m,
            139.770667m);
        var longMuseum = CreateRecommendation(
            "Long Museum Loop",
            "Culture",
            "Chuo, Tokyo",
            180,
            35.665600m,
            139.770700m);
        var otherCity = CreateRecommendation(
            "Kyoto Food Walk",
            "Food",
            "Gion, Kyoto",
            60,
            35.003700m,
            135.778600m);

        var ranked = ranker.Rank(profile, [], [longMuseum, otherCity, nearbyFood], context);

        Assert.Equal(nearbyFood.Id, ranked[0].Recommendation.Id);
        Assert.Contains(ranked[0].PositiveReasons, reason => reason.Contains("tiempo libre", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(ranked[1].NegativeReasons, reason => reason.Contains("justo", StringComparison.OrdinalIgnoreCase));
    }

    private static Recommendation CreateRecommendation(
        string title,
        string category,
        string neighborhood,
        int durationMinutes,
        decimal latitude,
        decimal longitude)
    {
        return new Recommendation
        {
            Id = Guid.NewGuid(),
            DestinationId = Guid.NewGuid(),
            Title = title,
            Category = category,
            Neighborhood = neighborhood,
            Description = $"{title} description",
            Latitude = latitude,
            Longitude = longitude,
            SuggestedDurationMinutes = durationMinutes,
            AccessLevel = ContentAccessLevel.Free
        };
    }
}

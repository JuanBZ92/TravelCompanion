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

    [Fact]
    public void Rank_penalizes_expensive_low_rated_closed_and_diet_conflicting_food()
    {
        var ranker = new DeterministicRecommendationRanker();
        var profile = new TravelPreferenceProfile
        {
            UserId = Guid.NewGuid().ToString(),
            Interests = ["Food"],
            FoodPreferences = ["local food"],
            DietaryRestrictions = ["vegetarian"],
            BudgetLevel = "low",
            MaxWalkingMinutes = 30
        };
        var context = new TravelPlanningContext(
            "Tokyo",
            new DateOnly(2026, 10, 6),
            new TimeOnly(12, 0),
            new TimeOnly(14, 0),
            120,
            new GeoPointDto(35.665000m, 139.770000m));
        var compatibleCafe = CreateRecommendation(
            "Vegetarian Local Cafe",
            "Food",
            "Chuo, Tokyo",
            60,
            35.665486m,
            139.770667m,
            ["local food", "vegetarian"],
            priceLevel: "low",
            rating: 4.8,
            openingHours: "09:00-17:00");
        var riskyDinner = CreateRecommendation(
            "Kobe Beef Dinner",
            "Food",
            "Chuo, Tokyo",
            60,
            35.665486m,
            139.770667m,
            ["local food", "meat-heavy"],
            priceLevel: "high",
            rating: 3.2,
            openingHours: "18:00-23:00");

        var ranked = ranker.Rank(profile, [], [riskyDinner, compatibleCafe], context);

        Assert.Equal(compatibleCafe.Id, ranked[0].Recommendation.Id);
        var riskyScore = Assert.Single(ranked, scored => scored.Recommendation.Id == riskyDinner.Id);
        Assert.Contains(riskyScore.NegativeReasons, reason => reason.Contains("presupuesto", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(riskyScore.NegativeReasons, reason => reason.Contains("restricciones alimentarias", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(riskyScore.NegativeReasons, reason => reason.Contains("abierto", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(riskyScore.NegativeReasons, reason => reason.Contains("valoracion", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Rank_penalizes_duplicates_from_existing_reservations()
    {
        var ranker = new DeterministicRecommendationRanker();
        var profile = new TravelPreferenceProfile
        {
            UserId = Guid.NewGuid().ToString(),
            Interests = ["Culture"],
            MaxWalkingMinutes = 30
        };
        var context = new TravelPlanningContext(
            "Tokyo",
            new DateOnly(2026, 10, 6),
            new TimeOnly(13, 0),
            new TimeOnly(15, 0),
            120,
            new GeoPointDto(35.665000m, 139.770000m));
        var duplicate = CreateRecommendation(
            "TeamLab Planets",
            "Culture",
            "Toyosu, Tokyo",
            60,
            35.665486m,
            139.770667m,
            ["immersive art"]);
        var freshOption = CreateRecommendation(
            "Small Gallery Walk",
            "Culture",
            "Ginza, Tokyo",
            60,
            35.665486m,
            139.770667m,
            ["art"]);
        var reservations = new[]
        {
            CreateReservation("TeamLab Planets", "Toyosu")
        };

        var ranked = ranker.Rank(profile, reservations, [duplicate, freshOption], context);

        Assert.Equal(freshOption.Id, ranked[0].Recommendation.Id);
        var duplicateScore = Assert.Single(ranked, scored => scored.Recommendation.Id == duplicate.Id);
        Assert.Contains(duplicateScore.NegativeReasons, reason => reason.Contains("itinerario", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Rank_uses_tags_for_dislikes()
    {
        var ranker = new DeterministicRecommendationRanker();
        var profile = new TravelPreferenceProfile
        {
            UserId = Guid.NewGuid().ToString(),
            Interests = ["Culture"],
            Dislikes = ["shopping"],
            MaxWalkingMinutes = 30
        };
        var context = new TravelPlanningContext(
            "Tokyo",
            new DateOnly(2026, 10, 6),
            new TimeOnly(13, 0),
            new TimeOnly(15, 0),
            120,
            new GeoPointDto(35.665000m, 139.770000m));
        var shoppingTagged = CreateRecommendation(
            "Craft Arcade",
            "Culture",
            "Ginza, Tokyo",
            60,
            35.665486m,
            139.770667m,
            ["shopping"]);
        var neutral = CreateRecommendation(
            "Pocket Museum",
            "Culture",
            "Ginza, Tokyo",
            60,
            35.665486m,
            139.770667m,
            ["history"]);

        var ranked = ranker.Rank(profile, [], [shoppingTagged, neutral], context);

        Assert.Equal(neutral.Id, ranked[0].Recommendation.Id);
        var penalized = Assert.Single(ranked, scored => scored.Recommendation.Id == shoppingTagged.Id);
        Assert.Contains(penalized.NegativeReasons, reason => reason.Contains("evitar", StringComparison.OrdinalIgnoreCase));
    }

    private static Recommendation CreateRecommendation(
        string title,
        string category,
        string neighborhood,
        int durationMinutes,
        decimal latitude,
        decimal longitude,
        List<string>? tags = null,
        string priceLevel = "medium",
        double? rating = null,
        string? openingHours = null)
    {
        return new Recommendation
        {
            Id = Guid.NewGuid(),
            DestinationId = Guid.NewGuid(),
            Title = title,
            Category = category,
            Neighborhood = neighborhood,
            Description = $"{title} description",
            Tags = tags ?? [],
            PriceLevel = priceLevel,
            Latitude = latitude,
            Longitude = longitude,
            SuggestedDurationMinutes = durationMinutes,
            Rating = rating,
            OpeningHours = openingHours,
            AccessLevel = ContentAccessLevel.Free
        };
    }

    private static Reservation CreateReservation(string title, string locationName)
    {
        return new Reservation
        {
            Id = Guid.NewGuid(),
            TripId = Guid.NewGuid(),
            Date = new DateOnly(2026, 10, 6),
            StartsAt = new TimeOnly(9, 0),
            Title = title,
            City = "Tokyo",
            LocationName = locationName,
            Address = "Tokyo",
            ConfirmationCode = "TEST",
            Notes = "Test reservation"
        };
    }
}

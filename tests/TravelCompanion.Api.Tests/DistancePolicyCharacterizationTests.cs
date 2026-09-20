using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TravelCompanion.Api.Controllers;
using TravelCompanion.Api.Data;
using TravelCompanion.Api.Models;
using TravelCompanion.Api.Options;
using TravelCompanion.Api.Services;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Api.Tests;

public sealed class DistancePolicyCharacterizationTests
{
    [Theory]
    [InlineData(0, 0, 0, 0, 0)]
    [InlineData(0, 0, 0, 1, 111.195)]
    [InlineData(0, 179, 0, -179, 222.390)]
    [InlineData(90, 0, 90, 90, 0)]
    public void Consumers_keep_their_distinct_rounding(decimal latitude, decimal longitude,
        decimal targetLatitude, decimal targetLongitude, decimal expectedFree)
    {
        Assert.Equal(expectedFree, FreeMapPreviewService.CalculateDistanceKm(latitude, longitude, targetLatitude, targetLongitude));
        var args = new object[] { latitude, longitude, targetLatitude, targetLongitude };
        var catalog = (decimal)Call(typeof(RecommendationsController), "CalculateDistanceKm", args)!;
        var ranker = (double)Call(typeof(DeterministicRecommendationRanker), "CalculateDistanceKm", args)!;
        // Known values assert each policy independently, without rounding the already rounded free result.
        var expectedCatalog = expectedFree switch { 111.195m => 111.19m, 222.390m => 222.39m, _ => 0m };
        Assert.Equal(expectedCatalog, catalog);
        Assert.Equal((double)expectedCatalog, ranker);
        var place = Place("Target", targetLongitude);
        place.Latitude = targetLatitude;
        var today = Call(typeof(TodayRecommendationService), "CalculateDistanceKm", new GeoPointDto(latitude, longitude), place);
        Assert.Equal(expectedFree switch { 111.195m => 111.2m, 222.390m => 222.4m, _ => 0m }, today);
    }

    [Fact]
    public void Today_keeps_missing_origin_unknown() =>
        Assert.Null(Call(typeof(TodayRecommendationService), "CalculateDistanceKm", null, Place("No origin", 0)));

    [Fact]
    public async Task Free_boundary_and_map_order_use_three_decimals()
    {
        await using var db = new TravelCompanionDbContext(new DbContextOptionsBuilder<TravelCompanionDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var destinationId = Guid.NewGuid();
        var inside = Place("Inside", 0.01797744m);
        var boundaryB = Place("Boundary B", 0.01798643m);
        var boundaryA = Place("Boundary A", 0.01798643m);
        var outside = Place("Outside", 0.01799542m);
        Recommendation[] places = [outside, boundaryB, inside, boundaryA];
        foreach (var place in places) place.DestinationId = destinationId;
        db.FreeMapCities.Add(new FreeMapCity
        {
            Id = Guid.NewGuid(), DestinationId = destinationId, CitySlug = "tokyo", DisplayName = "Tokyo",
            FreeRadiusKm = 2m, CoverageRadiusKm = 10m
        });
        db.Recommendations.AddRange(places);
        await db.SaveChangesAsync();
        Assert.Equal(1.999m, FreeMapPreviewService.CalculateDistanceKm(0, 0, 0, inside.Longitude));
        Assert.Equal(2m, FreeMapPreviewService.CalculateDistanceKm(0, 0, 0, boundaryA.Longitude));
        Assert.Equal(2.001m, FreeMapPreviewService.CalculateDistanceKm(0, 0, 0, outside.Longitude));
        var options = Microsoft.Extensions.Options.Options.Create(new FreePreviewOptions { MarkerObfuscationKey = "test-only" });
        var allowed = await new FreeTrialAccessService(db, options, NullLogger<FreeTrialAccessService>.Instance)
            .FilterToFreeRadiusAsync(places, destinationId);
        Assert.Equal(new[] { boundaryB.Id, inside.Id, boundaryA.Id }, allowed.Select(item => item.Id));
        var map = await new FreeMapPreviewService(db, options, null!).GetCityAsync("tokyo");
        Assert.NotNull(map);
        Assert.Equal(3, map.UnlockedCount);
        Assert.Equal(1, map.LockedCount);
        Assert.Equal(new[] { "Inside", "Boundary A", "Boundary B" },
            map.Markers.Where(item => item.Access == FreeMapMarkerAccess.Unlocked).Select(item => item.Recommendation!.Title));
        Assert.Null(Assert.Single(map.Markers, item => item.Access == FreeMapMarkerAccess.Locked).Recommendation);
    }

    private static Recommendation Place(string title, decimal longitude) => new()
    {
        Id = Guid.NewGuid(), Title = title, Category = "Art", Neighborhood = "Tokyo", CitySlug = "tokyo",
        Description = "Gallery", Longitude = longitude
    };

    private static object? Call(Type type, string method, params object?[] args) =>
        type.GetMethod(method, BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, args);
}

using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TravelCompanion.Api.Data;
using TravelCompanion.Api.Models;
using TravelCompanion.Api.Services;

namespace TravelCompanion.Api.Tests;

public sealed class TripPlanEditorServiceTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    [Fact]
    public async Task Create_prefills_city_segments_and_inherits_hotel_base()
    {
        await using var dbContext = CreateDbContext();
        var destination = await SeedDestinationAsync(dbContext);
        var service = CreateService(dbContext);

        var tripId = await service.CreateTripAsync(new CreateTripPlanCommand(
            "Cliente multicity",
            "8642",
            destination.Id,
            new DateOnly(2026, 10, 1),
            new DateOnly(2026, 10, 5),
            "Asia/Tokyo",
            [
                new CreateTripCitySegment("Tokyo", new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 3), "Hotel Tokyo"),
                new CreateTripCitySegment("Kyoto", new DateOnly(2026, 10, 4), new DateOnly(2026, 10, 5))
            ]));

        var editor = (await service.GetEditorAsync(tripId))!;

        Assert.Equal(["Tokyo", "Tokyo", "Tokyo", "Kyoto", "Kyoto"], editor.Payload.Days.Select(day => day.City));
        Assert.All(editor.Payload.Days, day => Assert.Equal("Hotel Tokyo", day.HotelBase));
    }

    [Fact]
    public async Task Create_rejects_city_segments_with_date_gaps()
    {
        await using var dbContext = CreateDbContext();
        var destination = await SeedDestinationAsync(dbContext);
        var service = CreateService(dbContext);

        var exception = await Assert.ThrowsAsync<ValidationException>(() => service.CreateTripAsync(new CreateTripPlanCommand(
            "Cliente multicity",
            "9753",
            destination.Id,
            new DateOnly(2026, 10, 1),
            new DateOnly(2026, 10, 5),
            "Asia/Tokyo",
            [
                new CreateTripCitySegment("Tokyo", new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 2)),
                new CreateTripCitySegment("Kyoto", new DateOnly(2026, 10, 4), new DateOnly(2026, 10, 5))
            ])));

        Assert.Contains("sin huecos", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Draft_is_not_materialized_until_publish()
    {
        await using var dbContext = CreateDbContext();
        var destination = await SeedDestinationAsync(dbContext);
        var recommendation = await SeedRecommendationAsync(dbContext, destination.Id);
        var service = CreateService(dbContext);
        var tripId = await service.CreateTripAsync(new CreateTripPlanCommand(
            "Ivana & Manu",
            "1908",
            destination.Id,
            new DateOnly(2026, 11, 12),
            new DateOnly(2026, 11, 13),
            "Asia/Tokyo"));

        var editor = await service.GetEditorAsync(tripId);
        Assert.NotNull(editor);
        editor.Payload.Days[0].City = "Tokyo";
        editor.Payload.Days[1].City = "Tokyo";
        editor.Payload.Days[0].Blocks[0].CuratedDescription = "Café tranquilo cerca del hotel.";
        editor.Payload.Days[0].Blocks[0].Recommendations.Add(new TripPlanRecommendationDraft
        {
            Id = Guid.NewGuid(),
            RecommendationId = recommendation.Id
        });
        var json = JsonSerializer.Serialize(editor.Payload, JsonOptions);

        var save = await service.SaveDraftAsync(tripId, json, editor.BasePlanRevision, null);

        Assert.True(save.Success);
        var draftTrip = await dbContext.Trips.AsNoTracking().SingleAsync(item => item.Id == tripId);
        Assert.Equal(TripPublicationStatus.Draft, draftTrip.PublicationStatus);
        Assert.Null(draftTrip.AccessPinHash);
        Assert.Empty(await dbContext.TripDayPlans.ToListAsync());
        Assert.Empty(await dbContext.Reservations.ToListAsync());

        var publish = await service.PublishAsync(tripId, json, editor.BasePlanRevision, null);

        Assert.True(publish.Success, publish.Message);
        var published = await dbContext.Trips
            .AsNoTracking()
            .Include(item => item.DayPlans)
                .ThenInclude(day => day.Blocks)
            .Include(item => item.Reservations)
            .SingleAsync(item => item.Id == tripId);
        Assert.Equal(TripPublicationStatus.Published, published.PublicationStatus);
        Assert.NotNull(published.AccessPinHash);
        Assert.Equal(1, published.PlanRevision);
        Assert.Equal(2, published.DayPlans.Count);
        Assert.All(published.DayPlans, day => Assert.Equal(4, day.Blocks.Count));
        var savedRecommendation = Assert.Single(published.Reservations);
        Assert.Equal(recommendation.Id, savedRecommendation.RecommendationId);
        Assert.NotNull(savedRecommendation.TripDayBlockId);
        Assert.Equal("Café tranquilo cerca del hotel.", published.DayPlans
            .Single(day => day.DayNumber == 1)
            .Blocks.Single(block => block.PeriodKey == "morning")
            .CuratedDescription);
        Assert.Null(await dbContext.TripPlanDrafts.SingleOrDefaultAsync(item => item.TripId == tripId));
    }

    [Fact]
    public async Task Publish_rejects_stale_draft_revision()
    {
        await using var dbContext = CreateDbContext();
        var destination = await SeedDestinationAsync(dbContext);
        var service = CreateService(dbContext);
        var tripId = await service.CreateTripAsync(new CreateTripPlanCommand(
            "Cliente",
            "2468",
            destination.Id,
            new DateOnly(2026, 10, 1),
            new DateOnly(2026, 10, 1),
            "Asia/Tokyo"));
        var editor = (await service.GetEditorAsync(tripId))!;
        editor.Payload.Days[0].City = "Tokyo";
        var json = JsonSerializer.Serialize(editor.Payload, JsonOptions);
        Assert.True((await service.SaveDraftAsync(tripId, json, 0, null)).Success);

        var trip = await dbContext.Trips.SingleAsync(item => item.Id == tripId);
        trip.PlanRevision = 1;
        await dbContext.SaveChangesAsync();

        var result = await service.PublishAsync(tripId, json, 0, null);

        Assert.False(result.Success);
        Assert.Contains("cambió", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(TripPublicationStatus.Draft, trip.PublicationStatus);
    }

    [Fact]
    public async Task Draft_rejects_more_than_three_recommendations_in_a_block()
    {
        await using var dbContext = CreateDbContext();
        var destination = await SeedDestinationAsync(dbContext);
        var recommendations = new List<Recommendation>();
        for (var index = 0; index < 4; index++)
        {
            recommendations.Add(await SeedRecommendationAsync(dbContext, destination.Id, $"Cafe {index}"));
        }
        var service = CreateService(dbContext);
        var tripId = await service.CreateTripAsync(new CreateTripPlanCommand(
            "Cliente",
            "1357",
            destination.Id,
            new DateOnly(2026, 10, 1),
            new DateOnly(2026, 10, 1),
            "Asia/Tokyo"));
        var editor = (await service.GetEditorAsync(tripId))!;
        editor.Payload.Days[0].Blocks[0].Recommendations = recommendations.Select(item => new TripPlanRecommendationDraft
        {
            Id = Guid.NewGuid(),
            RecommendationId = item.Id
        }).ToList();

        var result = await service.SaveDraftAsync(
            tripId,
            JsonSerializer.Serialize(editor.Payload, JsonOptions),
            editor.BasePlanRevision,
            null);

        Assert.False(result.Success);
        Assert.Contains("hasta 3", result.Message);
    }

    private static TripPlanEditorService CreateService(TravelCompanionDbContext dbContext) =>
        new(dbContext, new PasswordHasher<Trip>(), NullLogger<TripPlanEditorService>.Instance);

    private static TravelCompanionDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<TravelCompanionDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new TravelCompanionDbContext(options);
    }

    private static async Task<Destination> SeedDestinationAsync(TravelCompanionDbContext dbContext)
    {
        var destination = new Destination
        {
            Id = Guid.NewGuid(),
            Name = "Japan",
            Slug = $"japan-{Guid.NewGuid():N}",
            Country = "Japan",
            TimeZoneId = "Asia/Tokyo",
            HeroImageUrl = string.Empty,
            ShortDescription = string.Empty
        };
        dbContext.Destinations.Add(destination);
        await dbContext.SaveChangesAsync();
        return destination;
    }

    private static async Task<Recommendation> SeedRecommendationAsync(
        TravelCompanionDbContext dbContext,
        Guid destinationId,
        string title = "Woodberry Coffee")
    {
        var recommendation = new Recommendation
        {
            Id = Guid.NewGuid(),
            DestinationId = destinationId,
            Title = title,
            Category = "Food",
            Neighborhood = "Tokyo, Japan",
            CitySlug = "tokyo",
            Description = "Café recomendado.",
            Tags = ["food", "cafe", "breakfast"],
            PriceLevel = "low",
            SuggestedDurationMinutes = 60,
            Latitude = 35.681m,
            Longitude = 139.767m
        };
        dbContext.Recommendations.Add(recommendation);
        await dbContext.SaveChangesAsync();
        return recommendation;
    }
}

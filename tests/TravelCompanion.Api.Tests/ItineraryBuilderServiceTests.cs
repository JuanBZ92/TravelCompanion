using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using TravelCompanion.Api.Data;
using TravelCompanion.Api.Models;
using TravelCompanion.Api.Services;
using TravelCompanion.Shared;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Api.Tests;

public sealed class ItineraryBuilderServiceTests
{
    [Fact]
    public async Task Builder_setup_creates_blank_blocks_and_traveler_can_add_yuku_item()
    {
        await using var dbContext = CreateDbContext();
        var destination = CreateDestination();
        var user = CreateUser();
        var grant = new BuilderAccessGrant
        {
            Id = Guid.NewGuid(),
            AppUserId = user.Id,
            AppUser = user,
            DestinationId = destination.Id,
            Destination = destination,
            PinHash = "test"
        };
        dbContext.AddRange(destination, user, grant);
        await dbContext.SaveChangesAsync();

        var sessionService = new UserSessionService(dbContext);
        var (_, token) = await sessionService.CreateSessionAsync(user, accessMode: SessionAccessMode.Builder);
        var httpContext = CreateHttpContext(token);
        var setupService = new BuilderTripService(dbContext, sessionService);
        var startsOn = new DateOnly(2026, 10, 1);
        var setup = await setupService.SaveAsync(httpContext, new SaveBuilderTripSetupRequest(
            startsOn,
            startsOn.AddDays(2),
            "Asia/Tokyo",
            0,
            [new BuilderTripSetupSegmentDto("Tokyo", startsOn, startsOn.AddDays(2), "Hotel Test")]),
            CancellationToken.None);

        Assert.True(setup.IsConfigured);
        var trip = await dbContext.Trips.Include(item => item.DayPlans).ThenInclude(day => day.Blocks).SingleAsync();
        Assert.Equal(ExperienceMode.SelfServiceBuilder, trip.ExperienceMode);
        Assert.Equal(3, trip.DayPlans.Count);
        Assert.All(trip.DayPlans, day =>
        {
            Assert.Equal(4, day.Blocks.Count);
            Assert.All(day.Blocks, block => Assert.False(block.AutofillEnabled));
        });

        var recommendation = new Recommendation
        {
            Id = Guid.NewGuid(),
            DestinationId = destination.Id,
            Title = "Test cafe",
            Category = "Food",
            Neighborhood = "Tokyo, Japan",
            Description = "Test",
            Tags = ["cafe"],
            PriceLevel = "low",
            Latitude = 35.6m,
            Longitude = 139.7m,
            SuggestedDurationMinutes = 45,
            AccessLevel = ContentAccessLevel.Free
        };
        dbContext.Recommendations.Add(recommendation);
        await dbContext.SaveChangesAsync();

        var accessService = new TravelerAccessService(sessionService);
        var itineraryService = new TravelerItineraryService(dbContext, accessService);
        var result = await itineraryService.CreateAsync(httpContext, new ItineraryItemMutationRequest(
            recommendation.Id, null, recommendation.Title, startsOn, "morning", false, null, null,
            "Tokyo", recommendation.Title, recommendation.Neighborhood, null,
            recommendation.Latitude, recommendation.Longitude, setup.Revision, "test-create"));

        Assert.True(result.Success);
        Assert.Equal(ItineraryItemOwner.Traveler, result.Item?.Owner);
        Assert.Equal(ItineraryItemSource.YukuRecommendation, result.Item?.ItemSource);
        Assert.Equal(ItineraryTimePrecision.PeriodOnly, result.Item?.TimePrecision);
        Assert.Equal(recommendation.Id, result.Item?.RecommendationId);
    }

    private static TravelCompanionDbContext CreateDbContext() => new(
        new DbContextOptionsBuilder<TravelCompanionDbContext>()
            .UseInMemoryDatabase($"builder-{Guid.NewGuid():N}")
            .Options);

    private static DefaultHttpContext CreateHttpContext(string token)
    {
        var context = new DefaultHttpContext();
        context.Request.Headers.Authorization = $"Bearer {token}";
        return context;
    }

    private static AppUser CreateUser() => new()
    {
        Id = Guid.NewGuid(),
        Email = "builder@test.local",
        DisplayName = "Builder Test",
        MustChangePassword = false
    };

    private static Destination CreateDestination() => new()
    {
        Id = Guid.NewGuid(),
        Name = "Japon",
        Slug = "japon",
        Country = "Japan",
        TimeZoneId = "Asia/Tokyo",
        HeroImageUrl = string.Empty,
        ShortDescription = string.Empty
    };
}

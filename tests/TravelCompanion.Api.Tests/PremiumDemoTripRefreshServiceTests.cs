using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TravelCompanion.Api.Data;
using TravelCompanion.Api.Models;
using TravelCompanion.Api.Services;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Api.Tests;

public sealed class PremiumDemoTripRefreshServiceTests
{
    [Fact]
    public async Task Refresh_replaces_only_the_trip_using_pin_2222()
    {
        await using var db = CreateDb();
        var destination = new Destination
        {
            Id = Guid.NewGuid(),
            Name = "Japon",
            Slug = "japon",
            Country = "Japan",
            TimeZoneId = "Asia/Tokyo",
            HeroImageUrl = string.Empty,
            ShortDescription = string.Empty
        };
        var premiumUser = CreateUser("premium-demo@travelcompanion.local", "YUKU Premium");
        var otherUser = CreateUser("other@example.test", "Other");
        var premiumTrip = CreateTrip(premiumUser, destination, "Old premium trip", "2222");
        var otherTrip = CreateTrip(otherUser, destination, "Untouched trip", "3333");
        db.AddRange(destination, premiumUser, otherUser, premiumTrip, otherTrip);
        db.Recommendations.AddRange(CreateRecommendations(destination.Id));
        await db.SaveChangesAsync();

        var result = await new PremiumDemoTripRefreshService(db).RefreshAsync();

        Assert.False(result.Created);
        Assert.Equal(premiumTrip.Id, result.TripId);
        Assert.Equal(18, result.DayCount);
        Assert.Equal(4, result.CityCount);
        Assert.Equal(72, result.BlockCount);
        Assert.Equal(92, result.ReservationCount);
        var refreshed = await db.Trips.AsNoTracking()
            .Include(item => item.DayPlans).ThenInclude(day => day.Blocks)
            .Include(item => item.Reservations)
            .SingleAsync(item => item.Id == premiumTrip.Id);
        Assert.Equal(18, refreshed.DayPlans.Count);
        Assert.Equal(92, refreshed.Reservations.Count);
        Assert.True(refreshed.PlanRevision > 7);
        var untouched = await db.Trips.AsNoTracking().SingleAsync(item => item.Id == otherTrip.Id);
        Assert.Equal("Untouched trip", untouched.TravelerName);
        Assert.Equal(otherTrip.StartsOn, untouched.StartsOn);
        Assert.Equal(7, untouched.PlanRevision);
    }

    private static TravelCompanionDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<TravelCompanionDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new TravelCompanionDbContext(options);
    }

    private static AppUser CreateUser(string email, string name) => new()
    {
        Id = Guid.NewGuid(),
        Email = email,
        DisplayName = name,
        PasswordHash = string.Empty,
        MustChangePassword = false
    };

    private static Trip CreateTrip(AppUser user, Destination destination, string name, string pin)
    {
        var trip = new Trip
        {
            Id = Guid.NewGuid(),
            AppUserId = user.Id,
            AppUser = user,
            DestinationId = destination.Id,
            Destination = destination,
            TravelerName = name,
            StartsOn = new DateOnly(2026, 1, 1),
            EndsOn = new DateOnly(2026, 1, 3),
            TimeZoneId = "Asia/Tokyo",
            ExperienceMode = ExperienceMode.CuratedPremium,
            PublicationStatus = TripPublicationStatus.Published,
            PlanRevision = 7
        };
        trip.AccessPinHash = new PasswordHasher<Trip>().HashPassword(trip, pin);
        return trip;
    }

    private static IReadOnlyList<Recommendation> CreateRecommendations(Guid destinationId) =>
        PremiumDemoTripFactory.CitySlugs
            .SelectMany(citySlug => new[]
            {
                CreateRecommendation(destinationId, citySlug, "Food", 1),
                CreateRecommendation(destinationId, citySlug, "Culture", 2),
                CreateRecommendation(destinationId, citySlug, "Nature", 3)
            })
            .ToList();

    private static Recommendation CreateRecommendation(Guid destinationId, string citySlug, string category, int index) => new()
    {
        Id = Guid.NewGuid(),
        DestinationId = destinationId,
        ExternalId = $"{citySlug}-{category}-{index}",
        Title = $"{citySlug}-{category}-{index}",
        Category = category,
        Neighborhood = citySlug,
        CitySlug = citySlug,
        Description = $"Plan de {category} en {citySlug}.",
        Latitude = 35m + index,
        Longitude = 135m + index
    };
}

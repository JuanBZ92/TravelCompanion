using Microsoft.EntityFrameworkCore;
using TravelCompanion.Api.Data;
using TravelCompanion.Api.Models;
using TravelCompanion.Api.Services;
using TravelCompanion.Shared;

namespace TravelCompanion.Api.Tests;

public sealed class ExternalPlaceInsightsServiceTests
{
    [Fact]
    public async Task Report_groups_google_places_and_marks_existing_catalog_items()
    {
        await using var dbContext = CreateDbContext();
        var destination = CreateDestination();
        var user = new AppUser
        {
            Id = Guid.NewGuid(),
            Email = "traveler@test.local",
            DisplayName = "Traveler",
            MustChangePassword = false
        };
        var trip = new Trip
        {
            Id = Guid.NewGuid(),
            AppUserId = user.Id,
            AppUser = user,
            DestinationId = destination.Id,
            Destination = destination,
            TravelerName = "Viaje Test",
            StartsOn = new DateOnly(2026, 10, 1),
            EndsOn = new DateOnly(2026, 10, 5),
            TimeZoneId = "Asia/Tokyo"
        };
        var catalogPlaceId = "catalog-place";
        var pendingPlaceId = "pending-place";

        dbContext.AddRange(destination, user, trip,
            CreateReservation(trip, pendingPlaceId, "Donuts Tokyo", new DateOnly(2026, 10, 2)),
            CreateReservation(trip, pendingPlaceId, "Donuts Tokyo", new DateOnly(2026, 10, 3)),
            CreateReservation(trip, catalogPlaceId, "Cafe Catalogado", new DateOnly(2026, 10, 4)),
            new Recommendation
            {
                Id = Guid.NewGuid(),
                DestinationId = destination.Id,
                ProviderPlaceId = catalogPlaceId,
                Title = "Cafe Catalogado",
                Category = "Food",
                Neighborhood = "Tokyo",
                Description = "Cafe",
                PriceLevel = "medium",
                SuggestedDurationMinutes = 45
            });
        await dbContext.SaveChangesAsync();

        var report = await new ExternalPlaceInsightsService(dbContext).GetReportAsync();

        Assert.Equal(3, report.SavedCount);
        Assert.Equal(2, report.UniquePlaceCount);
        Assert.Equal(1, report.PendingReviewCount);
        Assert.Equal(1, report.TripCount);
        var pending = Assert.Single(report.Places, item => item.ProviderPlaceId == pendingPlaceId);
        Assert.Equal(2, pending.SavedCount);
        Assert.False(pending.IsInCatalog);
        Assert.Contains("query_place_id=pending-place", pending.GoogleMapsUrl);
        Assert.True(Assert.Single(report.Places, item => item.ProviderPlaceId == catalogPlaceId).IsInCatalog);
    }

    private static Reservation CreateReservation(Trip trip, string placeId, string title, DateOnly date) => new()
    {
        Id = Guid.NewGuid(),
        TripId = trip.Id,
        Trip = trip,
        ItemSource = ItineraryItemSource.GooglePlace,
        Owner = ItineraryItemOwner.Traveler,
        ProviderPlaceId = placeId,
        Date = date,
        StartsAt = new TimeOnly(12, 0),
        Title = title,
        City = "Tokyo",
        LocationName = title,
        Address = string.Empty,
        ConfirmationCode = string.Empty,
        Notes = string.Empty
    };

    private static TravelCompanionDbContext CreateDbContext() => new(
        new DbContextOptionsBuilder<TravelCompanionDbContext>()
            .UseInMemoryDatabase($"external-places-{Guid.NewGuid():N}")
            .Options);

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

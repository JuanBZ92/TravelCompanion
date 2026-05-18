using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using TravelCompanion.Api.Data;
using TravelCompanion.Api.Models;
using TravelCompanion.Api.Pages.Admin;
using TravelCompanion.Shared;

namespace TravelCompanion.Api.Tests;

public sealed class AdminReservationsPageTests
{
    [Fact]
    public async Task Save_trip_uses_user_display_name_when_traveler_is_empty()
    {
        await using var dbContext = CreateDbContext();
        var userId = Guid.NewGuid();
        var destinationId = Guid.NewGuid();

        dbContext.AppUsers.Add(new AppUser
        {
            Id = userId,
            Email = "demo@example.com",
            DisplayName = "Demo Traveler"
        });
        dbContext.Destinations.Add(new Destination
        {
            Id = destinationId,
            Name = "Japan",
            Slug = "japan",
            Country = "Japan",
            HeroImageUrl = "https://example.com/japan.jpg",
            ShortDescription = "Demo destination"
        });
        await dbContext.SaveChangesAsync();

        var page = new ReservationsModel(dbContext)
        {
            PageContext = new PageContext
            {
                HttpContext = new DefaultHttpContext()
            },
            TripInput = new ReservationsModel.TripForm
            {
                UserId = userId,
                DestinationId = destinationId,
                TravelerName = string.Empty,
                StartsOn = new DateOnly(2026, 5, 20),
                EndsOn = new DateOnly(2026, 5, 27)
            }
        };

        await page.OnPostSaveTripAsync();

        var trip = await dbContext.Trips.SingleAsync();
        Assert.Equal("Demo Traveler", trip.TravelerName);
    }

    [Fact]
    public async Task Save_reservation_ignores_trip_form_validation()
    {
        await using var dbContext = CreateDbContext();
        var userId = Guid.NewGuid();
        var destinationId = Guid.NewGuid();
        var tripId = Guid.NewGuid();

        dbContext.AppUsers.Add(new AppUser
        {
            Id = userId,
            Email = "demo@example.com",
            DisplayName = "Demo Traveler"
        });
        dbContext.Destinations.Add(new Destination
        {
            Id = destinationId,
            Name = "Japan",
            Slug = "japan",
            Country = "Japan",
            HeroImageUrl = "https://example.com/japan.jpg",
            ShortDescription = "Demo destination"
        });
        dbContext.Trips.Add(new Trip
        {
            Id = tripId,
            AppUserId = userId,
            DestinationId = destinationId,
            TravelerName = "Demo Traveler",
            StartsOn = new DateOnly(2026, 5, 20),
            EndsOn = new DateOnly(2026, 5, 27)
        });
        await dbContext.SaveChangesAsync();

        var page = new ReservationsModel(dbContext)
        {
            PageContext = new PageContext
            {
                HttpContext = new DefaultHttpContext()
            },
            Input = new ReservationsModel.ReservationInput
            {
                TripId = tripId,
                Type = ReservationType.Event,
                Date = new DateOnly(2026, 5, 21),
                StartsAt = new TimeOnly(10, 0),
                Title = "Museum visit",
                City = "Tokyo",
                LocationName = "Museum"
            },
            TripInput = new ReservationsModel.TripForm()
        };
        page.ModelState.AddModelError("TripInput.TravelerName", "El viajero es obligatorio.");

        await page.OnPostSaveAsync();

        var reservation = await dbContext.Reservations.SingleAsync();
        Assert.Equal("Museum visit", reservation.Title);
        Assert.True(page.ModelState.IsValid);
    }

    private static TravelCompanionDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<TravelCompanionDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new TravelCompanionDbContext(options);
    }
}

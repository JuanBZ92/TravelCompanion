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
        Assert.Equal(ScheduleItemKind.ManualEvent, reservation.PlanningKind);
        Assert.True(page.ModelState.IsValid);
    }

    [Fact]
    public async Task Save_linked_recommendation_classifies_it_as_recommendation()
    {
        await using var dbContext = CreateDbContext();
        var destinationId = Guid.NewGuid();
        var tripId = Guid.NewGuid();
        var recommendationId = Guid.NewGuid();
        dbContext.Destinations.Add(new Destination
        {
            Id = destinationId,
            Name = "Japan",
            Slug = "japan",
            Country = "Japan",
            HeroImageUrl = string.Empty,
            ShortDescription = string.Empty
        });
        dbContext.Trips.Add(new Trip
        {
            Id = tripId,
            DestinationId = destinationId,
            TravelerName = "Demo Traveler",
            StartsOn = new DateOnly(2026, 5, 20),
            EndsOn = new DateOnly(2026, 5, 27)
        });
        dbContext.Recommendations.Add(new Recommendation
        {
            Id = recommendationId,
            DestinationId = destinationId,
            Title = "Ramen One",
            Category = "Food",
            Neighborhood = "Tokyo, Japan",
            Description = "Ramen recomendado.",
            Latitude = 35.67m,
            Longitude = 139.76m,
            SuggestedDurationMinutes = 60,
            AccessLevel = ContentAccessLevel.Free
        });
        await dbContext.SaveChangesAsync();

        var page = new ReservationsModel(dbContext)
        {
            PageContext = new PageContext { HttpContext = new DefaultHttpContext() },
            Input = new ReservationsModel.ReservationInput
            {
                TripId = tripId,
                RecommendationId = recommendationId,
                Type = ReservationType.Event,
                Date = new DateOnly(2026, 5, 21),
                StartsAt = new TimeOnly(12, 0)
            }
        };

        await page.OnPostSaveAsync();

        var item = await dbContext.Reservations.SingleAsync();
        Assert.Equal(ScheduleItemKind.Recommendation, item.PlanningKind);
        Assert.Equal(recommendationId, item.RecommendationId);
    }

    [Fact]
    public async Task Save_flight_classifies_it_as_confirmed_reservation()
    {
        await using var dbContext = CreateDbContext();
        var destinationId = Guid.NewGuid();
        var tripId = Guid.NewGuid();
        dbContext.Destinations.Add(new Destination
        {
            Id = destinationId,
            Name = "Japan",
            Slug = "japan",
            Country = "Japan",
            HeroImageUrl = string.Empty,
            ShortDescription = string.Empty
        });
        dbContext.Trips.Add(new Trip
        {
            Id = tripId,
            DestinationId = destinationId,
            TravelerName = "Demo Traveler",
            StartsOn = new DateOnly(2026, 5, 20),
            EndsOn = new DateOnly(2026, 5, 27)
        });
        await dbContext.SaveChangesAsync();

        var page = new ReservationsModel(dbContext)
        {
            PageContext = new PageContext { HttpContext = new DefaultHttpContext() },
            Input = new ReservationsModel.ReservationInput
            {
                TripId = tripId,
                Type = ReservationType.Flight,
                Date = new DateOnly(2026, 5, 21),
                StartsAt = new TimeOnly(8, 0),
                Title = "Tokyo to Osaka",
                City = "Tokyo",
                FlightNumber = "TC101",
                OriginName = "Tokyo",
                DestinationName = "Osaka"
            }
        };

        await page.OnPostSaveAsync();

        var item = await dbContext.Reservations.SingleAsync();
        Assert.Equal(ScheduleItemKind.ConfirmedReservation, item.PlanningKind);
    }

    private static TravelCompanionDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<TravelCompanionDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new TravelCompanionDbContext(options);
    }
}

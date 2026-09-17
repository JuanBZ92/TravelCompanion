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
        var timed = new ItineraryItemMutationRequest(recommendation.Id, null, recommendation.Title, startsOn,
            "morning", true, new TimeOnly(20, 0), null, "Tokyo", recommendation.Title, "", null,
            null, null, result.Revision, "test-edit", true);
        var updated = await itineraryService.UpdateAsync(httpContext, result.Item!.Id, timed);
        Assert.Equal(ScheduleItemKind.ConfirmedReservation, updated.Item!.PlanningKind);
        Assert.Equal(new TimeOnly(20, 0), updated.Item.StartsAt);
        var persisted = await dbContext.Reservations.SingleAsync();
        Assert.Equal("night", trip.DayPlans.SelectMany(d => d.Blocks).Single(b => b.Id == persisted.TripDayBlockId).PeriodKey);
        Assert.Equal(recommendation.Latitude, persisted.Latitude);
        var untimed = await itineraryService.UpdateAsync(httpContext, result.Item.Id,
            timed with { UseExactTime = false, StartsAt = null, ExpectedRevision = updated.Revision });
        Assert.Equal(ScheduleItemKind.Recommendation, untimed.Item!.PlanningKind);
    }

    [Fact]
    public async Task Google_place_keeps_location_appears_in_insights_and_retry_does_not_duplicate()
    {
        await using var dbContext = CreateDbContext();
        var destination = CreateDestination();
        var user = CreateUser();
        dbContext.AddRange(destination, user, new BuilderAccessGrant
        {
            Id = Guid.NewGuid(),
            AppUserId = user.Id,
            AppUser = user,
            DestinationId = destination.Id,
            Destination = destination,
            PinHash = "test"
        });
        await dbContext.SaveChangesAsync();

        var sessionService = new UserSessionService(dbContext);
        var (_, token) = await sessionService.CreateSessionAsync(user, accessMode: SessionAccessMode.Builder);
        var httpContext = CreateHttpContext(token);
        var startsOn = new DateOnly(2026, 9, 18);
        var setup = await new BuilderTripService(dbContext, sessionService).SaveAsync(
            httpContext,
            new SaveBuilderTripSetupRequest(
                startsOn,
                startsOn.AddDays(1),
                "Asia/Tokyo",
                0,
                [new BuilderTripSetupSegmentDto("Kyoto", startsOn, startsOn.AddDays(1))]));
        var service = new TravelerItineraryService(dbContext, new TravelerAccessService(sessionService));
        var request = new ItineraryItemMutationRequest(
            null,
            "google-place-1",
            "Tonkatsu Suzuki",
            startsOn,
            "afternoon",
            false,
            null,
            null,
            null,
            "Tonkatsu Suzuki",
            "123 Example, Osaka, Japan",
            null,
            35.0116m,
            135.7681m,
            setup.Revision,
            "google-place-create");

        var created = await service.CreateAsync(httpContext, request);
        var retried = await service.CreateAsync(httpContext, request);

        Assert.Equal(created.Item!.Id, retried.Item!.Id);
        Assert.Equal(created.Revision, retried.Revision);
        var item = await dbContext.Reservations.SingleAsync(candidate => candidate.ProviderPlaceId == "google-place-1");
        Assert.Equal(ItineraryItemSource.GooglePlace, item.ItemSource);
        Assert.Equal("Kyoto", item.City);
        Assert.Equal("123 Example, Osaka, Japan", item.Address);
        Assert.Equal(35.0116m, item.Latitude);
        Assert.Equal(135.7681m, item.Longitude);

        var insight = Assert.Single((await new ExternalPlaceInsightsService(dbContext).GetReportAsync()).Places);
        Assert.Equal("Kyoto", insight.City);
        Assert.Equal(1, insight.SavedCount);

        var preserved = await service.UpdateAsync(httpContext, item.Id, request with
        {
            Title = "Tonkatsu Suzuki editado",
            Latitude = null,
            Longitude = null,
            ExpectedRevision = created.Revision
        });
        var preservedItem = await dbContext.Reservations.SingleAsync(candidate => candidate.Id == item.Id);
        Assert.Equal(35.0116m, preservedItem.Latitude);
        Assert.Equal(135.7681m, preservedItem.Longitude);

        var updated = await service.UpdateAsync(httpContext, item.Id, request with
        {
            GooglePlaceId = null,
            Title = "Plan libre",
            LocationName = "Mi lugar favorito",
            Address = "Dirección escrita a mano",
            Latitude = null,
            Longitude = null,
            ExpectedRevision = preserved.Revision
        });

        Assert.Equal(ItineraryItemSource.Manual, updated.Item!.ItemSource);
        Assert.Null(updated.Item.ProviderPlaceId);
        Assert.Empty((await new ExternalPlaceInsightsService(dbContext).GetReportAsync()).Places);
    }

    [Fact]
    public async Task Builder_can_delete_manual_trip_without_losing_access_or_leaving_linked_data()
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
        var service = new BuilderTripService(dbContext, sessionService);
        var startsOn = new DateOnly(2026, 10, 1);
        var setup = await service.SaveAsync(httpContext, new SaveBuilderTripSetupRequest(
            startsOn,
            startsOn.AddDays(2),
            "Asia/Tokyo",
            0,
            [new BuilderTripSetupSegmentDto("Tokyo", startsOn, startsOn.AddDays(2), "Hotel Test")]),
            CancellationToken.None);
        var tripId = setup.TripId!.Value;
        var reservation = new Reservation
        {
            Id = Guid.NewGuid(), TripId = tripId, Date = startsOn, StartsAt = new TimeOnly(9, 0),
            Title = "Plan de prueba", City = "Tokyo", LocationName = "Test", Address = string.Empty,
            ConfirmationCode = string.Empty, Notes = string.Empty, Owner = ItineraryItemOwner.Traveler
        };
        dbContext.Reservations.Add(reservation);
        dbContext.NotificationOutboxItems.Add(new NotificationOutboxItem
        {
            Id = Guid.NewGuid(), UserId = user.Id, ReservationId = reservation.Id,
            DeduplicationKey = $"delete-test-{reservation.Id}", Kind = "schedule-reminder",
            Title = "Recordatorio", Body = "Prueba", ScheduledForUtc = DateTimeOffset.UtcNow.AddHours(1)
        });
        await sessionService.CreateSessionAsync(user, tripId: tripId, accessMode: SessionAccessMode.Builder);
        await dbContext.SaveChangesAsync();

        var conflict = await Assert.ThrowsAsync<BuilderRevisionConflictException>(() => service.DeleteAsync(
            httpContext,
            new DeleteBuilderTripSetupRequest(tripId, setup.Revision - 1),
            CancellationToken.None));
        Assert.Equal(setup.Revision, conflict.CurrentRevision);

        var deleted = await service.DeleteAsync(
            httpContext,
            new DeleteBuilderTripSetupRequest(tripId, setup.Revision),
            CancellationToken.None);

        Assert.False(deleted.IsConfigured);
        Assert.Null(deleted.TripId);
        Assert.Empty(await dbContext.Trips.ToListAsync());
        Assert.Empty(await dbContext.Reservations.ToListAsync());
        Assert.Empty(await dbContext.NotificationOutboxItems.ToListAsync());
        Assert.All(await dbContext.AppUserSessions.Where(item => item.UserId == user.Id).ToListAsync(), item => Assert.Null(item.TripId));
        var preservedGrant = await dbContext.BuilderAccessGrants.SingleAsync();
        Assert.Null(preservedGrant.TripId);
        Assert.Equal(BuilderAccessStatus.Active, preservedGrant.Status);
    }

    [Fact]
    public async Task Builder_delete_rejects_curated_trip_even_when_grant_points_to_it()
    {
        await using var dbContext = CreateDbContext();
        var destination = CreateDestination();
        var user = CreateUser();
        var trip = new Trip
        {
            Id = Guid.NewGuid(), AppUserId = user.Id, DestinationId = destination.Id,
            TravelerName = user.DisplayName, StartsOn = new DateOnly(2026, 10, 1),
            EndsOn = new DateOnly(2026, 10, 3), ExperienceMode = ExperienceMode.CuratedPremium,
            PublicationStatus = TripPublicationStatus.Published
        };
        var grant = new BuilderAccessGrant
        {
            Id = Guid.NewGuid(), AppUserId = user.Id, AppUser = user,
            DestinationId = destination.Id, Destination = destination,
            TripId = trip.Id, Trip = trip, PinHash = "test"
        };
        dbContext.AddRange(destination, user, trip, grant);
        await dbContext.SaveChangesAsync();
        var sessionService = new UserSessionService(dbContext);
        var (_, token) = await sessionService.CreateSessionAsync(
            user,
            tripId: trip.Id,
            accessMode: SessionAccessMode.Builder);
        var service = new BuilderTripService(dbContext, sessionService);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.DeleteAsync(
            CreateHttpContext(token),
            new DeleteBuilderTripSetupRequest(trip.Id, trip.PlanRevision),
            CancellationToken.None));
        Assert.True(await dbContext.Trips.AnyAsync(item => item.Id == trip.Id));
    }

    [Fact]
    public async Task Builder_edit_preserves_plans_and_reports_dates_excluded_by_new_range()
    {
        await using var dbContext = CreateDbContext();
        var destination = CreateDestination();
        var user = CreateUser();
        var grant = new BuilderAccessGrant
        {
            Id = Guid.NewGuid(), AppUserId = user.Id, AppUser = user,
            DestinationId = destination.Id, Destination = destination, PinHash = "test"
        };
        dbContext.AddRange(destination, user, grant);
        await dbContext.SaveChangesAsync();
        var sessionService = new UserSessionService(dbContext);
        var (_, token) = await sessionService.CreateSessionAsync(user, accessMode: SessionAccessMode.Builder);
        var context = CreateHttpContext(token);
        var service = new BuilderTripService(dbContext, sessionService);
        var startsOn = new DateOnly(2026, 10, 1);
        var setup = await service.SaveAsync(context, new SaveBuilderTripSetupRequest(
            startsOn, startsOn.AddDays(2), "Asia/Tokyo", 0,
            [new BuilderTripSetupSegmentDto("Tokyo", startsOn, startsOn.AddDays(2))]));
        var reservation = new Reservation
        {
            Id = Guid.NewGuid(), TripId = setup.TripId!.Value, Date = startsOn.AddDays(2), StartsAt = new TimeOnly(18, 0),
            Title = "Cena", City = "Tokyo", LocationName = "Cena", Address = string.Empty,
            ConfirmationCode = string.Empty, Notes = string.Empty, Owner = ItineraryItemOwner.Traveler
        };
        dbContext.Reservations.Add(reservation);
        await dbContext.SaveChangesAsync();

        var exclusion = await Assert.ThrowsAsync<InvalidOperationException>(() => service.SaveAsync(context,
            new SaveBuilderTripSetupRequest(startsOn, startsOn.AddDays(1), "Asia/Tokyo", setup.Revision,
                [new BuilderTripSetupSegmentDto("Tokyo", startsOn, startsOn.AddDays(1))])));
        Assert.Contains("03/10/2026", exclusion.Message);

        var edited = await service.SaveAsync(context, new SaveBuilderTripSetupRequest(
            startsOn, startsOn.AddDays(2), "Asia/Tokyo", setup.Revision,
            [new BuilderTripSetupSegmentDto("Kyoto", startsOn, startsOn.AddDays(2), "Hotel Kyoto")]),
            CancellationToken.None);
        Assert.Equal(setup.Revision + 1, edited.Revision);
        Assert.True(await dbContext.Reservations.AnyAsync(item => item.Id == reservation.Id));
        Assert.All(edited.Segments, item => Assert.Equal("Kyoto", item.City));
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

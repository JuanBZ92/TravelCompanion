using Microsoft.EntityFrameworkCore;
using TravelCompanion.Api.Data;
using TravelCompanion.Api.Models;
using TravelCompanion.Shared;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Api.Tests;

public sealed partial class TravelChatServiceTests
{
    private static async Task<(AppUser User, Trip Trip, Recommendation Short, Recommendation Long)>
        SeedFreeTimeAsync(TravelCompanionDbContext db)
    {
        var destination = Guid.NewGuid();
        var shortIdea = CreateRecommendation(destination, "Short local visit", "Culture", "Gallery", 20, "free");
        shortIdea.Tags = ["culture"];
        var longIdea = CreateRecommendation(destination, "Long museum visit", "Culture", "Museum", 100, "free");
        longIdea.Tags = ["culture"];
        var user = await SeedPlanningWorldAsync(db, destination, shortIdea, longIdea);
        var trip = await db.Trips.SingleAsync();
        db.Reservations.RemoveRange(trip.Reservations);
        trip.Reservations.Clear();
        trip.StartsOn = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(2);
        trip.EndsOn = trip.StartsOn.AddDays(5);
        trip.TimeZoneId = "Asia/Tokyo";
        await db.SaveChangesAsync();
        return (user, trip, shortIdea, longIdea);
    }

    private static TravelChatRequest FreeTimeRequest(Trip trip, string locale = "en-US") => new(
        "What can I do in this free time?", null, "Tokyo", trip.StartsOn, null, locale,
        new(GuidedTravelActions.Recommend), new(GuidedTravelCategories.Culture, MaxDurationMinutes: 120)
        {
            WindowStartsAtLocal = trip.StartsOn.ToDateTime(new(10, 0)),
            WindowEndsAtLocal = trip.StartsOn.ToDateTime(new(12, 0)),
            WindowTimeZoneId = trip.TimeZoneId
        }, Guid.NewGuid());

    [Fact]
    public async Task Free_time_caps_window_at_fixed_booking_and_filters_duration_with_travel()
    {
        await using var db = CreateDbContext();
        var (user, trip, shortIdea, _) = await SeedFreeTimeAsync(db);
        var booking = CreateReservation("Booked lunch", new(11, 0), "Tokyo", trip.StartsOn);
        booking.TripId = trip.Id;
        booking.EndsAt = new(12, 0);
        db.Reservations.Add(booking);
        await db.SaveChangesAsync();

        var result = await CreateService(db).CreatePlanAsync(user, FreeTimeRequest(trip), CancellationToken.None, trip.Id);

        var card = Assert.Single(result.Cards);
        Assert.Equal(shortIdea.Id.ToString(), card.RecommendationId);
        Assert.Equal("10:10", card.StartTime);
        Assert.Equal("10:30", card.EndTime);
        Assert.Contains("estimates", result.Message);
        Assert.Contains(card.Warnings, warning => warning.Contains("estimates"));
        Assert.Contains(card.WhyItFits, reason => reason.Contains("20 minutes"));
        Assert.Equal("Asia/Tokyo", result.Criteria!.WindowTimeZoneId);
    }

    [Fact]
    public async Task Free_time_reserves_transfer_to_next_fixed_booking_when_coordinates_are_known()
    {
        await using var db = CreateDbContext();
        var (user, trip, shortIdea, _) = await SeedFreeTimeAsync(db);
        var booking = CreateReservation("Distant booked lunch", new(11, 0), "Tokyo", trip.StartsOn);
        booking.TripId = trip.Id;
        booking.EndsAt = new(12, 0);
        booking.Latitude = shortIdea.Latitude + 1;
        booking.Longitude = shortIdea.Longitude;
        db.Reservations.Add(booking);
        await db.SaveChangesAsync();
        var result = await CreateService(db).CreatePlanAsync(user, FreeTimeRequest(trip), CancellationToken.None, trip.Id);
        Assert.Empty(result.Cards);
        Assert.Contains("estimated travel", result.Message);
    }

    [Theory]
    [InlineData("ongoing")]
    [InlineData("zone")]
    [InlineData("partial")]
    [InlineData("utc")]
    [InlineData("too_long")]
    public async Task Free_time_rejects_occupied_or_invalid_windows(string invalid)
    {
        await using var db = CreateDbContext();
        var (user, trip, _, _) = await SeedFreeTimeAsync(db);
        var request = FreeTimeRequest(trip);
        if (invalid == "ongoing")
        {
            var booking = CreateReservation("Ongoing reservation", new(9, 30), "Tokyo", trip.StartsOn);
            booking.TripId = trip.Id;
            booking.EndsAt = new(10, 30);
            db.Reservations.Add(booking);
            await db.SaveChangesAsync();
        }
        else request = request with { Criteria = request.Criteria! with
        {
            WindowTimeZoneId = invalid == "zone" ? "UTC" : trip.TimeZoneId,
            WindowEndsAtLocal = invalid == "partial" ? null
                : invalid == "too_long" ? trip.StartsOn.ToDateTime(new(15, 0)) : request.Criteria.WindowEndsAtLocal,
            WindowStartsAtLocal = invalid == "utc" ? DateTime.SpecifyKind(request.Criteria.WindowStartsAtLocal!.Value, DateTimeKind.Utc)
                : request.Criteria.WindowStartsAtLocal
        }};
        var result = await CreateService(db).CreatePlanAsync(user, request, CancellationToken.None, trip.Id);
        Assert.Empty(result.Cards);
        Assert.Equal("time_window", result.MissingContext?.Field);
    }

    [Fact]
    public async Task Free_time_deduplicates_flexible_plans_without_blocking_the_window_and_preserves_free_permissions()
    {
        await using var db = CreateDbContext();
        var (user, trip, shortIdea, longIdea) = await SeedFreeTimeAsync(db);
        var existing = CreateReservation("Existing flexible idea", new(10, 0), "Tokyo", trip.StartsOn);
        existing.TripId = trip.Id;
        existing.RecommendationId = shortIdea.Id;
        existing.Owner = ItineraryItemOwner.Traveler;
        existing.PlanningKind = ScheduleItemKind.Recommendation;
        existing.TimePrecision = ItineraryTimePrecision.PeriodOnly;
        db.Reservations.Add(existing);
        longIdea.AccessLevel = ContentAccessLevel.Subscription;
        await db.SaveChangesAsync();

        var result = await CreateService(db).CreatePlanAsync(user, FreeTimeRequest(trip), CancellationToken.None, trip.Id);

        Assert.Empty(result.Cards);
        Assert.Null(result.MissingContext);
        Assert.Equal("adjust", result.GuidedQuestion?.Id);
    }

    [Fact]
    public async Task Free_time_is_scoped_to_active_trip_and_never_reads_another_accounts_booking()
    {
        await using var db = CreateDbContext();
        var (user, trip, _, _) = await SeedFreeTimeAsync(db);
        var result = await CreateService(db).CreatePlanAsync(user, FreeTimeRequest(trip), CancellationToken.None, Guid.NewGuid());
        Assert.Empty(result.Cards);
        Assert.Equal("date", result.MissingContext?.Field);
    }

    [Fact]
    public async Task Free_time_without_gps_keeps_optional_walking_filters_from_blocking_results()
    {
        await using var db = CreateDbContext();
        var (user, trip, _, _) = await SeedFreeTimeAsync(db);
        var request = FreeTimeRequest(trip);
        request = request with { Criteria = request.Criteria! with { MaxWalkingMinutes = 15, WalkingMinuteOptions = [15] } };
        var result = await CreateService(db).CreatePlanAsync(user, request, CancellationToken.None, trip.Id);
        Assert.NotEmpty(result.Cards);
        Assert.Null(result.GuidedQuestion);
        Assert.Null(result.Criteria!.MaxWalkingMinutes);
    }

    [Fact]
    public async Task Free_time_known_far_location_is_rejected_even_when_ranker_hides_large_distance()
    {
        await using var db = CreateDbContext();
        var (user, trip, _, _) = await SeedFreeTimeAsync(db);
        var result = await CreateService(db).CreatePlanAsync(user, FreeTimeRequest(trip) with
        {
            CurrentLocation = new(10, 10)
        }, CancellationToken.None, trip.Id);
        Assert.Empty(result.Cards);
    }

    [Fact]
    public async Task Free_time_archived_trip_is_rejected()
    {
        await using var db = CreateDbContext();
        var (user, trip, _, _) = await SeedFreeTimeAsync(db);
        trip.IsArchived = true;
        await db.SaveChangesAsync();
        var result = await CreateService(db).CreatePlanAsync(user, FreeTimeRequest(trip), CancellationToken.None, trip.Id);
        Assert.Equal("date", result.MissingContext?.Field);
        Assert.Empty(result.Cards);
    }

    [Fact]
    public async Task Free_time_distant_booking_after_requested_window_does_not_remove_fitting_ideas()
    {
        await using var db = CreateDbContext();
        var (user, trip, shortIdea, _) = await SeedFreeTimeAsync(db);
        var booking = CreateReservation("Evening booking", new(20, 0), "Tokyo", trip.StartsOn);
        booking.TripId = trip.Id;
        booking.EndsAt = new(21, 0);
        booking.Latitude = shortIdea.Latitude + 1;
        booking.Longitude = shortIdea.Longitude;
        db.Reservations.Add(booking);
        await db.SaveChangesAsync();
        var result = await CreateService(db).CreatePlanAsync(user, FreeTimeRequest(trip), CancellationToken.None, trip.Id);
        Assert.NotEmpty(result.Cards);
    }

    [Fact]
    public async Task Free_time_retry_keeps_same_candidates_and_alternative_does_not_repeat_previous_ideas()
    {
        await using var db = CreateDbContext();
        var (user, trip, _, _) = await SeedFreeTimeAsync(db);
        var service = CreateService(db);
        var request = FreeTimeRequest(trip);
        var first = await service.CreatePlanAsync(user, request, CancellationToken.None, trip.Id);
        var retry = await service.CreatePlanAsync(user, request with { ConversationId = first.ConversationId }, CancellationToken.None, trip.Id);
        Assert.Equal(first.Cards.Select(card => card.RecommendationId), retry.Cards.Select(card => card.RecommendationId));
        var alternative = await service.CreatePlanAsync(user, request with
        {
            ConversationId = retry.ConversationId,
            GuidedAction = new(GuidedTravelActions.Alternative)
        }, CancellationToken.None, trip.Id);
        Assert.DoesNotContain(alternative.Cards, card => first.Cards.Any(previous => previous.RecommendationId == card.RecommendationId));
    }
}

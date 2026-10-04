using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging;
using TravelCompanion.Api.Data;
using TravelCompanion.Api.Models;
using TravelCompanion.Api.Options;
using TravelCompanion.Api.Services;
using TravelCompanion.Shared;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Api.Tests;

internal sealed record DayPlanTestWorld(AppUser User, Trip Trip, BuilderAccessGrant Grant)
{
    internal static readonly DateOnly Start = new(2026, 10, 20);

    internal TravelerAccessContext Access(SessionAccessMode? mode = null, Guid? sessionTrip = null)
    {
        var accessMode = mode ?? (Grant.IsTrial ? SessionAccessMode.FreeMapPreview : SessionAccessMode.Builder);
        return new(new(Guid.NewGuid(), User, sessionTrip ?? Trip.Id, accessMode, DateTimeOffset.UtcNow.AddDays(1)),
            Grant.IsTrial ? ExperienceMode.FreePreview : ExperienceMode.SelfServiceBuilder,
            new(true, true, accessMode is SessionAccessMode.Builder or SessionAccessMode.FreeMapPreview,
                false, false, true));
    }

    internal DayPlanRequest Request(int days = 1, string pace = "balanced") =>
        new(Trip.Id, Trip.PlanRevision, Start, days, Guid.NewGuid(), new(pace, "medium", []), "es-ES");

    internal static async Task<DayPlanTestWorld> SeedAsync(TravelCompanionDbContext db, bool trial = false,
        int perCity = 60, int existing = 0)
    {
        var destination = new Destination
        {
            Id = Guid.NewGuid(), Name = "Japan", Slug = $"planner-{Guid.NewGuid():N}", Country = "Japan",
            HeroImageUrl = "", ShortDescription = "Synthetic planner validation"
        };
        var user = new AppUser { Id = Guid.NewGuid(), Email = $"planner-{Guid.NewGuid():N}@example.test", DisplayName = "Planner test" };
        user.TravelPreferenceProfile = new()
        {
            UserId = user.Id, BudgetLevel = "medium", TravelPace = "balanced", Interests = ["Culture", "Food"],
            MaxWalkingMinutes = 120
        };
        user.Entitlements.Add(new()
        {
            Id = Guid.NewGuid(), UserId = user.Id, DestinationId = destination.Id, AccessLevel = ContentAccessLevel.Free,
            Source = "synthetic", GrantedAt = DateTimeOffset.UtcNow
        });
        var trip = new Trip
        {
            Id = Guid.NewGuid(), AppUserId = user.Id, DestinationId = destination.Id, TravelerName = user.DisplayName,
            StartsOn = Start, EndsOn = Start.AddDays(6), TimeZoneId = "Asia/Tokyo",
            ExperienceMode = ExperienceMode.SelfServiceBuilder, PublicationStatus = TripPublicationStatus.Published,
            BuilderSegmentsJson = JsonSerializer.Serialize(new BuilderTripSetupSegmentDto[]
            {
                new("Tokyo", Start, Start.AddDays(3)), new("Kyoto", Start.AddDays(4), Start.AddDays(6))
            })
        };
        var grant = new BuilderAccessGrant
        {
            Id = Guid.NewGuid(), AppUserId = user.Id, DestinationId = destination.Id, TripId = trip.Id,
            IsTrial = trial, FreePolicy = FreeAccessPolicy.PersistentFree, Status = BuilderAccessStatus.Active,
            CreatedAtUtc = DateTimeOffset.UtcNow, TrialEditingStartedAtUtc = trial ? DateTimeOffset.UtcNow : null
        };
        db.AddRange(destination, user, trip, grant);
        foreach (var city in new[] { "Tokyo", "Kyoto" })
            db.Add(new FreeMapCity
            {
                Id = Guid.NewGuid(), DestinationId = destination.Id, CitySlug = city.ToLowerInvariant(), DisplayName = city,
                CenterLatitude = city == "Tokyo" ? 35.665m : 35.011m,
                CenterLongitude = city == "Tokyo" ? 139.77m : 135.768m, FreeRadiusKm = 2, IsEnabled = true
            });
        foreach (var city in new[] { "Tokyo", "Kyoto" })
        for (var i = 0; i < perCity; i++)
        {
            var food = i % 3 == 0;
            db.Add(new Recommendation
            {
                Id = Guid.NewGuid(), DestinationId = destination.Id, Title = $"{city} {(food ? "food" : "culture")} {i}",
                Category = food ? "Food" : "Culture", Neighborhood = $"{city}, Japan", Description = $"Synthetic {city} place",
                Tags = food ? ["food", "restaurant", "breakfast", "lunch", "dinner"] : ["culture", "museum"],
                PriceLevel = "medium", Latitude = city == "Tokyo" ? 35.665m : 35.011m,
                Longitude = city == "Tokyo" ? 139.77m : 135.768m, SuggestedDurationMinutes = 60,
                OpeningHours = "08:00-23:00", Rating = 4.5, AccessLevel = ContentAccessLevel.Free
            });
        }
        for (var i = 0; i < existing; i++)
            db.Add(new Reservation
            {
                Id = Guid.NewGuid(), TripId = trip.Id, Title = $"Existing manual plan {i}", Date = Start,
                StartsAt = new(9, 0), EndsAt = new(22, 0), City = "Tokyo", LocationName = "Synthetic place",
                Address = "", ConfirmationCode = "", Notes = "", Owner = ItineraryItemOwner.Traveler,
                Flexibility = ItineraryFlexibility.FixedByTraveler, TimePrecision = ItineraryTimePrecision.Exact
            });
        await db.SaveChangesAsync();
        return new(user, trip, grant);
    }

    internal static DayPlanService Service(TravelCompanionDbContext db, bool analytics = false, ILogger<DayPlanService>? logger = null)
    {
        var freeOptions = Microsoft.Extensions.Options.Options.Create(new FreePreviewOptions { AssistantRequestLimit = 3 });
        var free = new FreeTrialAccessService(db, freeOptions, NullLogger<FreeTrialAccessService>.Instance);
        var profile = new UserProfileService(db);
        var planning = new TravelRecommendationPlanningService(db, new DeterministicRecommendationRanker(), free);
        var text = new TravelAssistantTextProvider();
        var chat = new TravelChatService(db, profile,
            new TravelAssistantActionPlanner(new TravelChatIntentClassifier(),
                new TravelPreferenceCommandParser(new RecommendationTagCatalogService(db))),
            text, new TravelAssistantConversationStateService(db, NullLogger<TravelAssistantConversationStateService>.Instance),
            new TravelChatResponseComposer(text), planning, new NoModel(), Microsoft.Extensions.Options.Options.Create(new OpenAiTravelOptions()),
            new TravelAssistantTelemetry(NullLogger<TravelAssistantTelemetry>.Instance), NullLogger<TravelChatService>.Instance);
        return new(db, free, new AssistantUsageService(db, freeOptions,
            Microsoft.Extensions.Options.Options.Create(new StorePurchaseOptions { DailyAssistantLimit = 100 })), profile, chat, planning,
            logger: logger, analytics: analytics ? new ProductAnalyticsService(db) : null);
    }

    private sealed class NoModel : ITravelAiModelClient
    {
        public Task<TravelAiModelResult?> CreateStructuredResponseAsync(TravelAiModelRequest request, CancellationToken ct) =>
            throw new InvalidOperationException("Day planning must use the catalog without a remote model.");
    }
}

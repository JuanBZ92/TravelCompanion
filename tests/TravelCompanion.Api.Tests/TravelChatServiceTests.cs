using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging.Abstractions;
using TravelCompanion.Api.Data;
using TravelCompanion.Api.Models;
using TravelCompanion.Api.Options;
using TravelCompanion.Api.Services;
using TravelCompanion.Shared;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Api.Tests;

public sealed class TravelChatServiceTests
{
    [Fact]
    public async Task CreatePlanAsync_returns_structured_cards_from_user_reservations()
    {
        await using var dbContext = CreateDbContext();
        var destinationId = Guid.NewGuid();
        var user = CreateUser(destinationId);
        dbContext.AppUsers.Add(user);
        dbContext.Destinations.Add(new Destination
        {
            Id = destinationId,
            Name = "Japon",
            Slug = "japon",
            Country = "Japan",
            HeroImageUrl = string.Empty,
            ShortDescription = "Demo"
        });
        dbContext.Trips.Add(new Trip
        {
            Id = Guid.NewGuid(),
            AppUserId = user.Id,
            DestinationId = destinationId,
            TravelerName = "Demo Traveler",
            StartsOn = new DateOnly(2026, 10, 6),
            EndsOn = new DateOnly(2026, 10, 10),
            Reservations =
            [
                CreateReservation("Museum", new TimeOnly(9, 0), "Tokyo"),
                CreateReservation("Dinner", new TimeOnly(18, 0), "Tokyo")
            ]
        });
        dbContext.Recommendations.Add(new Recommendation
        {
            Id = Guid.NewGuid(),
            DestinationId = destinationId,
            Title = "Tsukiji Snack Walk",
            Category = "Food",
            Neighborhood = "Chuo, Tokyo",
            Description = "Local snacks in Tokyo before dinner.",
            Latitude = 35.665486m,
            Longitude = 139.770667m,
            SuggestedDurationMinutes = 90,
            AccessLevel = ContentAccessLevel.Free
        });
        await dbContext.SaveChangesAsync();

        var service = CreateService(dbContext);
        var response = await service.CreatePlanAsync(
            user,
            new TravelChatRequest(
                "Proponeme un plan entre mis reservas de hoy",
                null,
                "Tokyo",
                new DateOnly(2026, 10, 6),
                new GeoPointDto(35.665000m, 139.770000m),
                "es-ES"),
            CancellationToken.None);

        Assert.Equal("plan_between_reservations", response.Intent);
        Assert.Null(response.MissingContext);
        Assert.NotEmpty(response.ConversationId);
        Assert.Single(response.Cards);
        Assert.Equal("Tsukiji Snack Walk", response.Cards[0].Title);
        Assert.NotEmpty(response.Cards[0].WhyItFits);
        Assert.NotEmpty(response.SuggestedReplies);
    }

    [Theory]
    [InlineData("dime un plan")]
    [InlineData("quiero un plan")]
    [InlineData("propron un plan")]
    [InlineData("fabricame algo para manana")]
    [InlineData("armame algo para el 8 de octubre")]
    public async Task CreatePlanAsync_accepts_natural_plan_prompt_variants(string message)
    {
        await using var dbContext = CreateDbContext();
        var destinationId = Guid.NewGuid();
        var user = await SeedPlanningWorldAsync(
            dbContext,
            destinationId,
            CreateRecommendation(
                destinationId,
                "Tsukiji Snack Walk",
                "Food",
                "Local snacks in Tokyo before dinner.",
                90));

        var service = CreateService(dbContext);
        var response = await service.CreatePlanAsync(
            user,
            new TravelChatRequest(message, null, "Tokyo", new DateOnly(2026, 10, 6), null, "es-ES"),
            CancellationToken.None);

        Assert.Equal("plan_between_reservations", response.Intent);
        Assert.Null(response.MissingContext);
        Assert.NotEmpty(response.Cards);
    }

    [Fact]
    public async Task CreatePlanAsync_can_plan_open_day_when_no_reservations_exist()
    {
        await using var dbContext = CreateDbContext();
        var destinationId = Guid.NewGuid();
        var user = CreateUser(destinationId);
        dbContext.AppUsers.Add(user);
        dbContext.Destinations.Add(new Destination
        {
            Id = destinationId,
            Name = "Japon",
            Slug = "japon",
            Country = "Japan",
            HeroImageUrl = string.Empty,
            ShortDescription = "Demo"
        });
        dbContext.Trips.Add(new Trip
        {
            Id = Guid.NewGuid(),
            AppUserId = user.Id,
            DestinationId = destinationId,
            TravelerName = "Demo Traveler",
            StartsOn = new DateOnly(2026, 10, 6),
            EndsOn = new DateOnly(2026, 10, 10)
        });
        dbContext.Recommendations.Add(CreateRecommendation(
            destinationId,
            "Open day coffee walk",
            "Food",
            "Local coffee and snacks in Tokyo.",
            60));
        await dbContext.SaveChangesAsync();

        var service = CreateService(dbContext);
        var response = await service.CreatePlanAsync(
            user,
            new TravelChatRequest("Plan", null, "Tokyo", new DateOnly(2026, 10, 6), null, "es-ES"),
            CancellationToken.None);

        Assert.Null(response.MissingContext);
        Assert.Single(response.Cards);
        Assert.Equal("10:00", response.Cards[0].StartTime);
    }

    [Fact]
    public async Task CreatePlanAsync_returns_missing_context_when_minimum_preferences_are_missing()
    {
        await using var dbContext = CreateDbContext();
        var destinationId = Guid.NewGuid();
        var user = CreateUser(destinationId, includeProfile: false);
        dbContext.AppUsers.Add(user);
        await dbContext.SaveChangesAsync();

        var service = CreateService(dbContext);
        var response = await service.CreatePlanAsync(
            user,
            new TravelChatRequest("Plan", null, "Tokyo", new DateOnly(2026, 10, 6), null, "es-ES"),
            CancellationToken.None);

        Assert.NotNull(response.MissingContext);
        Assert.Equal("preferences", response.MissingContext.Field);
        Assert.Empty(response.Cards);
    }

    [Fact]
    public async Task CreatePlanAsync_guides_unknown_text_instead_of_free_chatting()
    {
        await using var dbContext = CreateDbContext();
        var destinationId = Guid.NewGuid();
        var user = CreateUser(destinationId);
        dbContext.AppUsers.Add(user);
        await dbContext.SaveChangesAsync();

        var service = CreateService(dbContext);
        var response = await service.CreatePlanAsync(
            user,
            new TravelChatRequest("foobar comando raro", null, "Tokyo", new DateOnly(2026, 10, 6), null, "es-ES"),
            CancellationToken.None);

        Assert.NotNull(response.MissingContext);
        Assert.Equal("assistantCommand", response.MissingContext.Field);
        Assert.Contains("Que puedo pedirte", response.SuggestedReplies);
        Assert.Contains("Plan para comer", response.SuggestedReplies);
        Assert.Empty(response.Cards);
    }

    [Fact]
    public async Task CreatePlanAsync_returns_guided_help_without_requiring_preferences()
    {
        await using var dbContext = CreateDbContext();
        var destinationId = Guid.NewGuid();
        var user = CreateUser(destinationId, includeProfile: false);
        dbContext.AppUsers.Add(user);
        await dbContext.SaveChangesAsync();

        var service = CreateService(dbContext);
        var response = await service.CreatePlanAsync(
            user,
            new TravelChatRequest("Que puedo pedirte", null, "Tokyo", new DateOnly(2026, 10, 6), null, "es-ES"),
            CancellationToken.None);

        Assert.Equal("help", response.Intent);
        Assert.Null(response.MissingContext);
        Assert.Empty(response.Cards);
        Assert.Contains("Ver mis preferencias", response.SuggestedReplies);
        Assert.Contains("Planificar", response.Message);
        Assert.Contains("Ajustar", response.Message);
        Assert.Contains("Agenda", response.Message);
        Assert.Contains("Preferencias", response.Message);
        Assert.Contains("Ayuda", response.Message);
        Assert.Contains("Evitar #culture", response.Message);
    }

    [Fact]
    public async Task CreatePlanAsync_returns_english_help_when_locale_is_en()
    {
        await using var dbContext = CreateDbContext();
        var destinationId = Guid.NewGuid();
        var user = CreateUser(destinationId, includeProfile: false);
        dbContext.AppUsers.Add(user);
        await dbContext.SaveChangesAsync();

        var service = CreateService(dbContext);
        var response = await service.CreatePlanAsync(
            user,
            new TravelChatRequest("What can I ask", null, "Tokyo", new DateOnly(2026, 10, 6), null, "en-US"),
            CancellationToken.None);

        Assert.Equal("help", response.Intent);
        Assert.Contains("I can help", response.Message);
        Assert.Contains("Show my schedule", response.SuggestedReplies);
    }

    [Fact]
    public async Task CreatePlanAsync_passes_configured_prompt_version_to_model_client()
    {
        await using var dbContext = CreateDbContext();
        var destinationId = Guid.NewGuid();
        var user = await SeedPlanningWorldAsync(
            dbContext,
            destinationId,
            CreateRecommendation(
                destinationId,
                "Tsukiji Snack Walk",
                "Food",
                "Local snacks in Tokyo before dinner.",
                90));
        var modelClient = new CapturingTravelAiModelClient();

        var service = CreateService(
            dbContext,
            modelClient,
            new OpenAiTravelOptions { PromptVersion = "travel-chat.v-test" });
        await service.CreatePlanAsync(
            user,
            new TravelChatRequest("Plan", null, "Tokyo", new DateOnly(2026, 10, 6), null, "en-US"),
            CancellationToken.None);

        Assert.NotNull(modelClient.LastRequest);
        Assert.Equal("travel-chat.v-test", modelClient.LastRequest!.PromptVersion);
        Assert.Equal("en-US", modelClient.LastRequest.Locale);
    }

    [Fact]
    public async Task UserProfileService_persists_real_preference_profile_patch()
    {
        await using var dbContext = CreateDbContext();
        var destinationId = Guid.NewGuid();
        var user = CreateUser(destinationId, includeProfile: false);
        dbContext.AppUsers.Add(user);
        await dbContext.SaveChangesAsync();

        var service = new UserProfileService(dbContext);
        var profile = await service.PatchProfileAsync(
            user.Id,
            new TravelPreferenceProfilePatchDto(
                ["ramen", "coffee"],
                ["vegetarian"],
                "low",
                "relaxed",
                ["Food", "Culture"],
                ["shopping"],
                true,
                15),
            CancellationToken.None);

        Assert.True(profile.HasMinimumPreferences);
        Assert.Equal("low", profile.BudgetLevel);
        Assert.Contains("Food", profile.Interests);

        var savedProfile = await dbContext.TravelPreferenceProfiles.FindAsync(user.Id);
        Assert.NotNull(savedProfile);
        Assert.Contains("vegetarian", savedProfile.DietaryRestrictions);
        Assert.Equal(15, savedProfile.MaxWalkingMinutes);
    }

    [Fact]
    public async Task ItineraryService_saves_recommendation_as_user_itinerary_item()
    {
        await using var dbContext = CreateDbContext();
        var destinationId = Guid.NewGuid();
        var user = CreateUser(destinationId);
        var recommendationId = Guid.NewGuid();
        dbContext.AppUsers.Add(user);
        dbContext.Destinations.Add(new Destination
        {
            Id = destinationId,
            Name = "Japon",
            Slug = "japon",
            Country = "Japan",
            HeroImageUrl = string.Empty,
            ShortDescription = "Demo"
        });
        dbContext.Trips.Add(new Trip
        {
            Id = Guid.NewGuid(),
            AppUserId = user.Id,
            DestinationId = destinationId,
            TravelerName = "Demo Traveler",
            StartsOn = new DateOnly(2026, 10, 6),
            EndsOn = new DateOnly(2026, 10, 10)
        });
        dbContext.Recommendations.Add(new Recommendation
        {
            Id = recommendationId,
            DestinationId = destinationId,
            Title = "Tsukiji Snack Walk",
            Category = "Food",
            Neighborhood = "Chuo, Tokyo",
            Description = "Local snacks in Tokyo before dinner.",
            Latitude = 35.665486m,
            Longitude = 139.770667m,
            SuggestedDurationMinutes = 90,
            AccessLevel = ContentAccessLevel.Free
        });
        await dbContext.SaveChangesAsync();

        var service = new ItineraryService(dbContext);
        var response = await service.SaveItineraryItemAsync(
            user,
            new SaveItineraryItemRequest(
                recommendationId,
                new DateOnly(2026, 10, 6),
                new TimeOnly(11, 0),
                new TimeOnly(12, 30)),
            CancellationToken.None);

        Assert.True(response.Saved);
        Assert.NotNull(response.Item);
        Assert.Equal(ScheduleItemKind.Recommendation, response.Item.PlanningKind);
        Assert.Equal("Plan guardado en tu itinerario.", response.Message);
        Assert.True(await dbContext.Reservations.AnyAsync(reservation =>
            reservation.Trip!.AppUserId == user.Id
            && reservation.Title == "Tsukiji Snack Walk"
            && reservation.PlanningKind == ScheduleItemKind.Recommendation));
    }

    [Fact]
    public async Task CreatePlanAsync_parses_date_from_message_and_uses_that_days_schedule()
    {
        await using var dbContext = CreateDbContext();
        var destinationId = Guid.NewGuid();
        var user = CreateUser(destinationId);
        dbContext.AppUsers.Add(user);
        dbContext.Destinations.Add(new Destination
        {
            Id = destinationId,
            Name = "Japon",
            Slug = "japon",
            Country = "Japan",
            HeroImageUrl = string.Empty,
            ShortDescription = "Demo"
        });
        dbContext.Trips.Add(new Trip
        {
            Id = Guid.NewGuid(),
            AppUserId = user.Id,
            DestinationId = destinationId,
            TravelerName = "Demo Traveler",
            StartsOn = new DateOnly(2026, 10, 6),
            EndsOn = new DateOnly(2026, 10, 10),
            Reservations =
            [
                CreateReservation("Morning tour", new TimeOnly(9, 0), "Tokyo", new DateOnly(2026, 10, 8)),
                CreateReservation("Dinner", new TimeOnly(18, 0), "Tokyo", new DateOnly(2026, 10, 8))
            ]
        });
        dbContext.Recommendations.Add(CreateRecommendation(
            destinationId,
            "October snack stop",
            "Food",
            "Local snacks in Tokyo.",
            60));
        await dbContext.SaveChangesAsync();

        var service = CreateService(dbContext);
        var response = await service.CreatePlanAsync(
            user,
            new TravelChatRequest(
                "Proponeme planes para el 8 de octubre",
                null,
                "Tokyo",
                new DateOnly(2026, 10, 6),
                null,
                "es-ES"),
            CancellationToken.None);

        Assert.Null(response.MissingContext);
        Assert.Single(response.Cards);
        Assert.Equal("2026-10-08", (await dbContext.TravelChatConversations.FindAsync(response.ConversationId))!.LastDate!.Value.ToString("yyyy-MM-dd"));
    }

    [Fact]
    public async Task CreatePlanAsync_returns_schedule_summary_for_schedule_intent()
    {
        await using var dbContext = CreateDbContext();
        var destinationId = Guid.NewGuid();
        var user = CreateUser(destinationId);
        dbContext.AppUsers.Add(user);
        dbContext.Destinations.Add(new Destination
        {
            Id = destinationId,
            Name = "Japon",
            Slug = "japon",
            Country = "Japan",
            HeroImageUrl = string.Empty,
            ShortDescription = "Demo"
        });
        dbContext.Trips.Add(new Trip
        {
            Id = Guid.NewGuid(),
            AppUserId = user.Id,
            DestinationId = destinationId,
            TravelerName = "Demo Traveler",
            StartsOn = new DateOnly(2026, 10, 6),
            EndsOn = new DateOnly(2026, 10, 10),
            Reservations = [CreateReservation("Museum", new TimeOnly(9, 0), "Tokyo")]
        });
        await dbContext.SaveChangesAsync();

        var service = CreateService(dbContext);
        var response = await service.CreatePlanAsync(
            user,
            new TravelChatRequest("Ver mi agenda", null, "Tokyo", new DateOnly(2026, 10, 6), null, "es-ES"),
            CancellationToken.None);

        Assert.Equal("view_schedule", response.Intent);
        Assert.Contains("Museum", response.Message);
        Assert.Empty(response.Cards);
    }

    [Fact]
    public async Task CreatePlanAsync_returns_and_updates_preferences_from_chat()
    {
        await using var dbContext = CreateDbContext();
        var destinationId = Guid.NewGuid();
        var user = CreateUser(destinationId);
        dbContext.AppUsers.Add(user);
        await dbContext.SaveChangesAsync();

        var service = CreateService(dbContext);
        var viewResponse = await service.CreatePlanAsync(
            user,
            new TravelChatRequest("Ver mis preferencias", null, null, null, null, "es-ES"),
            CancellationToken.None);

        Assert.Equal("view_preferences", viewResponse.Intent);
        Assert.Contains("Intereses", viewResponse.Message);

        var updateResponse = await service.CreatePlanAsync(
            user,
            new TravelChatRequest("Prefiero presupuesto bajo y ritmo tranquilo", null, null, null, null, "es-ES"),
            CancellationToken.None);

        Assert.Equal("update_preferences", updateResponse.Intent);
        Assert.NotNull(updateResponse.MissingContext);
        Assert.Contains("Queres guardarlo", updateResponse.Message);

        var confirmResponse = await service.CreatePlanAsync(
            user,
            new TravelChatRequest("Si, guardar preferencia", updateResponse.ConversationId, null, null, null, "es-ES"),
            CancellationToken.None);

        var profile = await dbContext.TravelPreferenceProfiles.FindAsync(user.Id);
        Assert.Equal("update_preferences", confirmResponse.Intent);
        Assert.Equal("low", profile!.BudgetLevel);
        Assert.Equal("relaxed", profile.TravelPace);
    }

    [Fact]
    public async Task CreatePlanAsync_updates_dislikes_from_visible_tag_words()
    {
        await using var dbContext = CreateDbContext();
        var destinationId = Guid.NewGuid();
        var user = CreateUser(destinationId);
        dbContext.AppUsers.Add(user);
        await dbContext.SaveChangesAsync();

        var service = CreateService(dbContext);
        var response = await service.CreatePlanAsync(
            user,
            new TravelChatRequest("editar preferencia evitar culture", null, null, null, null, "es-ES"),
            CancellationToken.None);

        Assert.Equal("update_preferences", response.Intent);
        Assert.NotNull(response.MissingContext);

        var confirmResponse = await service.CreatePlanAsync(
            user,
            new TravelChatRequest("Si, guardar preferencia", response.ConversationId, null, null, null, "es-ES"),
            CancellationToken.None);

        var profile = await dbContext.TravelPreferenceProfiles.FindAsync(user.Id);
        Assert.Equal("update_preferences", confirmResponse.Intent);
        Assert.Contains("culture", profile!.Dislikes);
        Assert.DoesNotContain("Culture", profile.Interests);
    }

    [Fact]
    public async Task CreatePlanAsync_updates_dislikes_from_database_tags()
    {
        await using var dbContext = CreateDbContext();
        var destinationId = Guid.NewGuid();
        var user = CreateUser(destinationId);
        dbContext.AppUsers.Add(user);
        var onsenRecommendation = CreateRecommendation(
            destinationId,
            "Kurama onsen stop",
            "Nature",
            "Relaxing mountain route.",
            90);
        onsenRecommendation.Tags = ["onsen"];
        dbContext.Recommendations.Add(onsenRecommendation);
        await dbContext.SaveChangesAsync();

        var service = CreateService(dbContext);
        var response = await service.CreatePlanAsync(
            user,
            new TravelChatRequest("editar preferencia evitar onsen", null, null, null, null, "es-ES"),
            CancellationToken.None);

        Assert.NotNull(response.MissingContext);
        Assert.Contains("onsen", response.Message);

        var confirmResponse = await service.CreatePlanAsync(
            user,
            new TravelChatRequest("Si, guardar preferencia", response.ConversationId, null, null, null, "es-ES"),
            CancellationToken.None);

        var profile = await dbContext.TravelPreferenceProfiles.FindAsync(user.Id);
        Assert.Equal("update_preferences", confirmResponse.Intent);
        Assert.Contains("onsen", profile!.Dislikes);
    }

    [Fact]
    public async Task CreatePlanAsync_does_not_update_preferences_when_confirmation_is_rejected()
    {
        await using var dbContext = CreateDbContext();
        var destinationId = Guid.NewGuid();
        var user = CreateUser(destinationId);
        dbContext.AppUsers.Add(user);
        await dbContext.SaveChangesAsync();

        var service = CreateService(dbContext);
        var response = await service.CreatePlanAsync(
            user,
            new TravelChatRequest("editar preferencia evitar culture", null, null, null, null, "es-ES"),
            CancellationToken.None);

        var rejectResponse = await service.CreatePlanAsync(
            user,
            new TravelChatRequest("No, solo este pedido", response.ConversationId, null, null, null, "es-ES"),
            CancellationToken.None);

        var profile = await dbContext.TravelPreferenceProfiles.FindAsync(user.Id);
        Assert.Equal("view_preferences", rejectResponse.Intent);
        Assert.DoesNotContain("culture", profile!.Dislikes);
        Assert.Contains("Culture", profile.Interests);
    }

    [Fact]
    public async Task CreatePlanAsync_uses_rejected_preference_patch_as_one_off_filter()
    {
        await using var dbContext = CreateDbContext();
        var destinationId = Guid.NewGuid();
        var user = CreateUser(destinationId);
        dbContext.AppUsers.Add(user);
        dbContext.Destinations.Add(new Destination
        {
            Id = destinationId,
            Name = "Japon",
            Slug = "japon",
            Country = "Japan",
            HeroImageUrl = string.Empty,
            ShortDescription = "Demo"
        });
        dbContext.Trips.Add(new Trip
        {
            Id = Guid.NewGuid(),
            AppUserId = user.Id,
            DestinationId = destinationId,
            TravelerName = "Demo Traveler",
            StartsOn = new DateOnly(2026, 10, 6),
            EndsOn = new DateOnly(2026, 10, 10)
        });
        var cultureRecommendation = CreateRecommendation(
            destinationId,
            "TeamLab Planets",
            "Culture",
            "Immersive culture in Tokyo.",
            60);
        cultureRecommendation.Tags = ["culture"];
        var foodRecommendation = CreateRecommendation(
            destinationId,
            "Ginza depachika route",
            "Food",
            "Local food in Tokyo.",
            60);
        foodRecommendation.Tags = ["food"];
        dbContext.Recommendations.AddRange(cultureRecommendation, foodRecommendation);
        await dbContext.SaveChangesAsync();

        var service = CreateService(dbContext);
        var confirmation = await service.CreatePlanAsync(
            user,
            new TravelChatRequest("proponeme un plan para 2026-10-06 evitando culture", null, "Tokyo", null, null, "es-ES"),
            CancellationToken.None);

        Assert.NotNull(confirmation.MissingContext);
        Assert.Contains("culture", confirmation.Message);

        var response = await service.CreatePlanAsync(
            user,
            new TravelChatRequest("No, solo este pedido", confirmation.ConversationId, "Tokyo", null, null, "es-ES"),
            CancellationToken.None);

        Assert.Null(response.MissingContext);
        Assert.NotEmpty(response.Cards);
        Assert.DoesNotContain(response.Cards, card => card.Tags.Contains("culture", StringComparer.OrdinalIgnoreCase));
        Assert.Contains(response.Cards, card => card.Tags.Contains("food", StringComparer.OrdinalIgnoreCase));

        var profile = await dbContext.TravelPreferenceProfiles.FindAsync(user.Id);
        Assert.DoesNotContain("culture", profile!.Dislikes);
    }

    [Fact]
    public async Task CreatePlanAsync_uses_model_message_when_available()
    {
        await using var dbContext = CreateDbContext();
        var destinationId = Guid.NewGuid();
        var user = CreateUser(destinationId);
        dbContext.AppUsers.Add(user);
        dbContext.Destinations.Add(new Destination
        {
            Id = destinationId,
            Name = "Japon",
            Slug = "japon",
            Country = "Japan",
            HeroImageUrl = string.Empty,
            ShortDescription = "Demo"
        });
        dbContext.Trips.Add(new Trip
        {
            Id = Guid.NewGuid(),
            AppUserId = user.Id,
            DestinationId = destinationId,
            TravelerName = "Demo Traveler",
            StartsOn = new DateOnly(2026, 10, 6),
            EndsOn = new DateOnly(2026, 10, 10),
            Reservations =
            [
                CreateReservation("Museum", new TimeOnly(9, 0), "Tokyo"),
                CreateReservation("Dinner", new TimeOnly(18, 0), "Tokyo")
            ]
        });
        dbContext.Recommendations.Add(new Recommendation
        {
            Id = Guid.NewGuid(),
            DestinationId = destinationId,
            Title = "Tsukiji Snack Walk",
            Category = "Food",
            Neighborhood = "Chuo, Tokyo",
            Description = "Local snacks in Tokyo before dinner.",
            Latitude = 35.665486m,
            Longitude = 139.770667m,
            SuggestedDurationMinutes = 90,
            AccessLevel = ContentAccessLevel.Free
        });
        await dbContext.SaveChangesAsync();

        var service = CreateService(
            dbContext,
            new FakeTravelAiModelClient(new TravelAiModelResult(
                "Modelo: tenes una ventana tranquila para Tsukiji.",
                ["Menos caminata", "Guardar plan"])));

        var response = await service.CreatePlanAsync(
            user,
            new TravelChatRequest("Plan", null, "Tokyo", new DateOnly(2026, 10, 6), null, "es-ES"),
            CancellationToken.None);

        Assert.Equal("Modelo: tenes una ventana tranquila para Tsukiji.", response.Message);
        Assert.Equal(["Menos caminata", "Guardar plan"], response.SuggestedReplies);
        Assert.Single(response.Cards);
    }

    [Fact]
    public async Task CreatePlanAsync_falls_back_when_model_fails()
    {
        await using var dbContext = CreateDbContext();
        var destinationId = Guid.NewGuid();
        var user = CreateUser(destinationId);
        dbContext.AppUsers.Add(user);
        dbContext.Destinations.Add(new Destination
        {
            Id = destinationId,
            Name = "Japon",
            Slug = "japon",
            Country = "Japan",
            HeroImageUrl = string.Empty,
            ShortDescription = "Demo"
        });
        dbContext.Trips.Add(new Trip
        {
            Id = Guid.NewGuid(),
            AppUserId = user.Id,
            DestinationId = destinationId,
            TravelerName = "Demo Traveler",
            StartsOn = new DateOnly(2026, 10, 6),
            EndsOn = new DateOnly(2026, 10, 10),
            Reservations =
            [
                CreateReservation("Museum", new TimeOnly(9, 0), "Tokyo"),
                CreateReservation("Dinner", new TimeOnly(18, 0), "Tokyo")
            ]
        });
        dbContext.Recommendations.Add(new Recommendation
        {
            Id = Guid.NewGuid(),
            DestinationId = destinationId,
            Title = "Tsukiji Snack Walk",
            Category = "Food",
            Neighborhood = "Chuo, Tokyo",
            Description = "Local snacks in Tokyo before dinner.",
            Latitude = 35.665486m,
            Longitude = 139.770667m,
            SuggestedDurationMinutes = 90,
            AccessLevel = ContentAccessLevel.Free
        });
        await dbContext.SaveChangesAsync();

        var service = CreateService(dbContext, new ThrowingTravelAiModelClient());
        var response = await service.CreatePlanAsync(
            user,
            new TravelChatRequest("Plan", null, "Tokyo", new DateOnly(2026, 10, 6), null, "es-ES"),
            CancellationToken.None);

        Assert.Contains("Te propongo este plan", response.Message);
        Assert.Single(response.Cards);
        Assert.NotEmpty(response.SuggestedReplies);
    }

    [Fact]
    public async Task CreatePlanAsync_changes_fallback_for_less_walking_request()
    {
        await using var dbContext = CreateDbContext();
        var destinationId = Guid.NewGuid();
        var user = CreateUser(destinationId);
        dbContext.AppUsers.Add(user);
        dbContext.Destinations.Add(new Destination
        {
            Id = destinationId,
            Name = "Japon",
            Slug = "japon",
            Country = "Japan",
            HeroImageUrl = string.Empty,
            ShortDescription = "Demo"
        });
        dbContext.Trips.Add(new Trip
        {
            Id = Guid.NewGuid(),
            AppUserId = user.Id,
            DestinationId = destinationId,
            TravelerName = "Demo Traveler",
            StartsOn = new DateOnly(2026, 10, 6),
            EndsOn = new DateOnly(2026, 10, 10),
            Reservations =
            [
                CreateReservation("Museum", new TimeOnly(9, 0), "Tokyo"),
                CreateReservation("Dinner", new TimeOnly(18, 0), "Tokyo")
            ]
        });
        dbContext.Recommendations.AddRange(
            new Recommendation
            {
                Id = Guid.NewGuid(),
                DestinationId = destinationId,
                Title = "Nearby tea stop",
                Category = "Food",
                Neighborhood = "Chuo, Tokyo",
                Description = "Local tea and snacks in Tokyo.",
                Latitude = 35.665100m,
                Longitude = 139.770100m,
                SuggestedDurationMinutes = 45,
                AccessLevel = ContentAccessLevel.Free
            },
            new Recommendation
            {
                Id = Guid.NewGuid(),
                DestinationId = destinationId,
                Title = "Far culture walk",
                Category = "Culture",
                Neighborhood = "Chuo, Tokyo",
                Description = "A longer culture walk in Tokyo.",
                Latitude = 35.720000m,
                Longitude = 139.810000m,
                SuggestedDurationMinutes = 90,
                AccessLevel = ContentAccessLevel.Free
            });
        await dbContext.SaveChangesAsync();

        var service = CreateService(dbContext);
        var response = await service.CreatePlanAsync(
            user,
            new TravelChatRequest(
                "Algo con menos caminata",
                null,
                "Tokyo",
                new DateOnly(2026, 10, 6),
                new GeoPointDto(35.665000m, 139.770000m),
                "es-ES"),
            CancellationToken.None);

        Assert.StartsWith("Busque una opcion con menos caminata", response.Message);
        Assert.Equal("Nearby tea stop", response.Cards[0].Title);
        Assert.Contains("Recomendar por duracion", response.SuggestedReplies);
    }

    [Fact]
    public async Task CreatePlanAsync_uses_directed_response_for_specific_follow_up_even_when_model_returns_generic_text()
    {
        await using var dbContext = CreateDbContext();
        var destinationId = Guid.NewGuid();
        var user = CreateUser(destinationId);
        dbContext.AppUsers.Add(user);
        dbContext.Destinations.Add(new Destination
        {
            Id = destinationId,
            Name = "Japon",
            Slug = "japon",
            Country = "Japan",
            HeroImageUrl = string.Empty,
            ShortDescription = "Demo"
        });
        dbContext.Trips.Add(new Trip
        {
            Id = Guid.NewGuid(),
            AppUserId = user.Id,
            DestinationId = destinationId,
            TravelerName = "Demo Traveler",
            StartsOn = new DateOnly(2026, 10, 6),
            EndsOn = new DateOnly(2026, 10, 10),
            Reservations =
            [
                CreateReservation("Museum", new TimeOnly(9, 0), "Tokyo"),
                CreateReservation("Dinner", new TimeOnly(18, 0), "Tokyo")
            ]
        });
        dbContext.Recommendations.Add(new Recommendation
        {
            Id = Guid.NewGuid(),
            DestinationId = destinationId,
            Title = "Tsukiji Snack Walk",
            Category = "Food",
            Neighborhood = "Chuo, Tokyo",
            Description = "Local snacks in Tokyo before dinner.",
            Latitude = 35.665486m,
            Longitude = 139.770667m,
            SuggestedDurationMinutes = 90,
            AccessLevel = ContentAccessLevel.Free
        });
        await dbContext.SaveChangesAsync();

        var service = CreateService(
            dbContext,
            new FakeTravelAiModelClient(new TravelAiModelResult(
                "Modelo generico que no atiende el pedido.",
                ["Respuesta fija"])));
        var response = await service.CreatePlanAsync(
            user,
            new TravelChatRequest("Quiero algo de comida local", null, "Tokyo", new DateOnly(2026, 10, 6), null, "es-ES"),
            CancellationToken.None);

        Assert.StartsWith("Busque algo de comida local", response.Message);
        Assert.Contains("Plan para relajar", response.SuggestedReplies);
    }

    [Fact]
    public async Task CreatePlanAsync_understands_low_cost_request_and_orders_by_price_level()
    {
        await using var dbContext = CreateDbContext();
        var destinationId = Guid.NewGuid();
        var user = await SeedPlanningWorldAsync(
            dbContext,
            destinationId,
            CreateRecommendation(destinationId, "Premium dinner", "Food", "Special dinner.", 60, "high"),
            CreateRecommendation(destinationId, "Budget ramen", "Food", "Simple local ramen.", 60, "low"),
            CreateRecommendation(destinationId, "Mid market", "Food", "Curated market route.", 60, "medium"));

        var service = CreateService(dbContext);
        var response = await service.CreatePlanAsync(
            user,
            new TravelChatRequest("proponme un plan de coste bajo", null, "Tokyo", new DateOnly(2026, 10, 6), null, "es-ES"),
            CancellationToken.None);

        Assert.StartsWith("Busque una opcion de bajo costo", response.Message);
        Assert.Equal("Budget ramen", response.Cards[0].Title);
        Assert.Equal("low", response.Cards[0].EstimatedCost);
        Assert.Contains("Algo premium", response.SuggestedReplies);
    }

    [Fact]
    public async Task CreatePlanAsync_understands_high_cost_request_and_orders_by_price_level()
    {
        await using var dbContext = CreateDbContext();
        var destinationId = Guid.NewGuid();
        var user = await SeedPlanningWorldAsync(
            dbContext,
            destinationId,
            CreateRecommendation(destinationId, "Budget ramen", "Food", "Simple local ramen.", 60, "low"),
            CreateRecommendation(destinationId, "Premium dinner", "Food", "Special dinner.", 60, "high"),
            CreateRecommendation(destinationId, "Mid market", "Food", "Curated market route.", 60, "medium"));

        var service = CreateService(dbContext);
        var response = await service.CreatePlanAsync(
            user,
            new TravelChatRequest("quiero un plan de coste alto", null, "Tokyo", new DateOnly(2026, 10, 6), null, "es-ES"),
            CancellationToken.None);

        Assert.StartsWith("Busque una opcion premium", response.Message);
        Assert.Equal("Premium dinner", response.Cards[0].Title);
        Assert.Equal("high", response.Cards[0].EstimatedCost);
        Assert.Contains("Coste bajo", response.SuggestedReplies);
    }

    [Theory]
    [InlineData("recomendar plan para caminar", "walking", "Evening walking route")]
    [InlineData("recomendar plan para pareja", "romantic", "Romantic riverside table")]
    [InlineData("recomendar plan nocturno", "nightlife", "Nightlife alley")]
    [InlineData("recomendar plan para bailar", "dance", "Dance club night")]
    public async Task CreatePlanAsync_uses_plan_topic_as_temporary_ranking_signal(
        string message,
        string matchingTag,
        string expectedTitle)
    {
        await using var dbContext = CreateDbContext();
        var destinationId = Guid.NewGuid();
        var matching = CreateRecommendation(
            destinationId,
            expectedTitle,
            "Experience",
            $"Curated {matchingTag} plan in Tokyo.",
            60);
        matching.Tags = [matchingTag];

        var generic = CreateRecommendation(
            destinationId,
            "Generic market stop",
            "Experience",
            "A flexible stop in Tokyo.",
            60);
        generic.Tags = ["neighborhood"];

        var user = await SeedPlanningWorldAsync(dbContext, destinationId, generic, matching);

        var service = CreateService(dbContext);
        var response = await service.CreatePlanAsync(
            user,
            new TravelChatRequest(message, null, "Tokyo", new DateOnly(2026, 10, 6), null, "es-ES"),
            CancellationToken.None);

        Assert.Equal(expectedTitle, response.Cards[0].Title);
        Assert.Contains(matchingTag, response.Cards[0].Tags);
    }

    [Fact]
    public async Task CreatePlanAsync_does_not_fallback_to_food_when_dance_request_has_no_matching_content()
    {
        await using var dbContext = CreateDbContext();
        var destinationId = Guid.NewGuid();
        var food = CreateRecommendation(
            destinationId,
            "Generic dinner stop",
            "Food",
            "Local food in Tokyo.",
            60);
        food.Tags = ["food", "local food"];
        var culture = CreateRecommendation(
            destinationId,
            "Generic culture walk",
            "Culture",
            "A flexible culture walk in Tokyo.",
            60);
        culture.Tags = ["culture"];
        var user = await SeedPlanningWorldAsync(dbContext, destinationId, food, culture);

        var service = CreateService(dbContext);
        var response = await service.CreatePlanAsync(
            user,
            new TravelChatRequest("recomendar plan para bailar", null, "Tokyo", new DateOnly(2026, 10, 6), null, "es-ES"),
            CancellationToken.None);

        Assert.StartsWith("Tenes", response.Message);
        Assert.Empty(response.Cards);
    }

    [Fact]
    public async Task CreatePlanAsync_requires_brunch_semantic_match_for_brunch_requests()
    {
        await using var dbContext = CreateDbContext();
        var destinationId = Guid.NewGuid();
        var genericFood = CreateRecommendation(
            destinationId,
            "Generic cafe stop",
            "Food",
            "Local coffee and snacks in Tokyo.",
            60);
        genericFood.Tags = ["food", "local food"];
        var brunch = CreateRecommendation(
            destinationId,
            "Omotesando brunch table",
            "Food",
            "Bright brunch cafe in Tokyo.",
            90);
        brunch.Tags = ["food"];
        var user = await SeedPlanningWorldAsync(dbContext, destinationId, genericFood, brunch);

        var service = CreateService(dbContext);
        var response = await service.CreatePlanAsync(
            user,
            new TravelChatRequest("quiero un brunch", null, "Tokyo", new DateOnly(2026, 10, 6), null, "es-ES"),
            CancellationToken.None);

        Assert.StartsWith("Busque una opcion de brunch", response.Message);
        Assert.Equal("Omotesando brunch table", response.Cards[0].Title);
        Assert.DoesNotContain(response.Cards, card => card.Title == "Generic cafe stop");
    }

    [Fact]
    public async Task CreatePlanAsync_does_not_fallback_to_generic_food_for_brunch_request()
    {
        await using var dbContext = CreateDbContext();
        var destinationId = Guid.NewGuid();
        var genericFood = CreateRecommendation(
            destinationId,
            "Generic cafe stop",
            "Food",
            "Local coffee and snacks in Tokyo.",
            60);
        genericFood.Tags = ["food", "local food"];
        var user = await SeedPlanningWorldAsync(dbContext, destinationId, genericFood);

        var service = CreateService(dbContext);
        var response = await service.CreatePlanAsync(
            user,
            new TravelChatRequest("quiero un brunch", null, "Tokyo", new DateOnly(2026, 10, 6), null, "es-ES"),
            CancellationToken.None);

        Assert.StartsWith("Tenes", response.Message);
        Assert.Empty(response.Cards);
    }

    [Theory]
    [InlineData("quiero templos", "Culture", "Morning temple", "Quiet temple and shrine route in Tokyo.")]
    [InlineData("recomendar jardines", "Nature", "Hidden garden", "Small garden pause in Tokyo.")]
    [InlineData("compras vintage", "Shopping", "Vintage map", "Curated vintage shops in Tokyo.")]
    [InlineData("mirador al atardecer", "Viewpoint", "Sunset deck", "Observation deck for sunset in Tokyo.")]
    [InlineData("plan de karaoke", "Nightlife", "Karaoke night", "Private karaoke rooms in Tokyo.")]
    [InlineData("quiero un onsen", "Nature", "Onsen pause", "Urban onsen and hot spring pause in Tokyo.")]
    [InlineData("barrio local", "Neighborhood", "Local alley walk", "Local neighborhood alleys in Tokyo.")]
    public async Task CreatePlanAsync_uses_semantic_facets_without_requiring_micro_tags(
        string message,
        string category,
        string expectedTitle,
        string expectedDescription)
    {
        await using var dbContext = CreateDbContext();
        var destinationId = Guid.NewGuid();
        var generic = CreateRecommendation(
            destinationId,
            "Generic category stop",
            category,
            $"Generic {category} recommendation in Tokyo.",
            60);
        generic.Tags = [category.ToLowerInvariant()];
        var matching = CreateRecommendation(
            destinationId,
            expectedTitle,
            category,
            expectedDescription,
            60);
        matching.Tags = [category.ToLowerInvariant()];
        var user = await SeedPlanningWorldAsync(dbContext, destinationId, generic, matching);

        var service = CreateService(dbContext);
        var response = await service.CreatePlanAsync(
            user,
            new TravelChatRequest(message, null, "Tokyo", new DateOnly(2026, 10, 6), null, "es-ES"),
            CancellationToken.None);

        Assert.Equal(expectedTitle, response.Cards[0].Title);
        Assert.DoesNotContain(response.Cards, card => card.Title == "Generic category stop");
    }

    [Fact]
    public async Task CreatePlanAsync_does_not_fallback_to_generic_culture_for_temple_request()
    {
        await using var dbContext = CreateDbContext();
        var destinationId = Guid.NewGuid();
        var genericCulture = CreateRecommendation(
            destinationId,
            "Generic culture stop",
            "Culture",
            "A broad cultural activity in Tokyo.",
            60);
        genericCulture.Tags = ["culture"];
        var user = await SeedPlanningWorldAsync(dbContext, destinationId, genericCulture);

        var service = CreateService(dbContext);
        var response = await service.CreatePlanAsync(
            user,
            new TravelChatRequest("quiero templos", null, "Tokyo", new DateOnly(2026, 10, 6), null, "es-ES"),
            CancellationToken.None);

        Assert.StartsWith("Tenes", response.Message);
        Assert.Empty(response.Cards);
    }

    [Fact]
    public async Task CreatePlanAsync_uses_request_signals_and_reuses_conversation_context_for_followups()
    {
        await using var dbContext = CreateDbContext();
        var destinationId = Guid.NewGuid();
        var user = CreateUser(destinationId);
        dbContext.AppUsers.Add(user);
        dbContext.Destinations.Add(new Destination
        {
            Id = destinationId,
            Name = "Japon",
            Slug = "japon",
            Country = "Japan",
            HeroImageUrl = string.Empty,
            ShortDescription = "Demo"
        });
        dbContext.Trips.Add(new Trip
        {
            Id = Guid.NewGuid(),
            AppUserId = user.Id,
            DestinationId = destinationId,
            TravelerName = "Demo Traveler",
            StartsOn = new DateOnly(2026, 10, 6),
            EndsOn = new DateOnly(2026, 10, 10),
            Reservations =
            [
                CreateReservation("Museum", new TimeOnly(9, 0), "Tokyo"),
                CreateReservation("Dinner", new TimeOnly(18, 0), "Tokyo")
            ]
        });
        dbContext.Recommendations.Add(new Recommendation
        {
            Id = Guid.NewGuid(),
            DestinationId = destinationId,
            Title = "Vegetarian snack stop",
            Category = "Food",
            Neighborhood = "Chuo, Tokyo",
            Description = "Local snacks in Tokyo with vegetarian options.",
            Latitude = 35.665486m,
            Longitude = 139.770667m,
            SuggestedDurationMinutes = 60,
            AccessLevel = ContentAccessLevel.Free
        });
        await dbContext.SaveChangesAsync();

        var service = CreateService(dbContext);
        var firstResponse = await service.CreatePlanAsync(
            user,
            new TravelChatRequest(
                "Quiero comida local vegetariana",
                null,
                "Tokyo",
                new DateOnly(2026, 10, 6),
                null,
                "es-ES"),
            CancellationToken.None);

        var savedPreferences = await dbContext.TravelPreferenceProfiles.FindAsync(user.Id);
        Assert.NotNull(savedPreferences);
        Assert.Contains("Food", savedPreferences.Interests);
        Assert.DoesNotContain("vegetarian", savedPreferences.DietaryRestrictions);

        var followUp = await service.CreatePlanAsync(
            user,
            new TravelChatRequest(
                "Otra opcion",
                firstResponse.ConversationId,
                null,
                null,
                null,
                "es-ES"),
            CancellationToken.None);

        Assert.StartsWith("Busque algo de comida local", followUp.Message);
        Assert.Single(followUp.Cards);
        Assert.Equal("Vegetarian snack stop", followUp.Cards[0].Title);

        var conversation = await dbContext.TravelChatConversations.FindAsync(firstResponse.ConversationId);
        Assert.NotNull(conversation);
        Assert.Equal("Tokyo", conversation.LastCity);
        Assert.Equal(new DateOnly(2026, 10, 6), conversation.LastDate);
    }

    [Fact]
    public async Task CreatePlanAsync_avoids_previous_recommendations_for_alternative_followup()
    {
        await using var dbContext = CreateDbContext();
        var destinationId = Guid.NewGuid();
        var user = CreateUser(destinationId);
        dbContext.AppUsers.Add(user);
        dbContext.Destinations.Add(new Destination
        {
            Id = destinationId,
            Name = "Japon",
            Slug = "japon",
            Country = "Japan",
            HeroImageUrl = string.Empty,
            ShortDescription = "Demo"
        });
        dbContext.Trips.Add(new Trip
        {
            Id = Guid.NewGuid(),
            AppUserId = user.Id,
            DestinationId = destinationId,
            TravelerName = "Demo Traveler",
            StartsOn = new DateOnly(2026, 10, 6),
            EndsOn = new DateOnly(2026, 10, 10),
            Reservations =
            [
                CreateReservation("Museum", new TimeOnly(9, 0), "Tokyo"),
                CreateReservation("Dinner", new TimeOnly(18, 0), "Tokyo")
            ]
        });
        dbContext.Recommendations.AddRange(
            CreateRecommendation(destinationId, "First snack stop", "Food", "Local snacks in Tokyo.", 60),
            CreateRecommendation(destinationId, "Second cafe stop", "Food", "Local coffee and snacks in Tokyo.", 60),
            CreateRecommendation(destinationId, "Third market stop", "Food", "Local market food in Tokyo.", 60),
            CreateRecommendation(destinationId, "Zesty tea alley", "Food", "Quiet local tea in Tokyo.", 45));
        await dbContext.SaveChangesAsync();

        var service = CreateService(dbContext);
        var firstResponse = await service.CreatePlanAsync(
            user,
            new TravelChatRequest(
                "Quiero comida local",
                null,
                "Tokyo",
                new DateOnly(2026, 10, 6),
                null,
                "es-ES"),
            CancellationToken.None);
        var firstRecommendationIds = firstResponse.Cards
            .Select(card => card.RecommendationId)
            .ToHashSet();

        var followUp = await service.CreatePlanAsync(
            user,
            new TravelChatRequest(
                "Otra opcion",
                firstResponse.ConversationId,
                null,
                null,
                null,
                "es-ES"),
            CancellationToken.None);

        Assert.NotEmpty(firstResponse.Cards);
        Assert.NotEmpty(followUp.Cards);
        Assert.All(followUp.Cards, card => Assert.DoesNotContain(card.RecommendationId, firstRecommendationIds));
        Assert.Equal("Zesty tea alley", followUp.Cards[0].Title);
    }

    [Fact]
    public async Task CreatePlanAsync_excludes_explicit_recommendation_id_from_card_actions()
    {
        await using var dbContext = CreateDbContext();
        var destinationId = Guid.NewGuid();
        var user = CreateUser(destinationId);
        dbContext.AppUsers.Add(user);
        dbContext.Destinations.Add(new Destination
        {
            Id = destinationId,
            Name = "Japon",
            Slug = "japon",
            Country = "Japan",
            HeroImageUrl = string.Empty,
            ShortDescription = "Demo"
        });
        dbContext.Trips.Add(new Trip
        {
            Id = Guid.NewGuid(),
            AppUserId = user.Id,
            DestinationId = destinationId,
            TravelerName = "Demo Traveler",
            StartsOn = new DateOnly(2026, 10, 6),
            EndsOn = new DateOnly(2026, 10, 10),
            Reservations =
            [
                CreateReservation("Museum", new TimeOnly(9, 0), "Tokyo"),
                CreateReservation("Dinner", new TimeOnly(18, 0), "Tokyo")
            ]
        });
        dbContext.Recommendations.AddRange(
            CreateRecommendation(destinationId, "First snack stop", "Food", "Local snacks in Tokyo.", 60),
            CreateRecommendation(destinationId, "Second cafe stop", "Food", "Local coffee and snacks in Tokyo.", 60),
            CreateRecommendation(destinationId, "Third market stop", "Food", "Local market food in Tokyo.", 60),
            CreateRecommendation(destinationId, "Zesty tea alley", "Food", "Quiet local tea in Tokyo.", 45));
        await dbContext.SaveChangesAsync();

        var service = CreateService(dbContext);
        var firstResponse = await service.CreatePlanAsync(
            user,
            new TravelChatRequest(
                "Proponeme un plan",
                null,
                "Tokyo",
                new DateOnly(2026, 10, 6),
                null,
                "es-ES"),
            CancellationToken.None);
        var replacedId = firstResponse.Cards[0].RecommendationId;

        var followUp = await service.CreatePlanAsync(
            user,
            new TravelChatRequest(
                $"Reemplazar {replacedId}",
                firstResponse.ConversationId,
                null,
                null,
                null,
                "es-ES"),
            CancellationToken.None);

        Assert.False(string.IsNullOrWhiteSpace(replacedId));
        Assert.NotEmpty(followUp.Cards);
        Assert.DoesNotContain(followUp.Cards, card => card.RecommendationId == replacedId);
    }

    [Fact]
    public async Task CreatePlanAsync_does_not_echo_model_saved_claims_without_backend_confirmation()
    {
        await using var dbContext = CreateDbContext();
        var destinationId = Guid.NewGuid();
        var user = await SeedPlanningWorldAsync(
            dbContext,
            destinationId,
            CreateRecommendation(destinationId, "Tsukiji Snack Walk", "Food", "Local snacks in Tokyo.", 60));

        var service = CreateService(
            dbContext,
            new FakeTravelAiModelClient(new TravelAiModelResult(
                "Listo, quedo guardado en tu agenda.",
                ["Ver agenda"])));
        var response = await service.CreatePlanAsync(
            user,
            new TravelChatRequest("Proponeme un plan", null, "Tokyo", new DateOnly(2026, 10, 6), null, "es-ES"),
            CancellationToken.None);

        Assert.Single(response.Cards);
        Assert.DoesNotContain("guardado", response.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("guardada", response.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("saved", response.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CreatePlanAsync_schedule_chip_returns_schedule_even_after_prior_model_response()
    {
        await using var dbContext = CreateDbContext();
        var destinationId = Guid.NewGuid();
        var user = await SeedPlanningWorldAsync(
            dbContext,
            destinationId,
            CreateRecommendation(destinationId, "Tsukiji Snack Walk", "Food", "Local snacks in Tokyo.", 60));

        var service = CreateService(
            dbContext,
            new FakeTravelAiModelClient(new TravelAiModelResult(
                "Texto del plan anterior que no debe copiarse.",
                ["Ver mi agenda"])));
        var firstResponse = await service.CreatePlanAsync(
            user,
            new TravelChatRequest("Proponeme un plan", null, "Tokyo", new DateOnly(2026, 10, 6), null, "es-ES"),
            CancellationToken.None);

        var scheduleResponse = await service.CreatePlanAsync(
            user,
            new TravelChatRequest("Ver mi agenda", firstResponse.ConversationId, "Tokyo", null, null, "es-ES"),
            CancellationToken.None);

        Assert.Equal("view_schedule", scheduleResponse.Intent);
        Assert.Contains("Tu agenda", scheduleResponse.Message);
        Assert.Contains("Museum", scheduleResponse.Message);
        Assert.DoesNotContain("Texto del plan anterior", scheduleResponse.Message);
        Assert.Empty(scheduleResponse.Cards);
    }

    [Fact]
    public async Task CreatePlanAsync_one_off_avoid_filter_uses_canonical_tag_aliases()
    {
        await using var dbContext = CreateDbContext();
        var destinationId = Guid.NewGuid();
        var onsenRecommendation = CreateRecommendation(
            destinationId,
            "Hakone onsen pause",
            "Wellness",
            "Thermal bath route in Tokyo.",
            60);
        onsenRecommendation.Tags = ["onsen"];
        var foodRecommendation = CreateRecommendation(
            destinationId,
            "Ginza depachika route",
            "Food",
            "Local food in Tokyo.",
            60);
        foodRecommendation.Tags = ["food"];
        var user = await SeedPlanningWorldAsync(dbContext, destinationId, onsenRecommendation, foodRecommendation);

        var service = CreateService(dbContext);
        var confirmation = await service.CreatePlanAsync(
            user,
            new TravelChatRequest(
                "proponeme un plan para 2026-10-06 evitando baños termales",
                null,
                "Tokyo",
                null,
                null,
                "es-ES"),
            CancellationToken.None);

        Assert.Equal("update_preferences", confirmation.Intent);
        Assert.NotNull(confirmation.MissingContext);
        Assert.Contains("onsen", confirmation.Message);

        var response = await service.CreatePlanAsync(
            user,
            new TravelChatRequest("No, solo este pedido", confirmation.ConversationId, "Tokyo", null, null, "es-ES"),
            CancellationToken.None);

        Assert.Null(response.MissingContext);
        Assert.NotEmpty(response.Cards);
        Assert.DoesNotContain(response.Cards, card => card.Tags.Contains("onsen", StringComparer.OrdinalIgnoreCase));
        Assert.Contains(response.Cards, card => card.Tags.Contains("food", StringComparer.OrdinalIgnoreCase));

        var profile = await dbContext.TravelPreferenceProfiles.FindAsync(user.Id);
        Assert.DoesNotContain("onsen", profile!.Dislikes);
    }

    [Fact]
    public async Task Conversation_eval_handles_guided_plan_followups_and_confirmed_actions()
    {
        await using var dbContext = CreateDbContext();
        var destinationId = Guid.NewGuid();
        var firstFood = CreateRecommendation(
            destinationId,
            "First snack stop",
            "Food",
            "Local food in Tokyo.",
            60,
            "medium");
        firstFood.Tags = ["food", "local food"];
        var secondFood = CreateRecommendation(
            destinationId,
            "Second tea stop",
            "Food",
            "Local tea and snacks in Tokyo.",
            45,
            "low");
        secondFood.Tags = ["food", "local food"];
        var freeFood = CreateRecommendation(
            destinationId,
            "Free market stroll",
            "Food",
            "Free market route in Tokyo.",
            45,
            "free");
        freeFood.Tags = ["food", "walking"];
        var highFood = CreateRecommendation(
            destinationId,
            "Premium sushi counter",
            "Food",
            "Premium food counter in Tokyo.",
            75,
            "high");
        highFood.Tags = ["food", "premium"];
        var culture = CreateRecommendation(
            destinationId,
            "Culture gallery pause",
            "Culture",
            "Small culture gallery in Tokyo.",
            60,
            "medium");
        culture.Tags = ["culture", "art"];

        var user = await SeedPlanningWorldAsync(
            dbContext,
            destinationId,
            firstFood,
            secondFood,
            freeFood,
            highFood,
            culture);

        var service = CreateService(
            dbContext,
            new FakeTravelAiModelClient(new TravelAiModelResult(
                "Texto del modelo controlado para plan.",
                ["Ver mi agenda"])));

        var firstPlan = await service.CreatePlanAsync(
            user,
            new TravelChatRequest(
                "Proponeme un plan para 2026-10-06",
                null,
                "Tokyo",
                null,
                null,
                "es-ES"),
            CancellationToken.None);
        var firstPlanIds = firstPlan.Cards.Select(card => card.RecommendationId).ToHashSet();

        var alternative = await service.CreatePlanAsync(
            user,
            new TravelChatRequest(
                "Otra opcion",
                firstPlan.ConversationId,
                null,
                null,
                null,
                "es-ES"),
            CancellationToken.None);

        var avoidCultureConfirmation = await service.CreatePlanAsync(
            user,
            new TravelChatRequest(
                "proponeme un plan para 2026-10-06 evitando culture",
                null,
                "Tokyo",
                null,
                null,
                "es-ES"),
            CancellationToken.None);
        var oneOffAvoidCulture = await service.CreatePlanAsync(
            user,
            new TravelChatRequest(
                "No, solo este pedido",
                avoidCultureConfirmation.ConversationId,
                "Tokyo",
                null,
                null,
                "es-ES"),
            CancellationToken.None);

        Assert.NotNull(avoidCultureConfirmation.MissingContext);
        Assert.Null(oneOffAvoidCulture.MissingContext);
        Assert.DoesNotContain(
            oneOffAvoidCulture.Cards,
            card => card.Tags.Contains("culture", StringComparer.OrdinalIgnoreCase));
        var profileAfterOneOff = await dbContext.TravelPreferenceProfiles.FindAsync(user.Id);
        Assert.DoesNotContain("culture", profileAfterOneOff!.Dislikes);

        var persistentPreferenceConfirmation = await service.CreatePlanAsync(
            user,
            new TravelChatRequest(
                "editar preferencia evitar culture",
                null,
                null,
                null,
                null,
                "es-ES"),
            CancellationToken.None);
        var persistentPreferenceApplied = await service.CreatePlanAsync(
            user,
            new TravelChatRequest(
                "Si, guardar preferencia",
                persistentPreferenceConfirmation.ConversationId,
                null,
                null,
                null,
                "es-ES"),
            CancellationToken.None);

        var lowCost = await service.CreatePlanAsync(
            user,
            new TravelChatRequest(
                "proponeme un plan de coste bajo",
                null,
                "Tokyo",
                new DateOnly(2026, 10, 6),
                null,
                "es-ES"),
            CancellationToken.None);
        var highCost = await service.CreatePlanAsync(
            user,
            new TravelChatRequest(
                "proponeme un plan de coste alto",
                null,
                "Tokyo",
                new DateOnly(2026, 10, 6),
                null,
                "es-ES"),
            CancellationToken.None);

        var schedule = await service.CreatePlanAsync(
            user,
            new TravelChatRequest(
                "Ver mi agenda",
                firstPlan.ConversationId,
                "Tokyo",
                null,
                null,
                "es-ES"),
            CancellationToken.None);
        var saveWithoutCardConfirmation = await service.CreatePlanAsync(
            user,
            new TravelChatRequest(
                "guardar plan",
                firstPlan.ConversationId,
                "Tokyo",
                null,
                null,
                "es-ES"),
            CancellationToken.None);

        Assert.Null(firstPlan.MissingContext);
        Assert.NotEmpty(firstPlan.Cards);
        Assert.NotEmpty(alternative.Cards);
        Assert.All(alternative.Cards, card => Assert.DoesNotContain(card.RecommendationId, firstPlanIds));
        Assert.Equal("update_preferences", persistentPreferenceApplied.Intent);
        var profileAfterPersistentPreference = await dbContext.TravelPreferenceProfiles.FindAsync(user.Id);
        Assert.Contains("culture", profileAfterPersistentPreference!.Dislikes);
        Assert.Equal("free", lowCost.Cards[0].EstimatedCost);
        Assert.Equal("high", highCost.Cards[0].EstimatedCost);
        Assert.Equal("view_schedule", schedule.Intent);
        Assert.Contains("Museum", schedule.Message);
        Assert.NotNull(saveWithoutCardConfirmation.MissingContext);
        Assert.Equal("confirmation", saveWithoutCardConfirmation.MissingContext.Field);
        Assert.DoesNotContain("guardado", saveWithoutCardConfirmation.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static TravelCompanionDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<TravelCompanionDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new TravelCompanionDbContext(options);
    }

    private static async Task<AppUser> SeedPlanningWorldAsync(
        TravelCompanionDbContext dbContext,
        Guid destinationId,
        params Recommendation[] recommendations)
    {
        var user = CreateUser(destinationId);
        dbContext.AppUsers.Add(user);
        dbContext.Destinations.Add(new Destination
        {
            Id = destinationId,
            Name = "Japon",
            Slug = "japon",
            Country = "Japan",
            HeroImageUrl = string.Empty,
            ShortDescription = "Demo"
        });
        dbContext.Trips.Add(new Trip
        {
            Id = Guid.NewGuid(),
            AppUserId = user.Id,
            DestinationId = destinationId,
            TravelerName = "Demo Traveler",
            StartsOn = new DateOnly(2026, 10, 6),
            EndsOn = new DateOnly(2026, 10, 10),
            Reservations =
            [
                CreateReservation("Museum", new TimeOnly(9, 0), "Tokyo"),
                CreateReservation("Dinner", new TimeOnly(18, 0), "Tokyo")
            ]
        });
        dbContext.Recommendations.AddRange(recommendations);
        await dbContext.SaveChangesAsync();
        return user;
    }

    private static TravelChatService CreateService(
        TravelCompanionDbContext dbContext,
        ITravelAiModelClient? modelClient = null,
        OpenAiTravelOptions? openAiOptions = null)
    {
        var intentClassifier = new TravelChatIntentClassifier();
        var textProvider = new TravelAssistantTextProvider();
        var preferenceCommandParser = new TravelPreferenceCommandParser(
            new RecommendationTagCatalogService(dbContext));
        var actionPlanner = new TravelAssistantActionPlanner(
            intentClassifier,
            preferenceCommandParser);
        var telemetry = new TravelAssistantTelemetry(
            NullLogger<TravelAssistantTelemetry>.Instance);

        return new TravelChatService(
            dbContext,
            new UserProfileService(dbContext),
            actionPlanner,
            textProvider,
            new TravelAssistantConversationStateService(
                dbContext,
                NullLogger<TravelAssistantConversationStateService>.Instance),
            new TravelChatResponseComposer(textProvider),
            new TravelRecommendationPlanningService(
                dbContext,
                new DeterministicRecommendationRanker()),
            modelClient ?? new FakeTravelAiModelClient(null),
            Microsoft.Extensions.Options.Options.Create(openAiOptions ?? new OpenAiTravelOptions()),
            telemetry,
            NullLogger<TravelChatService>.Instance);
    }

    private static AppUser CreateUser(Guid destinationId, bool includeProfile = true)
    {
        var userId = Guid.NewGuid();
        var user = new AppUser
        {
            Id = userId,
            Email = "demo@example.test",
            DisplayName = "Demo Traveler",
            Entitlements =
            [
                new UserEntitlement
                {
                    Id = Guid.NewGuid(),
                    UserId = userId,
                    AccessLevel = ContentAccessLevel.Free,
                    DestinationId = destinationId,
                    GrantedAt = DateTimeOffset.UtcNow,
                    Source = "test"
                }
            ]
        };

        if (includeProfile)
        {
            user.TravelPreferenceProfile = new TravelPreferenceProfile
            {
                UserId = userId,
                Interests = ["Food", "Culture", "Coffee"],
                FoodPreferences = ["local food", "snacks"],
                BudgetLevel = "medium",
                TravelPace = "balanced",
                MaxWalkingMinutes = 25
            };
        }

        return user;
    }

    private static Reservation CreateReservation(
        string title,
        TimeOnly startsAt,
        string city,
        DateOnly? date = null)
    {
        return new Reservation
        {
            Id = Guid.NewGuid(),
            Type = ReservationType.Event,
            Date = date ?? new DateOnly(2026, 10, 6),
            StartsAt = startsAt,
            Title = title,
            City = city,
            LocationName = title,
            Address = $"{title} address",
            ConfirmationCode = "CONF",
            Notes = string.Empty
        };
    }

    private static Recommendation CreateRecommendation(
        Guid destinationId,
        string title,
        string category,
        string description,
        int durationMinutes,
        string priceLevel = "medium")
    {
        return new Recommendation
        {
            Id = Guid.NewGuid(),
            DestinationId = destinationId,
            Title = title,
            Category = category,
            Neighborhood = "Chuo, Tokyo",
            Description = description,
            Tags = ["local food"],
            PriceLevel = priceLevel,
            Latitude = 35.665486m,
            Longitude = 139.770667m,
            SuggestedDurationMinutes = durationMinutes,
            Rating = 4.5,
            OpeningHours = "09:00-22:00",
            AccessLevel = ContentAccessLevel.Free
        };
    }

    private sealed class FakeTravelAiModelClient(TravelAiModelResult? result) : ITravelAiModelClient
    {
        public Task<TravelAiModelResult?> CreateStructuredResponseAsync(
            TravelAiModelRequest request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(result);
        }
    }

    private sealed class CapturingTravelAiModelClient : ITravelAiModelClient
    {
        public TravelAiModelRequest? LastRequest { get; private set; }

        public Task<TravelAiModelResult?> CreateStructuredResponseAsync(
            TravelAiModelRequest request,
            CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult<TravelAiModelResult?>(null);
        }
    }

    private sealed class ThrowingTravelAiModelClient : ITravelAiModelClient
    {
        public Task<TravelAiModelResult?> CreateStructuredResponseAsync(
            TravelAiModelRequest request,
            CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("Model unavailable.");
        }
    }
}

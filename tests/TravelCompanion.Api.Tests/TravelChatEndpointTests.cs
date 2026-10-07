using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TravelCompanion.Api.Data;
using TravelCompanion.Api.Models;
using TravelCompanion.Api.Services;
using TravelCompanion.Shared;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Api.Tests;

public sealed partial class TravelChatEndpointTests
{
    private static readonly JsonSerializerOptions SnapshotJsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    [Fact]
    public async Task TravelChat_requires_bearer_session()
    {
        await using var factory = new TravelCompanionApiFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/ai/travel-chat",
            new TravelChatRequest("Ver mi agenda", null, "Tokyo", new DateOnly(2026, 10, 6), null, "es-ES"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task TravelAssistantFeedback_requires_bearer_session()
    {
        await using var factory = new TravelCompanionApiFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/ai/feedback",
            new TravelAssistantFeedbackRequest(
                "conversation",
                Guid.NewGuid(),
                TravelAssistantFeedbackSignal.Helpful,
                "en-US",
                "plan_between_reservations",
                "balanced"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task TravelChat_validates_required_message()
    {
        await using var factory = new TravelCompanionApiFactory();
        var token = await factory.SeedPlanningUserAsync();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.PostAsJsonAsync(
            "/api/ai/travel-chat",
            new TravelChatRequest(" ", null, "Tokyo", new DateOnly(2026, 10, 6), null, "es-ES"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Paid_daily_limit_does_not_suggest_buying_the_pass_again()
    {
        await using var factory = new TravelCompanionApiFactory();
        await factory.SeedPlanningUserAsync();
        string paidToken;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TravelCompanionDbContext>();
            var user = await db.AppUsers.SingleAsync();
            var trip = await db.Trips.SingleAsync();
            var grant = new BuilderAccessGrant
            {
                Id = Guid.NewGuid(), AppUserId = user.Id, DestinationId = trip.DestinationId,
                TripId = trip.Id, IsTrial = false, Status = BuilderAccessStatus.Active,
                PurchasedAtUtc = DateTimeOffset.UtcNow
            };
            db.BuilderAccessGrants.Add(grant);
            db.AssistantDailyUsages.Add(new AssistantDailyUsage
            {
                Id = Guid.NewGuid(), BuilderAccessGrantId = grant.Id,
                UtcDate = DateOnly.FromDateTime(DateTime.UtcNow), SuccessfulRequests = 100
            });
            await db.SaveChangesAsync();
            var sessions = scope.ServiceProvider.GetRequiredService<UserSessionService>();
            (_, paidToken) = await sessions.CreateSessionAsync(user, tripId: trip.Id,
                accessMode: SessionAccessMode.Builder);
        }
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", paidToken);

        var response = await client.PostAsJsonAsync("/api/ai/travel-chat",
            new TravelChatRequest("Busco un lugar", null, "Tokyo",
                new DateOnly(2026, 10, 6), null, "es-ES"));

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<TravelChatResponse>(SnapshotJsonOptions);
        Assert.NotNull(body);
        Assert.Equal("daily_limit", body.Intent);
        Assert.Equal("daily_limit", body.MissingContext?.Field);
        Assert.Empty(body.SuggestedReplies);
    }

    [Fact]
    public async Task TravelChat_returns_stable_structured_contract_for_authenticated_mobile_client()
    {
        await using var factory = new TravelCompanionApiFactory();
        var token = await factory.SeedPlanningUserAsync();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.PostAsJsonAsync(
            "/api/ai/travel-chat",
            new TravelChatRequest(
                "Proponeme un plan para 2026-10-06",
                null,
                "Tokyo",
                null,
                new GeoPointDto(35.665000m, 139.770000m),
                "es-ES"));

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<TravelChatResponse>();

        Assert.NotNull(body);
        Assert.Equal("plan_between_reservations", body.Intent);
        Assert.Null(body.MissingContext);
        Assert.False(string.IsNullOrWhiteSpace(body.ConversationId));
        Assert.NotEmpty(body.Message);
        Assert.NotEmpty(body.SuggestedReplies);
        var card = Assert.Single(body.Cards);
        Assert.Equal("recommendation", card.Type);
        Assert.Equal("Tsukiji Snack Walk", card.Title);
        Assert.False(string.IsNullOrWhiteSpace(card.StartTime));
        Assert.False(string.IsNullOrWhiteSpace(card.EndTime));
        Assert.Equal("medium", card.EstimatedCost);
        Assert.NotNull(card.DistanceKm);
        Assert.NotNull(card.WalkingMinutes);
        Assert.NotEmpty(card.WhyItFits);
        Assert.NotNull(card.Warnings);
        Assert.False(string.IsNullOrWhiteSpace(card.RecommendationId));
        Assert.Contains("food", card.Tags);
    }

    [Theory]
    [InlineData("es-ES", "Además, hay una reserva más.")]
    [InlineData("en-US", "There is 1 more reservation.")]
    public async Task Agenda_uses_only_the_selected_trip_and_preserves_legitimate_matching_reservations(
        string locale, string extraReservationText)
    {
        await using var factory = new TravelCompanionApiFactory();
        var fixture = await factory.SeedOverlappingAgendaTripsAsync();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", fixture.Token);

        var response = await client.PostAsJsonAsync("/api/ai/travel-chat",
            new TravelChatRequest("Ver mi agenda", null, "Tokyo", new(2026, 10, 6), null, locale));

        response.EnsureSuccessStatusCode();
        var agenda = await response.Content.ReadFromJsonAsync<TravelChatResponse>(SnapshotJsonOptions);
        Assert.NotNull(agenda);
        AssertSelectedTripAgenda(agenda, extraReservationText);
    }

    [Fact]
    public async Task Agenda_follows_the_changed_session_trip_even_with_the_same_conversation_and_date()
    {
        await using var factory = new TravelCompanionApiFactory();
        var fixture = await factory.SeedOverlappingAgendaTripsAsync();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", fixture.Token);
        var request = new TravelChatRequest("Ver mi agenda", null, "Tokyo", new(2026, 10, 6), null, "es-ES");
        var firstResponse = await client.PostAsJsonAsync("/api/ai/travel-chat", request);
        firstResponse.EnsureSuccessStatusCode();
        var firstAgenda = await firstResponse.Content.ReadFromJsonAsync<TravelChatResponse>(SnapshotJsonOptions);
        Assert.NotNull(firstAgenda);
        AssertSelectedTripAgenda(firstAgenda, "Además, hay una reserva más.");

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TravelCompanionDbContext>();
            var session = await db.AppUserSessions.SingleAsync(item => item.Id == fixture.SessionId);
            session.TripId = fixture.OtherTripId;
            await db.SaveChangesAsync();
        }
        var nextResponse = await client.PostAsJsonAsync("/api/ai/travel-chat",
            request with { ConversationId = firstAgenda.ConversationId });

        nextResponse.EnsureSuccessStatusCode();
        var nextAgenda = await nextResponse.Content.ReadFromJsonAsync<TravelChatResponse>(SnapshotJsonOptions);
        Assert.NotNull(nextAgenda);
        Assert.Equal(firstAgenda.ConversationId, nextAgenda.ConversationId);
        Assert.Equal("view_schedule", nextAgenda.Intent);
        Assert.Null(nextAgenda.MissingContext);
        Assert.Equal(3, Regex.Matches(nextAgenda.Message, "^- ", RegexOptions.Multiline).Count);
        Assert.Single(Regex.Matches(nextAgenda.Message, "Hotel Yuku"));
        Assert.Single(Regex.Matches(nextAgenda.Message, "Gyoza"));
        Assert.Contains("Solo otro viaje", nextAgenda.Message);
        Assert.DoesNotContain("Solo primer viaje", nextAgenda.Message);
        Assert.DoesNotContain("Museum", nextAgenda.Message);
        Assert.DoesNotContain("Solo viaje archivado", nextAgenda.Message);
        Assert.DoesNotContain("Solo otra cuenta", nextAgenda.Message);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Agenda_without_a_selected_trip_requests_context_when_multiple_published_trips_match(
        bool supplyUntrustedTripId)
    {
        await using var factory = new TravelCompanionApiFactory();
        var fixture = await factory.SeedOverlappingAgendaTripsAsync(bindSelectedTrip: false);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", fixture.Token);
        var action = supplyUntrustedTripId
            ? new GuidedTravelActionDto(GuidedTravelActions.Recommend) { TripId = fixture.OtherTripId }
            : null;

        var response = await client.PostAsJsonAsync("/api/ai/travel-chat",
            new TravelChatRequest("Ver mi agenda", null, "Tokyo", new(2026, 10, 6), null, "es-ES", action));

        response.EnsureSuccessStatusCode();
        var agenda = await response.Content.ReadFromJsonAsync<TravelChatResponse>(SnapshotJsonOptions);
        Assert.NotNull(agenda);
        Assert.Equal("trip", agenda.MissingContext?.Field);
        Assert.Empty(agenda.Cards);
        Assert.Empty(agenda.SuggestedReplies);
        Assert.Contains("Cuenta", agenda.Message);
        Assert.DoesNotContain("Hotel Yuku", agenda.Message);
        Assert.DoesNotContain("Gyoza", agenda.Message);
        Assert.DoesNotContain("Solo primer viaje", agenda.Message);
        Assert.DoesNotContain("Solo otro viaje", agenda.Message);
    }

    [Theory]
    [InlineData("other-trip")]
    [InlineData("archived-trip")]
    [InlineData("other-account")]
    public async Task Agenda_does_not_allow_a_guided_payload_to_replace_the_authenticated_trip(string target)
    {
        await using var factory = new TravelCompanionApiFactory();
        var fixture = await factory.SeedOverlappingAgendaTripsAsync();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", fixture.Token);
        var untrustedTrip = target switch
        {
            "other-trip" => fixture.OtherTripId,
            "archived-trip" => fixture.ArchivedTripId,
            _ => fixture.ForeignTripId
        };
        var action = new GuidedTravelActionDto(GuidedTravelActions.Recommend) { TripId = untrustedTrip };

        var response = await client.PostAsJsonAsync("/api/ai/travel-chat",
            new TravelChatRequest("Ver mi agenda", null, "Tokyo", new(2026, 10, 6), null, "es-ES", action));

        response.EnsureSuccessStatusCode();
        var agenda = await response.Content.ReadFromJsonAsync<TravelChatResponse>(SnapshotJsonOptions);
        Assert.NotNull(agenda);
        AssertSelectedTripAgenda(agenda, "Además, hay una reserva más.");
    }

    [Fact]
    public async Task Agenda_without_a_selected_trip_keeps_legacy_access_when_only_one_valid_trip_matches()
    {
        await using var factory = new TravelCompanionApiFactory();
        var fixture = await factory.SeedOverlappingAgendaTripsAsync(bindSelectedTrip: false);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TravelCompanionDbContext>();
            (await db.Trips.SingleAsync(item => item.Id == fixture.OtherTripId)).PublicationStatus = TripPublicationStatus.Draft;
            await db.SaveChangesAsync();
        }
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", fixture.Token);

        var response = await client.PostAsJsonAsync("/api/ai/travel-chat",
            new TravelChatRequest("Ver mi agenda", null, "Tokyo", new(2026, 10, 6), null, "es-ES"));

        response.EnsureSuccessStatusCode();
        var agenda = await response.Content.ReadFromJsonAsync<TravelChatResponse>(SnapshotJsonOptions);
        Assert.NotNull(agenda);
        AssertSelectedTripAgenda(agenda, "Además, hay una reserva más.");
    }

    private static void AssertSelectedTripAgenda(TravelChatResponse agenda, string extraReservationText)
    {
        Assert.Equal("view_schedule", agenda.Intent);
        Assert.Null(agenda.MissingContext);
        Assert.Empty(agenda.Cards);
        Assert.Equal(5, Regex.Matches(agenda.Message, "^- ", RegexOptions.Multiline).Count);
        Assert.Single(Regex.Matches(agenda.Message, "Hotel Yuku"));
        Assert.Equal(2, Regex.Matches(agenda.Message, "Gyoza").Count);
        Assert.Contains("Museum", agenda.Message);
        Assert.Contains("Solo primer viaje", agenda.Message);
        Assert.Contains("\n" + extraReservationText, agenda.Message);
        Assert.DoesNotContain("Solo otro viaje", agenda.Message);
        Assert.DoesNotContain("Solo viaje archivado", agenda.Message);
        Assert.DoesNotContain("Solo otra cuenta", agenda.Message);
    }

    [Fact]
    public async Task TravelAssistantFeedback_records_signal_without_updating_preferences()
    {
        await using var factory = new TravelCompanionApiFactory();
        var token = await factory.SeedPlanningUserAsync();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var chatResponse = await client.PostAsJsonAsync(
            "/api/ai/travel-chat",
            new TravelChatRequest(
                "Proponeme un plan para 2026-10-06",
                null,
                "Tokyo",
                null,
                new GeoPointDto(35.665000m, 139.770000m),
                "es-ES"));

        chatResponse.EnsureSuccessStatusCode();
        var chat = await chatResponse.Content.ReadFromJsonAsync<TravelChatResponse>();
        Assert.NotNull(chat);
        var recommendationId = Guid.Parse(chat.Cards[0].RecommendationId!);

        var feedbackResponse = await client.PostAsJsonAsync(
            "/api/ai/feedback",
            new TravelAssistantFeedbackRequest(
                chat.ConversationId,
                recommendationId,
                TravelAssistantFeedbackSignal.HideSimilar,
                "es-ES",
                chat.Intent,
                "balanced"));

        feedbackResponse.EnsureSuccessStatusCode();
        var feedback = await feedbackResponse.Content.ReadFromJsonAsync<TravelAssistantFeedbackResponse>();
        Assert.NotNull(feedback);
        Assert.True(feedback.Accepted);

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<TravelCompanionDbContext>();
        var savedFeedback = await dbContext.TravelAssistantFeedbackItems.SingleAsync();
        Assert.Equal(TravelAssistantFeedbackSignal.HideSimilar, savedFeedback.Signal);
        Assert.Equal(recommendationId, savedFeedback.RecommendationId);

        var conversation = await dbContext.TravelChatConversations.FindAsync(chat.ConversationId);
        Assert.NotNull(conversation);
        Assert.Contains("food", conversation!.StateJson, StringComparison.OrdinalIgnoreCase);

        var profile = await dbContext.TravelPreferenceProfiles.SingleAsync();
        Assert.DoesNotContain("food", profile.Dislikes, StringComparer.OrdinalIgnoreCase);
    }

    [Theory]
    [MemberData(nameof(ContractSnapshotCases))]
    public async Task TravelChat_matches_contract_snapshot(
        string snapshotName,
        TravelChatRequest request,
        bool includeProfile)
    {
        await using var factory = new TravelCompanionApiFactory();
        var token = await factory.SeedPlanningUserAsync(includeProfile);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.PostAsJsonAsync("/api/ai/travel-chat", request);

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<TravelChatResponse>();
        Assert.NotNull(body);
        AssertSnapshot(snapshotName, body);
    }

    public static TheoryData<string, TravelChatRequest, bool> ContractSnapshotCases => new()
    {
        {
            "travel-chat-plan.json",
            new TravelChatRequest(
                "Proponeme un plan para 2026-10-06",
                null,
                "Tokyo",
                null,
                new GeoPointDto(35.665000m, 139.770000m),
                "es-ES"),
            true
        },
        {
            "travel-chat-missing-preferences.json",
            new TravelChatRequest("Proponeme un plan", null, "Tokyo", new DateOnly(2026, 10, 6), null, "es-ES"),
            false
        },
        {
            "travel-chat-schedule.json",
            new TravelChatRequest("Ver mi agenda", null, "Tokyo", new DateOnly(2026, 10, 6), null, "es-ES"),
            true
        },
        {
            "travel-chat-preference-confirmation.json",
            new TravelChatRequest("editar preferencia evitar culture", null, "Tokyo", new DateOnly(2026, 10, 6), null, "es-ES"),
            true
        },
        {
            "travel-chat-unsupported-command.json",
            new TravelChatRequest("mensaje raro que no entiendo", null, "Tokyo", new DateOnly(2026, 10, 6), null, "es-ES"),
            true
        },
        {
            "travel-chat-save-requires-confirmation.json",
            new TravelChatRequest("guardar plan", null, "Tokyo", new DateOnly(2026, 10, 6), null, "es-ES"),
            true
        }
    };

    private static void AssertSnapshot(string snapshotName, TravelChatResponse response)
    {
        var snapshotPath = GetSnapshotPath(snapshotName);
        var actual = NormalizeSnapshot(JsonSerializer.Serialize(response, SnapshotJsonOptions));

        if (Environment.GetEnvironmentVariable("TRAVELCOMPANION_ACCEPT_SNAPSHOTS") == "1")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(snapshotPath)!);
            File.WriteAllText(snapshotPath, actual + Environment.NewLine);
        }

        var expected = File.ReadAllText(snapshotPath).Trim();
        Assert.Equal(expected, actual);
    }

    private static string NormalizeSnapshot(string json)
    {
        return Regex.Replace(
            json,
            @"[0-9a-fA-F]{8}(?:-[0-9a-fA-F]{4}){3}-[0-9a-fA-F]{12}|[0-9a-fA-F]{32}",
            "<id>").Trim();
    }

    private static string GetSnapshotPath(string snapshotName)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "tests", "TravelCompanion.Api.Tests", "Snapshots");
            if (Directory.Exists(candidate))
            {
                return Path.Combine(candidate, snapshotName);
            }

            directory = directory.Parent;
        }

        return Path.Combine(AppContext.BaseDirectory, "Snapshots", snapshotName);
    }

    private sealed class TravelCompanionApiFactory : WebApplicationFactory<Program>
    {
        private readonly string databaseName = $"travel-companion-api-tests-{Guid.NewGuid():N}";

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<DbContextOptions<TravelCompanionDbContext>>();
                services.RemoveAll<IDbContextOptionsConfiguration<TravelCompanionDbContext>>();
                services.AddDbContext<TravelCompanionDbContext>(options =>
                    options.UseInMemoryDatabase(databaseName));

                services.RemoveAll<ITravelAiModelClient>();
                services.AddSingleton<ITravelAiModelClient, NullTravelAiModelClient>();
            });
        }

        public async Task<string> SeedPlanningUserAsync(bool includeProfile = true)
        {
            using var scope = Services.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<TravelCompanionDbContext>();
            var passwordHasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher<AppUser>>();
            var sessionService = scope.ServiceProvider.GetRequiredService<UserSessionService>();

            var destinationId = Guid.NewGuid();
            var user = new AppUser
            {
                Id = Guid.NewGuid(),
                Email = "endpoint@example.test",
                DisplayName = "Endpoint Traveler",
                PasswordHash = string.Empty,
                Entitlements =
                [
                    new UserEntitlement
                    {
                        Id = Guid.NewGuid(),
                        AccessLevel = ContentAccessLevel.Free,
                        DestinationId = destinationId,
                        GrantedAt = DateTimeOffset.UtcNow,
                        Source = "test"
                    }
                ]
            };
            user.PasswordHash = passwordHasher.HashPassword(user, "Password123!");
            if (includeProfile)
            {
                user.TravelPreferenceProfile = new TravelPreferenceProfile
                {
                    UserId = user.Id,
                    Interests = ["Food", "Culture"],
                    FoodPreferences = ["local food"],
                    BudgetLevel = "medium",
                    TravelPace = "balanced",
                    MaxWalkingMinutes = 25
                };
            }

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
                TravelerName = "Endpoint Traveler",
                StartsOn = new DateOnly(2026, 10, 6),
                EndsOn = new DateOnly(2026, 10, 10),
                Reservations =
                [
                    new Reservation
                    {
                        Id = Guid.NewGuid(),
                        Type = ReservationType.Event,
                        Date = new DateOnly(2026, 10, 6),
                        StartsAt = new TimeOnly(9, 0),
                        Title = "Museum",
                        City = "Tokyo",
                        LocationName = "Museum",
                        Address = "Museum address",
                        ConfirmationCode = "MUSEUM",
                        Notes = string.Empty
                    },
                    new Reservation
                    {
                        Id = Guid.NewGuid(),
                        Type = ReservationType.Event,
                        Date = new DateOnly(2026, 10, 6),
                        StartsAt = new TimeOnly(18, 0),
                        Title = "Dinner",
                        City = "Tokyo",
                        LocationName = "Dinner",
                        Address = "Dinner address",
                        ConfirmationCode = "DINNER",
                        Notes = string.Empty
                    }
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
                Tags = ["food", "local food"],
                PriceLevel = "medium",
                Latitude = 35.665486m,
                Longitude = 139.770667m,
                SuggestedDurationMinutes = 60,
                Rating = 4.6,
                OpeningHours = "09:00-22:00",
                AccessLevel = ContentAccessLevel.Free
            });
            await dbContext.SaveChangesAsync();

            var (session, token) = await sessionService.CreateSessionAsync(user);
            session.LastSeenAt = DateTimeOffset.UtcNow;
            await dbContext.SaveChangesAsync();
            return token;
        }

        public async Task<(string Token, Guid SessionId, Guid SelectedTripId, Guid OtherTripId,
            Guid ArchivedTripId, Guid ForeignTripId)> SeedOverlappingAgendaTripsAsync(bool bindSelectedTrip = true)
        {
            var token = await SeedPlanningUserAsync();
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<TravelCompanionDbContext>();
            var user = await db.AppUsers.SingleAsync();
            var selected = await db.Trips.Include(item => item.Reservations).SingleAsync();
            Reservation[] selectedBookings = [
                Booking("Hotel Yuku", new(0, 0), ReservationType.Lodging),
                Booking("Gyoza", new(10, 0)),
                Booking("Gyoza", new(10, 0)),
                Booking("Solo primer viaje", new(12, 0))
            ];
            foreach (var booking in selectedBookings) booking.TripId = selected.Id;
            selected.Reservations.AddRange(selectedBookings);
            db.Reservations.AddRange(selectedBookings);
            var other = OverlappingTrip(user.Id, "Solo otro viaje");
            var archived = OverlappingTrip(user.Id, "Solo viaje archivado");
            archived.IsArchived = true;
            var foreignOwner = new AppUser
            {
                Id = Guid.NewGuid(), Email = "another-agenda@example.test", DisplayName = "Another traveler"
            };
            var foreign = OverlappingTrip(foreignOwner.Id, "Solo otra cuenta");
            db.AppUsers.Add(foreignOwner);
            db.Trips.AddRange(other, archived, foreign);
            var session = await db.AppUserSessions.SingleAsync();
            session.TripId = bindSelectedTrip ? selected.Id : null;
            await db.SaveChangesAsync();
            return (token, session.Id, selected.Id, other.Id, archived.Id, foreign.Id);

            Trip OverlappingTrip(Guid owner, string exclusiveTitle) => new()
            {
                Id = Guid.NewGuid(), AppUserId = owner, DestinationId = selected.DestinationId,
                TravelerName = "Overlapping traveler", StartsOn = selected.StartsOn, EndsOn = selected.EndsOn,
                PublicationStatus = TripPublicationStatus.Published,
                Reservations = [Booking("Hotel Yuku", new(0, 0), ReservationType.Lodging),
                    Booking("Gyoza", new(10, 0)), Booking(exclusiveTitle, new(0, 10))]
            };

            static Reservation Booking(string title, TimeOnly time, ReservationType type = ReservationType.Event) => new()
            {
                Id = Guid.NewGuid(), Type = type, Date = new(2026, 10, 6), StartsAt = time,
                Title = title, City = "Tokyo", LocationName = title, Address = "Synthetic address",
                ConfirmationCode = Guid.NewGuid().ToString("N"), Notes = "Synthetic agenda scope validation"
            };
        }
    }

    private sealed class NullTravelAiModelClient : ITravelAiModelClient
    {
        public Task<TravelAiModelResult?> CreateStructuredResponseAsync(
            TravelAiModelRequest request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult<TravelAiModelResult?>(null);
        }
    }
}

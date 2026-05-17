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

public sealed class TravelChatEndpointTests
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
            new TravelChatRequest("editar preferencia evitar #culture", null, "Tokyo", new DateOnly(2026, 10, 6), null, "es-ES"),
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

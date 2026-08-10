using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Hosting;
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

public sealed class TodayRecommendationEndpointTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    [Fact]
    public async Task Today_requires_bearer_session()
    {
        await using var factory = new TravelCompanionApiFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/mobile/today");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Today_fills_free_periods_without_reusing_assigned_recommendations()
    {
        await using var factory = new TravelCompanionApiFactory();
        var seed = await factory.SeedTodayWorldAsync();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", seed.Token);

        var signalResponse = await client.PostAsJsonAsync(
            $"/api/mobile/recommendations/{seed.VisitedRecommendationId}/signals",
            new RecommendationSignalRequest(
                RecommendationSignal.VisitedConfirmed,
                "test",
                35.671m,
                139.765m,
                80,
                0.9m,
                DateTimeOffset.UtcNow),
            JsonOptions);
        signalResponse.EnsureSuccessStatusCode();

        var today = await client.GetFromJsonAsync<TodayDto>(
            $"/api/mobile/today?date={seed.FreeDate:yyyy-MM-dd}&latitude=35.671&longitude=139.765",
            JsonOptions);

        Assert.NotNull(today);
        var morning = Assert.Single(today.Sections, section => section.PeriodKey == "morning");
        Assert.NotEmpty(morning.Reservations);
        Assert.Empty(morning.Recommendations);

        var afternoon = Assert.Single(today.Sections, section => section.PeriodKey == "afternoon");
        Assert.Empty(afternoon.Reservations);
        Assert.InRange(afternoon.Recommendations.Count, 1, 2);
        Assert.DoesNotContain(afternoon.Recommendations, recommendation =>
            recommendation.Recommendation.Id == seed.AssignedRecommendationId);
        Assert.Contains(afternoon.Recommendations, recommendation =>
            recommendation.Recommendation.Id == seed.VisitedRecommendationId
            && recommendation.IsVisited
            && recommendation.VisitStatusLabel == "Ya visitado");
        Assert.NotEqual(seed.VisitedRecommendationId, afternoon.Recommendations[0].Recommendation.Id);
    }

    [Fact]
    public async Task Save_itinerary_item_persists_recommendation_id()
    {
        await using var factory = new TravelCompanionApiFactory();
        var seed = await factory.SeedTodayWorldAsync();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", seed.Token);

        var response = await client.PostAsJsonAsync(
            "/api/ai/save_itinerary_item",
            new SaveItineraryItemRequest(
                seed.UnassignedRecommendationId,
                seed.FreeDate,
                new TimeOnly(16, 30),
                null),
            JsonOptions);
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<SaveItineraryItemResponse>(JsonOptions);
        Assert.NotNull(payload);
        Assert.True(payload.Saved);
        Assert.Equal(seed.UnassignedRecommendationId, payload.Item?.RecommendationId);
    }

    private sealed class TravelCompanionApiFactory : WebApplicationFactory<Program>
    {
        private readonly string databaseName = $"today-recommendations-tests-{Guid.NewGuid():N}";

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

        public async Task<SeedResult> SeedTodayWorldAsync()
        {
            using var scope = Services.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<TravelCompanionDbContext>();
            var sessionService = scope.ServiceProvider.GetRequiredService<UserSessionService>();

            var destinationId = Guid.NewGuid();
            var tripId = Guid.NewGuid();
            var assignedRecommendationId = Guid.NewGuid();
            var visitedRecommendationId = Guid.NewGuid();
            var unassignedRecommendationId = Guid.NewGuid();
            var freeDate = new DateOnly(2026, 10, 22);
            var user = new AppUser
            {
                Id = Guid.NewGuid(),
                Email = "today@example.test",
                DisplayName = "Today Traveler",
                PasswordHash = string.Empty,
                MustChangePassword = false
            };

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
                Id = tripId,
                AppUserId = user.Id,
                DestinationId = destinationId,
                TravelerName = "Today Traveler",
                StartsOn = new DateOnly(2026, 10, 20),
                EndsOn = new DateOnly(2026, 10, 30),
                Reservations =
                [
                    CreateReservation("Morning booking", freeDate, new TimeOnly(9, 0), "Tokyo"),
                    CreateReservation("Lunch booking", freeDate, new TimeOnly(12, 30), "Tokyo"),
                    CreateReservation("Night booking", freeDate, new TimeOnly(21, 0), "Tokyo"),
                    CreateReservation("Assigned route", freeDate.AddDays(1), new TimeOnly(16, 0), "Tokyo", assignedRecommendationId)
                ]
            });
            dbContext.Recommendations.AddRange(
                CreateRecommendation(destinationId, assignedRecommendationId, "Already assigned tea route", ["tea", "walk", "culture"]),
                CreateRecommendation(destinationId, unassignedRecommendationId, "Quiet tea alley", ["tea", "walk", "culture"]),
                CreateRecommendation(destinationId, visitedRecommendationId, "Visited garden tea", ["tea", "walk", "culture"]));
            await dbContext.SaveChangesAsync();

            var (_, token) = await sessionService.CreateSessionAsync(user, tripId: tripId);
            return new SeedResult(
                token,
                freeDate,
                assignedRecommendationId,
                unassignedRecommendationId,
                visitedRecommendationId);
        }

        private static Reservation CreateReservation(
            string title,
            DateOnly date,
            TimeOnly startsAt,
            string city,
            Guid? recommendationId = null) =>
            new()
            {
                Id = Guid.NewGuid(),
                RecommendationId = recommendationId,
                Type = ReservationType.Event,
                Date = date,
                StartsAt = startsAt,
                Title = title,
                City = city,
                LocationName = title,
                Address = $"{title} address",
                ConfirmationCode = "CONF",
                Notes = string.Empty
            };

        private static Recommendation CreateRecommendation(
            Guid destinationId,
            Guid id,
            string title,
            IReadOnlyList<string> tags) =>
            new()
            {
                Id = id,
                DestinationId = destinationId,
                Title = title,
                Category = "Culture",
                Neighborhood = "Tokyo, Japan",
                Description = "A calm afternoon option in Tokyo.",
                Tags = tags.ToList(),
                PriceLevel = "medium",
                Latitude = 35.671m,
                Longitude = 139.765m,
                SuggestedDurationMinutes = 75,
                Rating = 4.5,
                OpeningHours = "10:00-19:00",
                AccessLevel = ContentAccessLevel.Free
            };
    }

    private sealed record SeedResult(
        string Token,
        DateOnly FreeDate,
        Guid AssignedRecommendationId,
        Guid UnassignedRecommendationId,
        Guid VisitedRecommendationId);

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

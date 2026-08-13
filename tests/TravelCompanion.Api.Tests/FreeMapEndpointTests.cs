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

public sealed class FreeMapEndpointTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    [Fact]
    public async Task Pin_0000_returns_preview_session_without_trip_and_blocks_private_endpoints()
    {
        await using var factory = new FreeMapApiFactory();
        await factory.SeedMapAsync();
        using var client = factory.CreateClient();

        var login = await client.PostAsJsonAsync("/api/auth/pin-login", new PinLoginRequestDto("0000"));
        login.EnsureSuccessStatusCode();
        var session = await login.Content.ReadFromJsonAsync<AuthSessionDto>(JsonOptions);

        Assert.NotNull(session);
        Assert.Equal(SessionAccessMode.FreeMapPreview, session.AccessMode);
        Assert.Null(session.TripId);
        Assert.False(session.MustChangePassword);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session.Token);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/mobile/bootstrap")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/mobile/today")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/mobile/docs")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/me/schedule")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync(
            "/api/ai/travel-chat",
            new { message = "plan", locale = "es" })).StatusCode);
    }

    [Fact]
    public async Task Free_map_returns_full_inside_marker_and_redacted_stable_outside_marker()
    {
        await using var factory = new FreeMapApiFactory();
        var seed = await factory.SeedMapAsync();
        using var client = factory.CreateClient();
        var session = await LoginAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session.Token);

        var cities = await client.GetFromJsonAsync<IReadOnlyList<FreeMapCityDto>>(
            "/api/mobile/free-map/cities",
            JsonOptions);
        var firstResponse = await client.GetAsync("/api/mobile/free-map/tokyo");
        var rawJson = await firstResponse.Content.ReadAsStringAsync();
        var first = JsonSerializer.Deserialize<FreeMapPreviewDto>(rawJson, JsonOptions);
        var second = await client.GetFromJsonAsync<FreeMapPreviewDto>(
            "/api/mobile/free-map/tokyo",
            JsonOptions);

        Assert.Single(cities!);
        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.Equal(1, first.UnlockedCount);
        Assert.Equal(1, first.LockedCount);

        var unlocked = Assert.Single(first.Markers, marker => marker.Access == FreeMapMarkerAccess.Unlocked);
        Assert.NotNull(unlocked.Recommendation);
        Assert.Equal(seed.InsideRecommendationId, unlocked.Recommendation.Id);
        Assert.Equal("Inside ramen", unlocked.Recommendation.Title);
        Assert.Equal(unlocked.Recommendation.Latitude, unlocked.Latitude);
        Assert.Equal(unlocked.Recommendation.Longitude, unlocked.Longitude);

        var locked = Assert.Single(first.Markers, marker => marker.Access == FreeMapMarkerAccess.Locked);
        var lockedAgain = Assert.Single(second.Markers, marker => marker.Access == FreeMapMarkerAccess.Locked);
        Assert.Null(locked.Recommendation);
        Assert.NotEqual(seed.LockedLatitude, locked.Latitude);
        Assert.NotEqual(seed.LockedLongitude, locked.Longitude);
        Assert.Equal(locked.MarkerKey, lockedAgain.MarkerKey);
        Assert.Equal(locked.Latitude, lockedAgain.Latitude);
        Assert.Equal(locked.Longitude, lockedAgain.Longitude);
        Assert.True(CalculateDistanceKm(35.681236m, 139.767125m, locked.Latitude, locked.Longitude) >= 2.15m);
        Assert.DoesNotContain(seed.LockedRecommendationId.ToString(), rawJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Secret bar", rawJson, StringComparison.Ordinal);
        Assert.DoesNotContain(first.Markers, marker => marker.Recommendation?.Title == "Kyoto cafe");
    }

    [Fact]
    public async Task Trip_session_cannot_use_free_map_endpoint()
    {
        await using var factory = new FreeMapApiFactory();
        await factory.SeedMapAsync();
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<TravelCompanionDbContext>();
        var user = new AppUser
        {
            Id = Guid.NewGuid(),
            Email = "trip-map@example.test",
            DisplayName = "Trip user",
            PasswordHash = string.Empty
        };
        dbContext.AppUsers.Add(user);
        await dbContext.SaveChangesAsync();
        var sessionService = scope.ServiceProvider.GetRequiredService<UserSessionService>();
        var (_, token) = await sessionService.CreateSessionAsync(user);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/mobile/free-map/tokyo")).StatusCode);
    }

    private static async Task<AuthSessionDto> LoginAsync(HttpClient client)
    {
        var login = await client.PostAsJsonAsync("/api/auth/pin-login", new PinLoginRequestDto("0000"));
        login.EnsureSuccessStatusCode();
        return (await login.Content.ReadFromJsonAsync<AuthSessionDto>(JsonOptions))!;
    }

    private static decimal CalculateDistanceKm(
        decimal originLatitude,
        decimal originLongitude,
        decimal targetLatitude,
        decimal targetLongitude)
    {
        const double earthRadiusKm = 6371;
        static double ToRadians(decimal degrees) => (double)degrees * Math.PI / 180;
        var latitudeDelta = ToRadians(targetLatitude - originLatitude);
        var longitudeDelta = ToRadians(targetLongitude - originLongitude);
        var originLatitudeRadians = ToRadians(originLatitude);
        var targetLatitudeRadians = ToRadians(targetLatitude);
        var a = Math.Sin(latitudeDelta / 2) * Math.Sin(latitudeDelta / 2)
            + Math.Cos(originLatitudeRadians) * Math.Cos(targetLatitudeRadians)
            * Math.Sin(longitudeDelta / 2) * Math.Sin(longitudeDelta / 2);
        return (decimal)(earthRadiusKm * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a)));
    }

    private sealed class FreeMapApiFactory : WebApplicationFactory<Program>
    {
        private readonly string databaseName = $"free-map-tests-{Guid.NewGuid():N}";

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<DbContextOptions<TravelCompanionDbContext>>();
                services.RemoveAll<IDbContextOptionsConfiguration<TravelCompanionDbContext>>();
                services.AddDbContext<TravelCompanionDbContext>(options =>
                    options.UseInMemoryDatabase(databaseName));
            });
        }

        public async Task<SeedResult> SeedMapAsync()
        {
            using var scope = Services.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<TravelCompanionDbContext>();
            var destinationId = Guid.NewGuid();
            var insideId = Guid.NewGuid();
            var lockedId = Guid.NewGuid();
            dbContext.Destinations.Add(new Destination
            {
                Id = destinationId,
                Name = "Japon",
                Slug = "japon",
                Country = "Japan",
                HeroImageUrl = string.Empty,
                ShortDescription = "Japan"
            });
            dbContext.FreeMapCities.Add(new FreeMapCity
            {
                Id = Guid.NewGuid(),
                DestinationId = destinationId,
                CitySlug = "tokyo",
                DisplayName = "Tokyo",
                CenterLatitude = 35.681236m,
                CenterLongitude = 139.767125m,
                FreeRadiusKm = 2m,
                CoverageRadiusKm = 20m,
                IsEnabled = true,
                SortOrder = 1
            });
            dbContext.Recommendations.AddRange(
                CreateRecommendation(insideId, destinationId, "Inside ramen", "tokyo", 35.686000m, 139.767125m),
                CreateRecommendation(lockedId, destinationId, "Secret bar", "tokyo", 35.711236m, 139.767125m),
                CreateRecommendation(Guid.NewGuid(), destinationId, "Kyoto cafe", "kyoto", 35.0037m, 135.7688m));
            await dbContext.SaveChangesAsync();
            return new SeedResult(insideId, lockedId, 35.711236m, 139.767125m);
        }

        private static Recommendation CreateRecommendation(
            Guid id,
            Guid destinationId,
            string title,
            string citySlug,
            decimal latitude,
            decimal longitude) => new()
        {
            Id = id,
            DestinationId = destinationId,
            ExternalId = $"test-{id:N}",
            Title = title,
            Category = "Food",
            Neighborhood = $"{citySlug}, Japan",
            CitySlug = citySlug,
            Description = $"Description for {title}",
            Tags = ["food"],
            PriceLevel = "medium",
            Latitude = latitude,
            Longitude = longitude,
            SuggestedDurationMinutes = 60,
            AccessLevel = ContentAccessLevel.Free
        };
    }

    private sealed record SeedResult(
        Guid InsideRecommendationId,
        Guid LockedRecommendationId,
        decimal LockedLatitude,
        decimal LockedLongitude);
}

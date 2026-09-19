using System.Net.Http.Headers;
using System.Net.Http.Json;
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

public sealed class BuilderSetupEndpointTests
{
    [Fact]
    public async Task Revoked_builder_grant_invalidates_existing_session()
    {
        await using var factory = new BuilderApiFactory();
        var token = await factory.SeedBuilderAsync();
        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<TravelCompanionDbContext>();
            var grant = await dbContext.BuilderAccessGrants.SingleAsync();
            grant.Status = BuilderAccessStatus.Revoked;
            grant.RevokedAtUtc = DateTimeOffset.UtcNow;
            await dbContext.SaveChangesAsync();
        }

        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await client.GetAsync("/api/mobile/builder/setup");

        Assert.Equal(System.Net.HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Four_digit_paid_pin_resolves_mode_from_account_and_searches_descriptions_before_paging()
    {
        await using var factory = new BuilderApiFactory();
        await factory.SeedBuilderAsync();
        using var client = factory.CreateClient();
        var login = await client.PostAsJsonAsync("/api/auth/pin-login", new PinLoginRequestDto("1111"));
        login.EnsureSuccessStatusCode();
        using var json = System.Text.Json.JsonDocument.Parse(await login.Content.ReadAsStringAsync());
        Assert.Equal("SelfServiceBuilder", json.RootElement.GetProperty("experienceMode").GetString());
        Assert.Equal(System.Text.Json.JsonValueKind.Null, json.RootElement.GetProperty("tripId").ValueKind);
        client.DefaultRequestHeaders.Authorization = new("Bearer", json.RootElement.GetProperty("token").GetString());
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TravelCompanionDbContext>();
        var destination = await db.Destinations.SingleAsync();
        for (var index = 0; index < 12; index++) db.Recommendations.Add(new Recommendation
        {
            Id = Guid.NewGuid(), DestinationId = destination.Id, Title = $"Cafe {index:D2}", Category = "Food",
            Neighborhood = "Tokyo", Description = "Cafe y pasteleria", DescriptionEn = "Coffee and donuts",
            Latitude = 35, Longitude = 139, PriceLevel = "low", ProviderPlaceId = $"ChIJ{index}"
        });
        db.Recommendations.Add(new Recommendation
        {
            Id = Guid.NewGuid(), DestinationId = destination.Id, Title = "Hidden donuts", Category = "Food",
            Neighborhood = "Tokyo", Description = "Donuts reserved for administrators",
            Latitude = 35, Longitude = 139, PriceLevel = "high", AccessLevel = ContentAccessLevel.AdminOnly
        });
        await db.SaveChangesAsync();
        client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("en-US");
        var response = await client.PostAsJsonAsync("/api/mobile/places/search-page?page=2", new PlaceSearchRequest("DONUTS"));
        response.EnsureSuccessStatusCode();
        var page = await response.Content.ReadFromJsonAsync<PagedResultDto<RecommendationDto>>(new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web)
        { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } });
        Assert.NotNull(page);
        Assert.Equal(12, page.TotalItems);
        Assert.Equal(2, page.Items.Count);
        Assert.All(page.Items, item => Assert.Equal("Coffee and donuts", item.Description));
    }

    [Fact]
    public async Task Place_search_uses_google_only_when_the_authorized_catalog_has_no_matches()
    {
        await using var factory = new BuilderApiFactory();
        var token = await factory.SeedBuilderAsync();
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TravelCompanionDbContext>();
            var destination = await db.Destinations.SingleAsync();
            db.Recommendations.Add(new Recommendation
            {
                Id = Guid.NewGuid(), DestinationId = destination.Id, Title = "Kyoto Design Museum",
                Category = "Museum", Neighborhood = "Kyoto", Description = "Japanese design collection",
                Latitude = 35, Longitude = 135, PriceLevel = "low"
            });
            await db.SaveChangesAsync();
        }

        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var catalogResponse = await client.PostAsJsonAsync(
            "/api/mobile/places/search",
            new PlaceSearchRequest("Design Museum", IncludeGoogle: true));
        catalogResponse.EnsureSuccessStatusCode();
        Assert.Equal(0, factory.GooglePlaces.SearchCount);

        var googleResponse = await client.PostAsJsonAsync(
            "/api/mobile/places/search",
            new PlaceSearchRequest("Unlisted ramen", IncludeGoogle: true));
        googleResponse.EnsureSuccessStatusCode();
        var results = await googleResponse.Content.ReadFromJsonAsync<List<RecommendationDto>>(
            new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web)
            {
                Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
            });
        Assert.Equal(1, factory.GooglePlaces.SearchCount);
        Assert.Single(results!);
        Assert.Equal("Google fallback", results![0].Title);
    }

    [Fact]
    public async Task Put_setup_accepts_valid_record_validation_metadata_and_creates_trip()
    {
        await using var factory = new BuilderApiFactory();
        var token = await factory.SeedBuilderAsync();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var arrival = new DateOnly(2026, 9, 1);

        var response = await client.PutAsJsonAsync(
            "/api/mobile/builder/setup",
            new SaveBuilderTripSetupRequest(
                arrival,
                arrival.AddDays(6),
                "Asia/Tokyo",
                0,
                [new BuilderTripSetupSegmentDto("Tokyo", arrival, arrival.AddDays(6))]));

        response.EnsureSuccessStatusCode();
        var setup = await response.Content.ReadFromJsonAsync<BuilderTripSetupDto>();
        Assert.NotNull(setup?.TripId);
        Assert.Equal(arrival, setup.ArrivalDate);
        Assert.Equal(arrival.AddDays(6), setup.DepartureDate);

        using var deleteRequest = new HttpRequestMessage(HttpMethod.Delete, "/api/mobile/builder/setup")
        {
            Content = JsonContent.Create(new DeleteBuilderTripSetupRequest(setup.TripId.Value, setup.Revision))
        };
        var deleteResponse = await client.SendAsync(deleteRequest);
        deleteResponse.EnsureSuccessStatusCode();
        var deleted = await deleteResponse.Content.ReadFromJsonAsync<BuilderTripSetupDto>();
        Assert.False(deleted?.IsConfigured);
        Assert.Null(deleted?.TripId);

        client.DefaultRequestHeaders.Authorization = null;
        var loginAgain = await client.PostAsJsonAsync("/api/auth/pin-login", new PinLoginRequestDto("1111"));
        loginAgain.EnsureSuccessStatusCode();
        using var loginJson = System.Text.Json.JsonDocument.Parse(await loginAgain.Content.ReadAsStringAsync());
        Assert.Equal(System.Text.Json.JsonValueKind.Null, loginJson.RootElement.GetProperty("tripId").ValueKind);
        Assert.True(loginJson.RootElement.GetProperty("capabilities").GetProperty("requiresTripSetup").GetBoolean());
    }

    private sealed class BuilderApiFactory : WebApplicationFactory<Program>
    {
        private readonly string databaseName = $"builder-endpoint-{Guid.NewGuid():N}";
        public FakeGooglePlacesService GooglePlaces { get; } = new();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<DbContextOptions<TravelCompanionDbContext>>();
                services.RemoveAll<IDbContextOptionsConfiguration<TravelCompanionDbContext>>();
                services.AddDbContext<TravelCompanionDbContext>(options => options.UseInMemoryDatabase(databaseName));
                services.RemoveAll<IGooglePlacesService>();
                services.AddSingleton<IGooglePlacesService>(GooglePlaces);
            });
        }

        public async Task<string> SeedBuilderAsync()
        {
            using var scope = Services.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<TravelCompanionDbContext>();
            var destination = new Destination
            {
                Id = Guid.NewGuid(),
                Name = "Japon",
                Slug = $"japon-{Guid.NewGuid():N}",
                Country = "Japan",
                TimeZoneId = "Asia/Tokyo",
                HeroImageUrl = string.Empty,
                ShortDescription = string.Empty
            };
            var user = new AppUser
            {
                Id = Guid.NewGuid(),
                Email = "builder-endpoint@example.test",
                DisplayName = "Builder Endpoint",
                PasswordHash = string.Empty
            };
            dbContext.AddRange(destination, user, new BuilderAccessGrant
            {
                Id = Guid.NewGuid(),
                AppUserId = user.Id,
                AppUser = user,
                DestinationId = destination.Id,
                Destination = destination,
                PinHash = new Microsoft.AspNetCore.Identity.PasswordHasher<BuilderAccessGrant>().HashPassword(null!, "1111")
            });
            await dbContext.SaveChangesAsync();

            var sessionService = scope.ServiceProvider.GetRequiredService<UserSessionService>();
            var (_, token) = await sessionService.CreateSessionAsync(user, accessMode: SessionAccessMode.Builder);
            return token;
        }
    }

    private sealed class FakeGooglePlacesService : IGooglePlacesService
    {
        public int SearchCount { get; private set; }

        public Task<IReadOnlyList<RecommendationDto>> SearchAsync(
            Guid destinationId,
            PlaceSearchRequest request,
            CancellationToken cancellationToken)
        {
            SearchCount++;
            IReadOnlyList<RecommendationDto> result =
            [
                new RecommendationDto(
                    Guid.Empty, destinationId, "Google fallback", "Restaurant", "Tokyo, Japan",
                    "External result", [], "unknown", 35.6m, 139.7m, 60, null, null,
                    ContentAccessLevel.Free, [], null)
                {
                    Provider = "Google",
                    ProviderPlaceId = "google-fallback"
                }
            ];
            return Task.FromResult(result);
        }
    }
}

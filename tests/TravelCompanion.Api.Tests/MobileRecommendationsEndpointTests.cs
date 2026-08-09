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

public sealed class MobileRecommendationsEndpointTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    [Fact]
    public async Task Mobile_bootstrap_returns_summaries_and_detail_endpoint_returns_full_authorized_content()
    {
        await using var factory = new TravelCompanionApiFactory();
        var seed = await factory.SeedUserWithPackagedRecommendationAsync();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", seed.Token);

        var bootstrap = await client.GetFromJsonAsync<MobileBootstrapDto>(
            "/api/mobile/bootstrap?destinationSlug=japon",
            JsonOptions);

        Assert.NotNull(bootstrap);
        var summary = Assert.Single(bootstrap.Recommendations);
        Assert.Equal(seed.RecommendationId, summary.Id);
        Assert.EndsWith("...", summary.Description);
        Assert.True(summary.Description.Length < seed.FullDescription.Length);

        var detail = await client.GetFromJsonAsync<RecommendationDto>(
            $"/api/mobile/recommendations/{seed.RecommendationId}",
            JsonOptions);

        Assert.NotNull(detail);
        Assert.Equal(seed.FullDescription, detail.Description);
    }

    [Fact]
    public async Task Mobile_recommendation_detail_hides_locked_content()
    {
        await using var factory = new TravelCompanionApiFactory();
        var seed = await factory.SeedUserWithPackagedRecommendationAsync(includePackageAccess: false);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", seed.Token);

        var response = await client.GetAsync($"/api/mobile/recommendations/{seed.RecommendationId}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private sealed class TravelCompanionApiFactory : WebApplicationFactory<Program>
    {
        private readonly string databaseName = $"mobile-recommendations-tests-{Guid.NewGuid():N}";

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

        public async Task<SeedResult> SeedUserWithPackagedRecommendationAsync(bool includePackageAccess = true)
        {
            using var scope = Services.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<TravelCompanionDbContext>();
            var sessionService = scope.ServiceProvider.GetRequiredService<UserSessionService>();

            var destinationId = Guid.NewGuid();
            var packageId = Guid.NewGuid();
            var recommendationId = Guid.NewGuid();
            var fullDescription = "Full paid editorial recommendation with enough detail to be useful only when the traveler opens the detail screen after entitlement checks.";

            var user = new AppUser
            {
                Id = Guid.NewGuid(),
                Email = "mobile-detail@example.test",
                DisplayName = "Mobile Detail Traveler",
                PasswordHash = string.Empty,
                MustChangePassword = false
            };

            if (includePackageAccess)
            {
                user.Entitlements.Add(new UserEntitlement
                {
                    Id = Guid.NewGuid(),
                    AccessLevel = ContentAccessLevel.Paid,
                    TravelPackageId = packageId,
                    GrantedAt = DateTimeOffset.UtcNow,
                    Source = "test"
                });
            }

            var package = new TravelPackage
            {
                Id = packageId,
                DestinationId = destinationId,
                Name = "Premium",
                Slug = "premium",
                Description = "Premium content",
                Price = 19,
                Currency = "USD"
            };

            var recommendation = new Recommendation
            {
                Id = recommendationId,
                DestinationId = destinationId,
                Title = "Premium hidden route",
                Category = "Culture",
                Neighborhood = "Tokyo",
                Description = fullDescription,
                Tags = ["culture", "hidden gem"],
                PriceLevel = "medium",
                Latitude = 35.0m,
                Longitude = 139.0m,
                SuggestedDurationMinutes = 90,
                Rating = 4.5,
                OpeningHours = "09:00-18:00",
                AccessLevel = ContentAccessLevel.Paid,
                Packages = [package]
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
            dbContext.TravelPackages.Add(package);
            dbContext.Recommendations.Add(recommendation);
            await dbContext.SaveChangesAsync();

            var (session, token) = await sessionService.CreateSessionAsync(user);
            session.LastSeenAt = DateTimeOffset.UtcNow;
            await dbContext.SaveChangesAsync();
            return new SeedResult(token, recommendationId, fullDescription);
        }
    }

    private sealed record SeedResult(
        string Token,
        Guid RecommendationId,
        string FullDescription);

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

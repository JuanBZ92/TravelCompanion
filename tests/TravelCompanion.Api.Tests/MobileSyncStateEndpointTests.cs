using System.Net;
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

public sealed class MobileSyncStateEndpointTests
{
    [Fact]
    public async Task Sync_state_returns_small_independent_versions_and_honors_etag()
    {
        await using var factory = new SyncApiFactory();
        var seed = await factory.SeedAsync();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", seed.Token);

        using var first = await client.GetAsync("/api/mobile/sync-state");
        first.EnsureSuccessStatusCode();
        Assert.NotNull(first.Headers.ETag);
        var state = await first.Content.ReadFromJsonAsync<MobileSyncStateDto>();
        Assert.NotNull(state);
        Assert.Equal(seed.TripId, state.TripId);
        Assert.Equal(7, state.ItineraryVersion);
        Assert.Equal(4, state.CatalogVersion);
        Assert.Equal(3, state.DocumentsVersion);
        Assert.False(state.Capabilities.CanEditItinerary);
        Assert.True(state.Capabilities.CanCalculateRoutes);

        using var secondRequest = new HttpRequestMessage(HttpMethod.Get, "/api/mobile/sync-state");
        secondRequest.Headers.IfNoneMatch.Add(first.Headers.ETag);
        using var second = await client.SendAsync(secondRequest);
        Assert.Equal(HttpStatusCode.NotModified, second.StatusCode);
        Assert.Equal(0, second.Content.Headers.ContentLength ?? 0);
    }

    [Fact]
    public async Task Sync_state_requires_a_valid_session()
    {
        await using var factory = new SyncApiFactory();
        using var client = factory.CreateClient();
        var response = await client.GetAsync("/api/mobile/sync-state");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private sealed class SyncApiFactory : WebApplicationFactory<Program>
    {
        private readonly string _databaseName = $"mobile-sync-{Guid.NewGuid():N}";

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<DbContextOptions<TravelCompanionDbContext>>();
                services.RemoveAll<IDbContextOptionsConfiguration<TravelCompanionDbContext>>();
                services.AddDbContext<TravelCompanionDbContext>(options => options.UseInMemoryDatabase(_databaseName));
            });
        }

        public async Task<SeedResult> SeedAsync()
        {
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<TravelCompanionDbContext>();
            var sessions = scope.ServiceProvider.GetRequiredService<UserSessionService>();
            var destination = new Destination
            {
                Id = Guid.NewGuid(), Name = "Japan", Slug = "japan", Country = "Japan",
                HeroImageUrl = string.Empty, ShortDescription = "Test"
            };
            var user = new AppUser
            {
                Id = Guid.NewGuid(), Email = "sync@example.test", DisplayName = "Sync", PasswordHash = string.Empty
            };
            var trip = new Trip
            {
                Id = Guid.NewGuid(), AppUserId = user.Id, AppUser = user, DestinationId = destination.Id,
                Destination = destination, TravelerName = "Sync", StartsOn = new(2026, 9, 1), EndsOn = new(2026, 9, 7),
                PlanRevision = 7, PublicationStatus = TripPublicationStatus.Published
            };
            db.AddRange(destination, user, trip);
            db.MobileDataVersions.AddRange(
                new MobileDataVersion { Scope = MobileDataVersionScopes.Catalog(destination.Id), Version = 4 },
                new MobileDataVersion { Scope = MobileDataVersionScopes.Documents(trip.Id), Version = 3 },
                new MobileDataVersion { Scope = MobileDataVersionScopes.Today(user.Id), Version = 2 });
            await db.SaveChangesAsync();
            var (_, token) = await sessions.CreateSessionAsync(user, tripId: trip.Id, accessMode: SessionAccessMode.Trip);
            return new SeedResult(token, trip.Id);
        }
    }

    private sealed record SeedResult(string Token, Guid TripId);
}

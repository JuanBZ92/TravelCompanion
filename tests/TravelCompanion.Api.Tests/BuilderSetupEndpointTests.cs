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
    }

    private sealed class BuilderApiFactory : WebApplicationFactory<Program>
    {
        private readonly string databaseName = $"builder-endpoint-{Guid.NewGuid():N}";

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<DbContextOptions<TravelCompanionDbContext>>();
                services.RemoveAll<IDbContextOptionsConfiguration<TravelCompanionDbContext>>();
                services.AddDbContext<TravelCompanionDbContext>(options => options.UseInMemoryDatabase(databaseName));
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
                PinHash = "test"
            });
            await dbContext.SaveChangesAsync();

            var sessionService = scope.ServiceProvider.GetRequiredService<UserSessionService>();
            var (_, token) = await sessionService.CreateSessionAsync(user, accessMode: SessionAccessMode.Builder);
            return token;
        }
    }
}

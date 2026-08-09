using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
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

public sealed class PinLoginEndpointTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    [Fact]
    public async Task Pin_login_returns_session_for_trip_owner()
    {
        await using var factory = new TravelCompanionApiFactory();
        var seed = await factory.SeedTripsWithPinAsync();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/auth/pin-login",
            new PinLoginRequestDto("1908"));

        response.EnsureSuccessStatusCode();
        var session = await response.Content.ReadFromJsonAsync<AuthSessionDto>(JsonOptions);

        Assert.NotNull(session);
        Assert.Equal(seed.UserId, session.UserId);
        Assert.Equal(seed.PinTripId, session.TripId);
        Assert.False(session.MustChangePassword);
        Assert.Equal("Japan", session.DestinationName);
    }

    [Fact]
    public async Task Pin_session_limits_my_schedule_to_unlocked_trip()
    {
        await using var factory = new TravelCompanionApiFactory();
        var seed = await factory.SeedTripsWithPinAsync();
        using var client = factory.CreateClient();

        var login = await client.PostAsJsonAsync(
            "/api/auth/pin-login",
            new PinLoginRequestDto("1908"));
        login.EnsureSuccessStatusCode();
        var session = await login.Content.ReadFromJsonAsync<AuthSessionDto>(JsonOptions);
        Assert.NotNull(session);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session.Token);
        var schedule = await client.GetFromJsonAsync<TripScheduleDto>("/api/me/schedule", JsonOptions);

        Assert.NotNull(schedule);
        Assert.Equal(seed.PinTripId, schedule.TripId);
        Assert.Equal("PIN Trip Traveler", schedule.TravelerName);
        Assert.Single(schedule.Items);
    }

    [Fact]
    public async Task Pin_login_rejects_unknown_pin()
    {
        await using var factory = new TravelCompanionApiFactory();
        await factory.SeedTripsWithPinAsync();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/auth/pin-login",
            new PinLoginRequestDto("9999"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private sealed class TravelCompanionApiFactory : WebApplicationFactory<Program>
    {
        private readonly string databaseName = $"pin-login-tests-{Guid.NewGuid():N}";

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

        public async Task<SeedResult> SeedTripsWithPinAsync()
        {
            using var scope = Services.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<TravelCompanionDbContext>();
            var pinHasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher<Trip>>();

            var user = new AppUser
            {
                Id = Guid.NewGuid(),
                Email = "pin@example.test",
                DisplayName = "PIN Traveler",
                PasswordHash = string.Empty,
                MustChangePassword = true
            };
            var destination = new Destination
            {
                Id = Guid.NewGuid(),
                Name = "Japan",
                Slug = "japan",
                Country = "Japan",
                HeroImageUrl = "https://example.test/japan.jpg",
                ShortDescription = "PIN destination"
            };
            var defaultTrip = new Trip
            {
                Id = Guid.NewGuid(),
                AppUserId = user.Id,
                DestinationId = destination.Id,
                TravelerName = "Default Traveler",
                StartsOn = new DateOnly(2026, 1, 10),
                EndsOn = new DateOnly(2026, 1, 14)
            };
            var pinTrip = new Trip
            {
                Id = Guid.NewGuid(),
                AppUserId = user.Id,
                DestinationId = destination.Id,
                TravelerName = "PIN Trip Traveler",
                StartsOn = new DateOnly(2026, 12, 10),
                EndsOn = new DateOnly(2026, 12, 20),
                Reservations =
                [
                    new Reservation
                    {
                        Id = Guid.NewGuid(),
                        Type = ReservationType.Event,
                        Date = new DateOnly(2026, 12, 11),
                        StartsAt = new TimeOnly(10, 0),
                        Title = "Unlocked plan",
                        City = "Tokyo",
                        LocationName = "Tokyo Station",
                        Address = "Tokyo",
                        ConfirmationCode = "PIN-PLAN",
                        Notes = string.Empty
                    }
                ]
            };
            pinTrip.AccessPinHash = pinHasher.HashPassword(pinTrip, "1908");
            pinTrip.AccessPinUpdatedAt = DateTimeOffset.UtcNow;

            dbContext.AppUsers.Add(user);
            dbContext.Destinations.Add(destination);
            dbContext.Trips.AddRange(defaultTrip, pinTrip);
            await dbContext.SaveChangesAsync();
            return new SeedResult(user.Id, pinTrip.Id);
        }
    }

    private sealed record SeedResult(Guid UserId, Guid PinTripId);

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

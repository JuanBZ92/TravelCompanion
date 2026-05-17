using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
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
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Api.Tests;

public sealed class NotificationEndpointTests
{
    [Fact]
    public async Task RegisterDevice_requires_bearer_session()
    {
        await using var factory = new TravelCompanionApiFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/notifications/devices",
            new RegisterNotificationDeviceRequest(
                "install-1",
                "fcmv1",
                "token-1",
                "es-ES",
                "Asia/Tokyo"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task RegisterDevice_upserts_current_users_device()
    {
        await using var factory = new TravelCompanionApiFactory();
        var token = await factory.SeedUserAsync();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var firstResponse = await client.PostAsJsonAsync(
            "/api/notifications/devices",
            new RegisterNotificationDeviceRequest(
                "install-1",
                "android",
                "token-1",
                "es-ES",
                "Asia/Tokyo"));
        firstResponse.EnsureSuccessStatusCode();

        var secondResponse = await client.PostAsJsonAsync(
            "/api/notifications/devices",
            new RegisterNotificationDeviceRequest(
                "install-1",
                "fcmv1",
                "token-2",
                "en-US",
                "America/New_York",
                ScheduleRemindersEnabled: false));
        secondResponse.EnsureSuccessStatusCode();
        var body = await secondResponse.Content.ReadFromJsonAsync<NotificationDeviceRegistrationDto>();

        Assert.NotNull(body);
        Assert.Equal("install-1", body.InstallationId);
        Assert.Equal("fcmv1", body.Platform);
        Assert.False(body.ScheduleRemindersEnabled);

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<TravelCompanionDbContext>();
        var devices = await dbContext.NotificationDeviceRegistrations.ToListAsync();
        var device = Assert.Single(devices);
        Assert.Equal("token-2", device.PushToken);
        Assert.Equal("en-US", device.Locale);
        Assert.Null(device.DisabledAtUtc);
    }

    [Fact]
    public async Task DisableDevice_marks_current_users_device_disabled()
    {
        await using var factory = new TravelCompanionApiFactory();
        var token = await factory.SeedUserAsync();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var registerResponse = await client.PostAsJsonAsync(
            "/api/notifications/devices",
            new RegisterNotificationDeviceRequest(
                "install-1",
                "apns",
                "token-1",
                null,
                null));
        registerResponse.EnsureSuccessStatusCode();

        var disableResponse = await client.DeleteAsync("/api/notifications/devices/install-1");

        Assert.Equal(HttpStatusCode.NoContent, disableResponse.StatusCode);
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<TravelCompanionDbContext>();
        var device = Assert.Single(await dbContext.NotificationDeviceRegistrations.ToListAsync());
        Assert.NotNull(device.DisabledAtUtc);
    }

    private sealed class TravelCompanionApiFactory : WebApplicationFactory<Program>
    {
        private readonly string databaseName = $"travel-companion-notification-tests-{Guid.NewGuid():N}";

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

        public async Task<string> SeedUserAsync()
        {
            using var scope = Services.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<TravelCompanionDbContext>();
            var passwordHasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher<AppUser>>();
            var sessionService = scope.ServiceProvider.GetRequiredService<UserSessionService>();

            var user = new AppUser
            {
                Id = Guid.NewGuid(),
                Email = "notifications@example.test",
                DisplayName = "Notification Traveler",
                PasswordHash = string.Empty
            };
            user.PasswordHash = passwordHasher.HashPassword(user, "Password123!");
            dbContext.AppUsers.Add(user);
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

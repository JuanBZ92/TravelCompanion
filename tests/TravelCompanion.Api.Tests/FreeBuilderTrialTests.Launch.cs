using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using TravelCompanion.Api.Data;
using TravelCompanion.Api.Models;
using TravelCompanion.Api.Services;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Api.Tests;

public sealed partial class FreeBuilderTrialTests
{
    [PostgresItineraryIdempotencyTests.PostgresFact]
    public async Task Postgres_readiness_requires_applied_migrations()
    {
        var schema = "ready_" + Guid.NewGuid().ToString("N");
        var baseConnection = Environment.GetEnvironmentVariable("TRAVELCOMPANION_TEST_POSTGRES")!;
        await using var admin = new NpgsqlConnection(baseConnection);
        await admin.OpenAsync();
        await using (var create = new NpgsqlCommand($"CREATE SCHEMA \"{schema}\"", admin)) await create.ExecuteNonQueryAsync();
        try
        {
            var connection = new NpgsqlConnectionStringBuilder(baseConnection) { SearchPath = schema }.ConnectionString;
            await using var factory = new TrialApiFactory(connection);
            using var client = factory.CreateClient();
            using var before = await client.GetAsync("/health/ready");
            Assert.Equal(HttpStatusCode.ServiceUnavailable, before.StatusCode);
            Assert.Contains("migration-required", await before.Content.ReadAsStringAsync());
            using (var scope = factory.Services.CreateScope())
                await scope.ServiceProvider.GetRequiredService<TravelCompanionDbContext>().Database.MigrateAsync();
            using var after = await client.GetAsync("/health/ready");
            Assert.Equal(HttpStatusCode.OK, after.StatusCode);
        }
        finally
        {
            await using var drop = new NpgsqlCommand($"DROP SCHEMA \"{schema}\" CASCADE", admin);
            await drop.ExecuteNonQueryAsync();
        }
    }

    [Fact]
    public async Task Failed_email_does_not_leave_a_challenge_claiming_delivery()
    {
        var sender = new FailingEmailSender();
        await using var factory = new TrialApiFactory(emailSender: sender);
        await factory.SeedAsync();
        using var client = factory.CreateClient();
        await PrepareTripAsync(client, "email-failure");
        for (var attempt = 0; attempt < 2; attempt++)
        {
            using var response = await client.PostAsJsonAsync("/api/mobile/account/email/code",
                new RequestEmailCodeDto("failure@example.test", "en"), JsonOptions);
            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        }
        Assert.Equal(2, sender.Calls);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TravelCompanionDbContext>();
        Assert.False(await db.EmailVerificationChallenges.AnyAsync(x => x.ConsumedAtUtc == null));
    }

    private sealed class FailingEmailSender : ITransactionalEmailSender
    {
        public int Calls;
        public Task SendVerificationCodeAsync(string email, string code, string locale, CancellationToken ct)
        {
            Calls++;
            throw new HttpRequestException("Provider unavailable.");
        }
    }
    private static async Task<Guid> PrepareTripAsync(HttpClient client, string installation)
    {
        var login = await LoginAsync(client, installation);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.Token);
        var arrival = new DateOnly(2026, 11, 1);
        using var response = await client.PutAsJsonAsync("/api/mobile/builder/setup", new SaveBuilderTripSetupRequest(
            arrival, arrival.AddDays(4), "Asia/Tokyo", 0,
            [new BuilderTripSetupSegmentDto("Tokyo", arrival, arrival.AddDays(4))]), JsonOptions);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<BuilderTripSetupDto>(JsonOptions))!.TripId!.Value;
    }

    [Fact]
    public async Task Preparation_is_owned_explicit_idempotent_and_conflict_aware()
    {
        await using var factory = new TrialApiFactory();
        await factory.SeedAsync();
        using var client = factory.CreateClient();
        var trip = await PrepareTripAsync(client, "preparation-owner");
        var path = $"/api/mobile/trips/{trip}/preparation";
        var items = await client.GetFromJsonAsync<List<TripPreparationItemDto>>(path, JsonOptions);
        Assert.Equal(4, items!.Count);
        Assert.All(items, x => Assert.False(x.Completed));
        using var saved = await client.PutAsJsonAsync(path + "/reservations", new SaveTripPreparationItemRequest(true, 0));
        saved.EnsureSuccessStatusCode();
        using var replay = await client.PutAsJsonAsync(path + "/reservations", new SaveTripPreparationItemRequest(true, 0));
        Assert.Equal(1, (await replay.Content.ReadFromJsonAsync<TripPreparationItemDto>())!.Revision);
        using var stale = await client.PutAsJsonAsync(path + "/reservations", new SaveTripPreparationItemRequest(false, 0));
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        using var invalid = await client.PutAsJsonAsync(path + "/unrecognized", new SaveTripPreparationItemRequest(true, 0));
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        using var other = factory.CreateClient();
        await PrepareTripAsync(other, "preparation-other");
        using var denied = await other.GetAsync(path);
        Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
        using var deniedSave = await other.PutAsJsonAsync(path + "/reservations", new SaveTripPreparationItemRequest(false, 1));
        Assert.Equal(HttpStatusCode.Unauthorized, deniedSave.StatusCode);
        using var deleted = await client.DeleteAsync("/api/mobile/account");
        deleted.EnsureSuccessStatusCode();
        using var scope = factory.Services.CreateScope();
        Assert.False(await scope.ServiceProvider.GetRequiredService<TravelCompanionDbContext>()
            .TripPreparationItems.AnyAsync(x => x.TripId == trip));
    }

    [Fact]
    public async Task Redeem_has_a_pin_attempt_limit()
    {
        await using var factory = new TrialApiFactory();
        await factory.SeedAsync();
        using var client = factory.CreateClient();
        await PrepareTripAsync(client, "rate-limit-owner");
        HttpStatusCode status = default;
        for (var i = 0; i < 9; i++)
        {
            using var response = await client.PostAsJsonAsync("/api/mobile/pass/redeem", new RedeemTravelPassRequest("999999"), JsonOptions);
            status = response.StatusCode;
        }
        Assert.Equal(HttpStatusCode.TooManyRequests, status);
    }

    [PostgresItineraryIdempotencyTests.PostgresFact]
    public async Task Postgres_same_paid_pin_can_only_be_claimed_by_one_trip()
    {
        var schema = "launch_" + Guid.NewGuid().ToString("N");
        var baseConnection = Environment.GetEnvironmentVariable("TRAVELCOMPANION_TEST_POSTGRES")!;
        await using var admin = new NpgsqlConnection(baseConnection);
        await admin.OpenAsync();
        await using (var create = new NpgsqlCommand($"CREATE SCHEMA \"{schema}\"", admin)) await create.ExecuteNonQueryAsync();
        try
        {
            var connection = new NpgsqlConnectionStringBuilder(baseConnection) { SearchPath = schema }.ConnectionString;
            using var hasher = new ConcurrentPinHasher();
            await using var factory = new TrialApiFactory(connection, hasher);
            using (var scope = factory.Services.CreateScope())
                await scope.ServiceProvider.GetRequiredService<TravelCompanionDbContext>().Database.MigrateAsync();
            await factory.SeedAsync();
            using var first = factory.CreateClient();
            using var second = factory.CreateClient();
            await PrepareTripAsync(first, "concurrent-first");
            await PrepareTripAsync(second, "concurrent-second");
            var results = await Task.WhenAll(first.PostAsJsonAsync("/api/mobile/pass/redeem", new RedeemTravelPassRequest("4321"), JsonOptions),
                second.PostAsJsonAsync("/api/mobile/pass/redeem", new RedeemTravelPassRequest("4321"), JsonOptions));
            Assert.Single(results, x => x.StatusCode == HttpStatusCode.OK);
            Assert.Single(results, x => x.StatusCode == HttpStatusCode.Conflict);
            using var finalScope = factory.Services.CreateScope();
            var db = finalScope.ServiceProvider.GetRequiredService<TravelCompanionDbContext>();
            Assert.Equal(1, await db.BuilderAccessGrants.CountAsync(x => !x.IsTrial && x.TripId != null));
            Assert.Equal(1, await db.BuilderAccessGrants.CountAsync(x => x.IsTrial && x.TripId != null));
            foreach (var result in results) result.Dispose();
        }
        finally
        {
            await using var drop = new NpgsqlCommand($"DROP SCHEMA \"{schema}\" CASCADE", admin);
            await drop.ExecuteNonQueryAsync();
        }
    }

    private sealed class ConcurrentPinHasher : IPasswordHasher<BuilderAccessGrant>, IDisposable
    {
        private readonly PasswordHasher<BuilderAccessGrant> inner = new();
        private readonly Barrier barrier = new(2);
        private int calls;
        public string HashPassword(BuilderAccessGrant user, string password) => inner.HashPassword(user, password);
        public PasswordVerificationResult VerifyHashedPassword(BuilderAccessGrant user, string hash, string password)
        {
            if (Interlocked.Increment(ref calls) <= 2 && !barrier.SignalAndWait(TimeSpan.FromSeconds(20)))
                throw new TimeoutException("Both redemption requests must reach the same unclaimed pass.");
            return inner.VerifyHashedPassword(user, hash, password);
        }
        public void Dispose() => barrier.Dispose();
    }
}

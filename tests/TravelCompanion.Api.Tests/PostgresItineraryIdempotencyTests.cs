using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Npgsql;
using System.Security.Cryptography;
using System.Text;
using TravelCompanion.Api.Data;
using TravelCompanion.Api.Models;
using TravelCompanion.Api.Options;
using TravelCompanion.Api.Services;
using TravelCompanion.Shared;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Api.Tests;

public sealed class PostgresItineraryIdempotencyTests
{
    [PostgresFact]
    public async Task Concurrent_otp_verification_consumes_the_code_once()
    {
        var baseConnectionString = Environment.GetEnvironmentVariable("TRAVELCOMPANION_TEST_POSTGRES")!;
        var schema = $"tc_otp_{Guid.NewGuid():N}";
        var builder = new NpgsqlConnectionStringBuilder(baseConnectionString) { SearchPath = schema };
        await using var administrationConnection = new NpgsqlConnection(baseConnectionString);
        await administrationConnection.OpenAsync();
        await using (var createSchema = administrationConnection.CreateCommand())
        {
            createSchema.CommandText = $"CREATE SCHEMA \"{schema}\"";
            await createSchema.ExecuteNonQueryAsync();
        }

        try
        {
            var dbOptions = new DbContextOptionsBuilder<TravelCompanionDbContext>()
                .UseNpgsql(builder.ConnectionString, provider => provider.EnableRetryOnFailure()).Options;
            const string email = "otp-concurrency@example.test";
            const string code = "123456";
            const string secret = "postgres-otp-secret";
            await using (var setup = new TravelCompanionDbContext(dbOptions))
            {
                await setup.Database.MigrateAsync();
                setup.AppUsers.Add(new AppUser
                {
                    Id = Guid.NewGuid(), Email = email, DisplayName = "OTP test", EmailVerified = false
                });
                using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
                setup.EmailVerificationChallenges.Add(new EmailVerificationChallenge
                {
                    Id = Guid.NewGuid(), Email = email,
                    CodeHash = Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes($"{email}:{code}"))),
                    RequestIpHash = "postgres", CreatedAtUtc = DateTimeOffset.UtcNow,
                    ExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(10),
                    ResendAvailableAtUtc = DateTimeOffset.UtcNow
                });
                await setup.SaveChangesAsync();
            }

            async Task<bool> VerifyAsync()
            {
                try
                {
                    await using var context = new TravelCompanionDbContext(dbOptions);
                    var service = new EmailAccountService(context, new UserSessionService(context),
                        new NoOpEmailSender(), Microsoft.Extensions.Options.Options.Create(new EmailVerificationOptions { HashSecret = secret }));
                    await service.VerifyCodeAsync(new DefaultHttpContext(), new(email, code), default);
                    return true;
                }
                catch (Exception exception) when (exception is InvalidOperationException or DbUpdateException or PostgresException)
                {
                    return false;
                }
            }

            var results = await Task.WhenAll(VerifyAsync(), VerifyAsync());
            Assert.Single(results, item => item);
            await using var verification = new TravelCompanionDbContext(dbOptions);
            Assert.NotNull((await verification.EmailVerificationChallenges.SingleAsync()).ConsumedAtUtc);
            Assert.Equal(1, await verification.AppUserSessions.CountAsync(item => item.RevokedAt == null));
        }
        finally
        {
            await using var dropSchema = administrationConnection.CreateCommand();
            dropSchema.CommandText = $"DROP SCHEMA IF EXISTS \"{schema}\" CASCADE";
            await dropSchema.ExecuteNonQueryAsync();
        }
    }

    [PostgresFact]
    public async Task Concurrent_assistant_reservations_cannot_exceed_the_trial_limit()
    {
        var baseConnectionString = Environment.GetEnvironmentVariable("TRAVELCOMPANION_TEST_POSTGRES")!;
        var schema = $"tc_quota_{Guid.NewGuid():N}";
        var builder = new NpgsqlConnectionStringBuilder(baseConnectionString) { SearchPath = schema };
        await using var administrationConnection = new NpgsqlConnection(baseConnectionString);
        await administrationConnection.OpenAsync();
        await using (var createSchema = administrationConnection.CreateCommand())
        {
            createSchema.CommandText = $"CREATE SCHEMA \"{schema}\"";
            await createSchema.ExecuteNonQueryAsync();
        }

        try
        {
            var dbOptions = new DbContextOptionsBuilder<TravelCompanionDbContext>()
                .UseNpgsql(builder.ConnectionString, provider => provider.EnableRetryOnFailure()).Options;
            var userId = Guid.NewGuid();
            var tripId = Guid.NewGuid();
            var grantId = Guid.NewGuid();
            await using (var setup = new TravelCompanionDbContext(dbOptions))
            {
                await setup.Database.MigrateAsync();
                var destinationId = Guid.NewGuid();
                setup.Destinations.Add(new Destination
                {
                    Id = destinationId, Name = "Japan", Slug = $"quota-{schema}", Country = "Japan",
                    HeroImageUrl = string.Empty, ShortDescription = "Quota test"
                });
                setup.AppUsers.Add(new AppUser
                {
                    Id = userId, Email = $"quota-{schema}@example.test", DisplayName = "Quota test"
                });
                setup.Trips.Add(new Trip
                {
                    Id = tripId, AppUserId = userId, DestinationId = destinationId, TravelerName = "Quota test",
                    StartsOn = DateOnly.FromDateTime(DateTime.UtcNow), EndsOn = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1))
                });
                setup.BuilderAccessGrants.Add(new BuilderAccessGrant
                {
                    Id = grantId, AppUserId = userId, DestinationId = destinationId, TripId = tripId,
                    IsTrial = true, Status = BuilderAccessStatus.Active, CreatedAtUtc = DateTimeOffset.UtcNow,
                    TrialEditingExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(30),
                    TrialDraftExpiresAtUtc = DateTimeOffset.UtcNow.AddDays(30)
                });
                await setup.SaveChangesAsync();
            }

            async Task<bool> ReserveAsync(int index)
            {
                try
                {
                    await using var context = new TravelCompanionDbContext(dbOptions);
                    var service = new AssistantUsageService(context,
                        Microsoft.Extensions.Options.Options.Create(new FreePreviewOptions { AssistantRequestLimit = 3 }),
                        Microsoft.Extensions.Options.Options.Create(new StorePurchaseOptions { DailyAssistantLimit = 30 }));
                    await service.ReserveAsync(userId, tripId, $"parallel-{index}", default);
                    return true;
                }
                catch (Exception exception) when (exception is TrialUpgradeRequiredException or DbUpdateException or PostgresException)
                {
                    return false;
                }
            }

            var results = await Task.WhenAll(Enumerable.Range(0, 6).Select(ReserveAsync));
            Assert.Equal(3, results.Count(item => item));
            await using var verification = new TravelCompanionDbContext(dbOptions);
            Assert.Equal(3, await verification.AssistantUsageLeases.CountAsync(item =>
                item.BuilderAccessGrantId == grantId && item.CancelledAtUtc == null && item.CompletedAtUtc == null));
        }
        finally
        {
            await using var dropSchema = administrationConnection.CreateCommand();
            dropSchema.CommandText = $"DROP SCHEMA IF EXISTS \"{schema}\" CASCADE";
            await dropSchema.ExecuteNonQueryAsync();
        }
    }

    [PostgresFact]
    public async Task Catalog_mutations_increment_persistent_mobile_versions()
    {
        var baseConnectionString = Environment.GetEnvironmentVariable("TRAVELCOMPANION_TEST_POSTGRES")!;
        var schema = $"tc_versions_{Guid.NewGuid():N}";
        var builder = new NpgsqlConnectionStringBuilder(baseConnectionString) { SearchPath = schema };
        await using var administrationConnection = new NpgsqlConnection(baseConnectionString);
        await administrationConnection.OpenAsync();
        await using (var createSchema = administrationConnection.CreateCommand())
        {
            createSchema.CommandText = $"CREATE SCHEMA \"{schema}\"";
            await createSchema.ExecuteNonQueryAsync();
        }

        try
        {
            var options = new DbContextOptionsBuilder<TravelCompanionDbContext>()
                .UseNpgsql(builder.ConnectionString, provider => provider.EnableRetryOnFailure()).Options;
            await using var db = new TravelCompanionDbContext(options);
            await db.Database.MigrateAsync();
            var destination = new Destination
            {
                Id = Guid.NewGuid(), Name = "Japan", Slug = $"versions-{schema}", Country = "Japan",
                HeroImageUrl = string.Empty, ShortDescription = "Version trigger test"
            };
            db.Destinations.Add(destination);
            await db.SaveChangesAsync();
            var catalogScope = MobileDataVersionScopes.Catalog(destination.Id);
            var initialCatalog = await db.MobileDataVersions.SingleAsync(item => item.Scope == catalogScope);

            db.Recommendations.Add(new Recommendation
            {
                Id = Guid.NewGuid(), DestinationId = destination.Id, Title = "Versioned place",
                Category = "Culture", Neighborhood = "Tokyo", Description = "Test",
                Latitude = 35.6m, Longitude = 139.7m, SuggestedDurationMinutes = 60,
                AccessLevel = ContentAccessLevel.Free
            });
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();

            var updatedCatalog = await db.MobileDataVersions.SingleAsync(item => item.Scope == catalogScope);
            Assert.True(updatedCatalog.Version > initialCatalog.Version);
            Assert.True(await db.MobileDataVersions.AnyAsync(item => item.Scope == MobileDataVersionScopes.FreeCatalogGlobal));
        }
        finally
        {
            await using var dropSchema = administrationConnection.CreateCommand();
            dropSchema.CommandText = $"DROP SCHEMA IF EXISTS \"{schema}\" CASCADE";
            await dropSchema.ExecuteNonQueryAsync();
        }
    }

    [PostgresFact]
    public async Task Concurrent_retries_commit_one_reservation_and_one_analytics_signal()
    {
        var baseConnectionString = Environment.GetEnvironmentVariable("TRAVELCOMPANION_TEST_POSTGRES")!;

        var schema = $"tc_test_{Guid.NewGuid():N}";
        var builder = new NpgsqlConnectionStringBuilder(baseConnectionString)
        {
            SearchPath = schema
        };

        await using var administrationConnection = new NpgsqlConnection(baseConnectionString);
        await administrationConnection.OpenAsync();
        await using (var createSchema = administrationConnection.CreateCommand())
        {
            createSchema.CommandText = $"CREATE SCHEMA \"{schema}\"";
            await createSchema.ExecuteNonQueryAsync();
        }

        try
        {
            var options = new DbContextOptionsBuilder<TravelCompanionDbContext>()
                .UseNpgsql(builder.ConnectionString, provider => provider.EnableRetryOnFailure())
                .Options;

            Guid userId;
            Guid tripId;
            Guid recommendationId;
            await using (var setup = new TravelCompanionDbContext(options))
            {
                await setup.Database.MigrateAsync();
                userId = Guid.NewGuid();
                tripId = Guid.NewGuid();
                recommendationId = Guid.NewGuid();
                var destinationId = Guid.NewGuid();
                setup.Destinations.Add(new Destination
                {
                    Id = destinationId,
                    Name = "Japan",
                    Slug = $"japan-{schema}",
                    Country = "Japan",
                    HeroImageUrl = string.Empty,
                    ShortDescription = "PostgreSQL integration test"
                });
                setup.AppUsers.Add(new AppUser
                {
                    Id = userId,
                    Email = $"postgres-{schema}@example.test",
                    DisplayName = "PostgreSQL Test",
                    MustChangePassword = false
                });
                setup.Trips.Add(new Trip
                {
                    Id = tripId,
                    AppUserId = userId,
                    DestinationId = destinationId,
                    TravelerName = "PostgreSQL Test",
                    StartsOn = new DateOnly(2026, 10, 1),
                    EndsOn = new DateOnly(2026, 10, 3),
                    PublicationStatus = TripPublicationStatus.Published
                });
                setup.Recommendations.Add(new Recommendation
                {
                    Id = recommendationId,
                    DestinationId = destinationId,
                    Title = "Concurrent ramen",
                    Category = "Food",
                    Neighborhood = "Tokyo",
                    Description = "Idempotency test",
                    Latitude = 35.6762m,
                    Longitude = 139.6503m,
                    SuggestedDurationMinutes = 60,
                    AccessLevel = ContentAccessLevel.Free
                });
                await setup.SaveChangesAsync();
            }

            var mutationId = Guid.NewGuid();
            var request = new SaveItineraryItemRequest(
                recommendationId,
                new DateOnly(2026, 10, 2),
                new TimeOnly(12, 0),
                null,
                mutationId);

            async Task<SaveItineraryItemResponse> SaveAsync()
            {
                await using var context = new TravelCompanionDbContext(options);
                var user = await context.AppUsers
                    .AsNoTracking()
                    .Include(existing => existing.Entitlements)
                    .SingleAsync(existing => existing.Id == userId);
                return await new ItineraryService(context)
                    .SaveItineraryItemAsync(user, request, CancellationToken.None);
            }

            var results = await Task.WhenAll(SaveAsync(), SaveAsync());

            Assert.All(results, result => Assert.True(result.Saved, result.Message));
            Assert.Equal(results[0].Item?.Id, results[1].Item?.Id);
            await using var verification = new TravelCompanionDbContext(options);
            Assert.Equal(1, await verification.Reservations.CountAsync(existing =>
                existing.TripId == tripId && existing.ClientMutationId == mutationId));
            Assert.Equal(1, await verification.RecommendationInteractionSignals.CountAsync(existing =>
                existing.TripId == tripId && existing.RecommendationId == recommendationId));
        }
        finally
        {
            await using var dropSchema = administrationConnection.CreateCommand();
            dropSchema.CommandText = $"DROP SCHEMA IF EXISTS \"{schema}\" CASCADE";
            await dropSchema.ExecuteNonQueryAsync();
        }
    }

    public sealed class PostgresFactAttribute : FactAttribute
    {
        public PostgresFactAttribute()
        {
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("TRAVELCOMPANION_TEST_POSTGRES")))
            {
                Skip = "Set TRAVELCOMPANION_TEST_POSTGRES to run the real PostgreSQL concurrency test.";
            }
        }
    }

    private sealed class NoOpEmailSender : ITransactionalEmailSender
    {
        public Task SendVerificationCodeAsync(string email, string code, string locale, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }
}

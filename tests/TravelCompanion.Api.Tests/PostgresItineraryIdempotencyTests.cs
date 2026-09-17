using Microsoft.EntityFrameworkCore;
using Npgsql;
using TravelCompanion.Api.Data;
using TravelCompanion.Api.Models;
using TravelCompanion.Api.Services;
using TravelCompanion.Shared;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Api.Tests;

public sealed class PostgresItineraryIdempotencyTests
{
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
                .UseNpgsql(builder.ConnectionString)
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
}

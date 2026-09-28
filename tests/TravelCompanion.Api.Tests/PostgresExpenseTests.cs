using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using TravelCompanion.Api.Data;
using TravelCompanion.Api.Models;
using TravelCompanion.Api.Services;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Api.Tests;

public sealed class PostgresExpenseTests
{
    [PostgresItineraryIdempotencyTests.PostgresFact]
    public async Task MigrationAndConcurrentSavesPreserveExactlyOneExpense()
    {
        var connection = Environment.GetEnvironmentVariable("TRAVELCOMPANION_TEST_POSTGRES")!;
        var schema = $"tc_expenses_{Guid.NewGuid():N}";
        await using var admin = new NpgsqlConnection(connection); await admin.OpenAsync();
        await using (var command = admin.CreateCommand()) { command.CommandText = $"CREATE SCHEMA \"{schema}\""; await command.ExecuteNonQueryAsync(); }
        try
        {
            var options = new DbContextOptionsBuilder<TravelCompanionDbContext>().UseNpgsql(
                new NpgsqlConnectionStringBuilder(connection) { SearchPath = schema }.ConnectionString, p => p.EnableRetryOnFailure()).Options;
            var tripId = Guid.NewGuid(); string token;
            await using (var db = new TravelCompanionDbContext(options))
            {
                await db.Database.MigrateAsync();
                var destination = new Destination { Id = Guid.NewGuid(), Name = "Japan", Slug = schema, Country = "Japan", HeroImageUrl = "", ShortDescription = "" };
                var user = new AppUser { Id = Guid.NewGuid(), Email = $"{schema}@test.local", DisplayName = "Test" };
                db.AddRange(destination, user, new Trip { Id = tripId, AppUserId = user.Id, DestinationId = destination.Id,
                    TravelerName = "Test", StartsOn = new(2026, 9, 29), EndsOn = new(2026, 10, 2) });
                await db.SaveChangesAsync(); (_, token) = await new UserSessionService(db).CreateSessionAsync(user, tripId: tripId);
            }
            var id = Guid.NewGuid();
            async Task<SaveExpenseResult> Save(SaveExpenseRequest request)
            {
                await using var db = new TravelCompanionDbContext(options);
                var context = new DefaultHttpContext(); context.Request.Headers.Authorization = $"Bearer {token}";
                return await new ExpenseService(db, new UserSessionService(db), new Rates()).SaveAsync(context, tripId, id, request, default);
            }
            var request = new SaveExpenseRequest(10, "EUR", new(2026, 9, 29), ExpenseCategory.Food, "Lunch", null, null, 0, 0, Guid.NewGuid());
            var same = await Task.WhenAll(Save(request), Save(request));
            Assert.All(same, x => { Assert.True(x.Saved); Assert.Equal(1, x.Entry!.Revision); });
            var different = await Task.WhenAll(Save(request with { ExpectedRevision = 1, Amount = 20, MutationId = Guid.NewGuid() }),
                Save(request with { ExpectedRevision = 1, Amount = 30, MutationId = Guid.NewGuid() }));
            Assert.Single(different, x => x.Saved); Assert.Single(different, x => !x.Saved);
            await using var verify = new TravelCompanionDbContext(options);
            Assert.Single(await verify.TripExpenses.ToListAsync());
            verify.Trips.Remove(await verify.Trips.SingleAsync()); await verify.SaveChangesAsync();
            Assert.Empty(await verify.TripExpenses.ToListAsync());
        }
        finally
        {
            await using var command = admin.CreateCommand(); command.CommandText = $"DROP SCHEMA IF EXISTS \"{schema}\" CASCADE"; await command.ExecuteNonQueryAsync();
        }
    }
    private sealed class Rates : IExpenseRateService
    {
        public Task<ExpenseRateDto?> GetAsync(string currency, string target, DateOnly date, CancellationToken ct) =>
            Task.FromResult<ExpenseRateDto?>(new(currency, target, 1, date, "identity"));
    }
}

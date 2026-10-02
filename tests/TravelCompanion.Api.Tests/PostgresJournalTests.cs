using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using TravelCompanion.Api.Data;
using TravelCompanion.Api.Models;
using TravelCompanion.Api.Services;
using TravelCompanion.Shared;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Api.Tests;

public sealed class PostgresJournalTests
{
    [PostgresItineraryIdempotencyTests.PostgresFact]
    public async Task MigrationBackfillsNotesAndConcurrentEditsRemainIdempotent()
    {
        var connectionString = Environment.GetEnvironmentVariable("TRAVELCOMPANION_TEST_POSTGRES")!;
        var schema = $"tc_journal_{Guid.NewGuid():N}";
        await using var admin = new NpgsqlConnection(connectionString);
        await admin.OpenAsync();
        await using (var command = admin.CreateCommand())
        {
            command.CommandText = $"CREATE SCHEMA \"{schema}\"";
            await command.ExecuteNonQueryAsync();
        }
        try
        {
            var options = new DbContextOptionsBuilder<TravelCompanionDbContext>().UseNpgsql(
                new NpgsqlConnectionStringBuilder(connectionString) { SearchPath = schema }.ConnectionString,
                p => p.EnableRetryOnFailure()).Options;
            var user = Guid.NewGuid(); var trip = Guid.NewGuid(); var activity = Guid.NewGuid(); string token;
            await using (var db = new TravelCompanionDbContext(options))
            {
                var priorMigration = db.Database.GetMigrations().TakeWhile(x => !x.EndsWith("AddPersonalJournal", StringComparison.Ordinal)).Last();
                await db.GetService<IMigrator>().MigrateAsync(priorMigration);
                var destination = new Destination { Id = Guid.NewGuid(), Name = "Japan", Slug = schema, Country = "Japan", HeroImageUrl = "", ShortDescription = "" };
                var owner = new AppUser { Id = user, Email = $"{schema}@test.local", DisplayName = "Journal" };
                db.AddRange(destination, owner, new Trip { Id = trip, AppUserId = user, DestinationId = destination.Id, TravelerName = "Journal",
                    StartsOn = new(2026, 10, 1), EndsOn = new(2026, 10, 2) });
                db.Reservations.Add(new Reservation { Id = activity, TripId = trip, Date = new(2026, 10, 1), Title = "Café", City = "Tokyo",
                    LocationName = "Café", Address = "", ConfirmationCode = "private", Notes = "Una nota anterior", Owner = ItineraryItemOwner.Traveler });
                await db.SaveChangesAsync();
                await db.Database.MigrateAsync();
                var migrated = await db.JournalNotes.SingleAsync();
                Assert.Equal("Una nota anterior", migrated.Notes);
                Assert.Equal(1, migrated.Revision);
                (_, token) = await new UserSessionService(db).CreateSessionAsync(owner, tripId: trip, accessMode: SessionAccessMode.FreeMapPreview);
            }
            async Task<JournalSaveResult> Save(SaveJournalNoteRequest request)
            {
                await using var db = new TravelCompanionDbContext(options);
                var context = new DefaultHttpContext(); context.Request.Headers.Authorization = $"Bearer {token}";
                return await new JournalService(db, new UserSessionService(db)).SaveAsync(context, trip, activity, request, default);
            }
            var request = new SaveJournalNoteRequest("Una nota nueva", 1, Guid.NewGuid());
            var retries = await Task.WhenAll(Save(request), Save(request));
            Assert.All(retries, x => Assert.True(x.Saved));
            Assert.All(retries, x => Assert.Equal(2, x.Entry.Revision));
            var concurrent = await Task.WhenAll(Save(new("Versión A", 2, Guid.NewGuid())), Save(new("Versión B", 2, Guid.NewGuid())));
            Assert.Single(concurrent, x => x.Saved);
            Assert.Single(concurrent, x => !x.Saved);
            var freeId = Guid.NewGuid();
            async Task<JournalFreeSaveResult> Free(SaveJournalFreeEntryRequest? value, DeleteJournalFreeEntryRequest? delete = null)
            {
                await using var db = new TravelCompanionDbContext(options);
                var context = new DefaultHttpContext(); context.Request.Headers.Authorization = $"Bearer {token}";
                var service = new JournalService(db, new UserSessionService(db));
                return delete is null ? await service.SaveFreeAsync(context, trip, freeId, value!, default)
                    : await service.DeleteFreeAsync(context, trip, freeId, delete, default);
            }
            var freeRequest = new SaveJournalFreeEntryRequest("", "Tokyo", new(2026, 9, 1), "Before the trip", 0, Guid.NewGuid());
            var freeRetries = await Task.WhenAll(Free(freeRequest), Free(freeRequest));
            Assert.All(freeRetries, x => { Assert.True(x.Saved); Assert.Equal(1, x.Entry.Revision); });
            var freeRace = await Task.WhenAll(
                Free(freeRequest with { ExpectedRevision = 1, MutationId = Guid.NewGuid(), Notes = "Device A" }),
                Free(freeRequest with { ExpectedRevision = 1, MutationId = Guid.NewGuid(), Notes = "Device B" }));
            Assert.Single(freeRace, x => x.Saved); Assert.Single(freeRace, x => !x.Saved);
            var deleteRequest = new DeleteJournalFreeEntryRequest(2, Guid.NewGuid());
            var deleted = await Free(null, deleteRequest);
            Assert.True(deleted.Saved); Assert.True(deleted.Entry.Deleted);
            Assert.Equal(deleted, await Free(null, deleteRequest));
            Assert.False((await Free(freeRequest with { ExpectedRevision = 3, MutationId = Guid.NewGuid() })).Saved);
            await using var verification = new TravelCompanionDbContext(options);
            var indexes = await verification.Database.SqlQueryRaw<string>("""
                SELECT indexname AS "Value" FROM pg_indexes WHERE schemaname = current_schema()
                AND tablename = 'JournalFreeEntries'
                """).ToListAsync();
            Assert.Contains("IX_JournalFreeEntries_UserId_TripId_Date_Id", indexes);
            var beforeFreeEntries = verification.Database.GetMigrations().TakeWhile(x => !x.EndsWith("AddJournalFreeEntries", StringComparison.Ordinal)).Last();
            await Assert.ThrowsAsync<PostgresException>(() => verification.GetService<IMigrator>().MigrateAsync(beforeFreeEntries));
            Assert.Single(await verification.JournalFreeEntries.ToListAsync());
            var stranger = new AppUser { Id = Guid.NewGuid(), Email = $"{schema}-other@test.local", DisplayName = "Other" };
            verification.AppUsers.Add(stranger); await verification.SaveChangesAsync();
            var otherSessions = new UserSessionService(verification);
            var (_, otherToken) = await otherSessions.CreateSessionAsync(stranger);
            var otherContext = new DefaultHttpContext(); otherContext.Request.Headers.Authorization = $"Bearer {otherToken}";
            var otherJournal = new JournalService(verification, otherSessions);
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => otherJournal.ListFreeAsync(otherContext, trip, default));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => otherJournal.SaveFreeAsync(otherContext, trip, freeId, freeRequest, default));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => otherJournal.DeleteFreeAsync(otherContext, trip, freeId, deleteRequest, default));
            verification.Reservations.Remove(await verification.Reservations.SingleAsync());
            await verification.SaveChangesAsync();
            Assert.Single(await verification.JournalNotes.ToListAsync());
            verification.Trips.Remove(await verification.Trips.SingleAsync());
            await verification.SaveChangesAsync();
            Assert.Empty(await verification.JournalNotes.ToListAsync());
            Assert.Empty(await verification.JournalFreeEntries.ToListAsync());
        }
        finally
        {
            await using var command = admin.CreateCommand();
            command.CommandText = $"DROP SCHEMA IF EXISTS \"{schema}\" CASCADE";
            await command.ExecuteNonQueryAsync();
        }
    }
}

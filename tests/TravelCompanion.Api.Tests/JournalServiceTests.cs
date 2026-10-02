using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using TravelCompanion.Api.Data;
using TravelCompanion.Api.Models;
using TravelCompanion.Api.Services;
using TravelCompanion.Shared;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Api.Tests;

public sealed class JournalServiceTests
{
    [Fact]
    public async Task FreeEntriesAreIndependentIdempotentAndCannotResurrect()
    {
        await using var fixture = await Fixture.CreateAsync(SessionAccessMode.FreeMapPreview);
        var id = Guid.NewGuid();
        var request = new SaveJournalFreeEntryRequest("", "", new(2026, 9, 1), "Preparación", 0, Guid.NewGuid());
        var first = await fixture.Service.SaveFreeAsync(fixture.Context, fixture.Trip.Id, id, request, default);
        Assert.True(first.Saved);
        Assert.Equal(first, await fixture.Service.SaveFreeAsync(fixture.Context, fixture.Trip.Id, id, request, default));
        var other = await fixture.Service.SaveFreeAsync(fixture.Context, fixture.Trip.Id, Guid.NewGuid(), request with { MutationId = Guid.NewGuid() }, default);
        Assert.True(other.Saved);
        Assert.Equal(0, fixture.Trip.PlanRevision);
        Assert.Equal("Texto editorial", fixture.Activity.Notes);
        var stale = await fixture.Service.SaveFreeAsync(fixture.Context, fixture.Trip.Id, id, request with { MutationId = Guid.NewGuid() }, default);
        Assert.False(stale.Saved);
        var deletion = new DeleteJournalFreeEntryRequest(1, Guid.NewGuid());
        var deleted = await fixture.Service.DeleteFreeAsync(fixture.Context, fixture.Trip.Id, id, deletion, default);
        Assert.True(deleted.Entry.Deleted);
        Assert.Equal(deleted, await fixture.Service.DeleteFreeAsync(fixture.Context, fixture.Trip.Id, id, deletion, default));
        Assert.False((await fixture.Service.SaveFreeAsync(fixture.Context, fixture.Trip.Id, id,
            request with { ExpectedRevision = 2, MutationId = Guid.NewGuid() }, default)).Saved);
        Assert.Contains(await fixture.Service.ListFreeAsync(fixture.Context, fixture.Trip.Id, default), x => x.Id == id && x.Deleted);
    }

    [Fact]
    public async Task FreeEntriesEnforceOwnerLimitsAndAccountCleanup()
    {
        await using var fixture = await Fixture.CreateAsync();
        var request = new SaveJournalFreeEntryRequest("", "", new(2026, 10, 1), "", 0, Guid.NewGuid());
        await fixture.Service.SaveFreeAsync(fixture.Context, fixture.Trip.Id, Guid.NewGuid(), request, default);
        await Assert.ThrowsAsync<ArgumentException>(() => fixture.Service.SaveFreeAsync(fixture.Context, fixture.Trip.Id,
            Guid.NewGuid(), request with { Notes = new string('x', 2001) }, default));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => fixture.Service.ListFreeAsync(fixture.Context, Guid.NewGuid(), default));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => fixture.Service.DeleteFreeAsync(fixture.Context, Guid.NewGuid(),
            Guid.NewGuid(), new(0, Guid.NewGuid()), default));
        var account = new EmailAccountService(fixture.Db, new UserSessionService(fixture.Db), new NoEmail(),
            Microsoft.Extensions.Options.Options.Create(new TravelCompanion.Api.Options.EmailVerificationOptions()));
        await account.DeleteAccountAsync(fixture.Context, default);
        Assert.Empty(await fixture.Db.JournalFreeEntries.ToListAsync());
    }
    [Fact]
    public async Task AccountDeletionErasesNotesEvenThoughAccountIsSoftDeleted()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.Service.SaveAsync(fixture.Context, fixture.Trip.Id, fixture.Activity.Id,
            new("Recuerdo privado", 0, Guid.NewGuid()), default);
        var account = new EmailAccountService(fixture.Db, new UserSessionService(fixture.Db), new NoEmail(),
            Microsoft.Extensions.Options.Options.Create(new TravelCompanion.Api.Options.EmailVerificationOptions()));
        await account.DeleteAccountAsync(fixture.Context, default);
        Assert.Empty(await fixture.Db.JournalNotes.ToListAsync());
        Assert.NotNull((await fixture.Db.AppUsers.SingleAsync()).DeletedAtUtc);
    }

    [Fact]
    public async Task LegacyEditorUpdatesSameJournalEntryWithoutDuplicatingIt()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Activity.Owner = ItineraryItemOwner.Traveler;
        fixture.Activity.Notes = "Nota original";
        await JournalService.SyncLegacyAsync(fixture.Db, fixture.Trip, fixture.Activity, default);
        await fixture.Db.SaveChangesAsync();
        fixture.Activity.Notes = "Nota editada";
        await JournalService.SyncLegacyAsync(fixture.Db, fixture.Trip, fixture.Activity, default);
        await fixture.Db.SaveChangesAsync();
        var entry = Assert.Single(await fixture.Db.JournalNotes.ToListAsync());
        Assert.Equal("Nota editada", entry.Notes); Assert.Equal(2, entry.Revision);
        await fixture.Service.SaveAsync(fixture.Context, fixture.Trip.Id, fixture.Activity.Id,
            new("Guardado desde Travel Assistant.", 2, Guid.NewGuid()), default);
        await JournalService.SyncLegacyAsync(fixture.Db, fixture.Trip, fixture.Activity, default);
        await fixture.Db.SaveChangesAsync();
        Assert.Equal("Guardado desde Travel Assistant.", (await fixture.Db.JournalNotes.SingleAsync()).Notes);
    }

    private sealed class NoEmail : ITransactionalEmailSender
    {
        public Task SendVerificationCodeAsync(string email, string code, string locale, CancellationToken cancellationToken) => Task.CompletedTask;
    }
    private sealed class CapturedEmail : ITransactionalEmailSender
    {
        public string Code = "";
        public Task SendVerificationCodeAsync(string email, string code, string locale, CancellationToken cancellationToken)
        { Code = code; return Task.CompletedTask; }
    }
    [Fact]
    public async Task AnonymousLinkMovesFreeMemoriesAndTombstonesToVerifiedOwner()
    {
        await using var fixture = await Fixture.CreateAsync(SessionAccessMode.FreeMapPreview);
        var source = await fixture.Db.AppUsers.SingleAsync();
        source.Email = "free-preview+journal@travelcompanion.system";
        var target = new AppUser { Id = Guid.NewGuid(), Email = "verified@example.com", DisplayName = "Verified" };
        fixture.Db.AppUsers.Add(target); await fixture.Db.SaveChangesAsync();
        var id = Guid.NewGuid();
        await fixture.Service.SaveFreeAsync(fixture.Context, fixture.Trip.Id, id, new("", "", new(2026, 10, 1), "Local", 0, Guid.NewGuid()), default);
        await fixture.Service.DeleteFreeAsync(fixture.Context, fixture.Trip.Id, Guid.NewGuid(), new(0, Guid.NewGuid()), default);
        var sender = new CapturedEmail();
        var account = new EmailAccountService(fixture.Db, new UserSessionService(fixture.Db), sender,
            Microsoft.Extensions.Options.Options.Create(new TravelCompanion.Api.Options.EmailVerificationOptions { HashSecret = "journal-test-secret-only" }));
        await account.RequestCodeAsync(fixture.Context, new(target.Email, "es"), default);
        var linked = await account.VerifyCodeAsync(fixture.Context, new(target.Email, sender.Code), default);
        Assert.Equal(target.Id, linked.UserId);
        Assert.Equal(source.Id, linked.LinkedFromUserId);
        Assert.Equal(target.Id, fixture.Trip.AppUserId);
        var entries = await fixture.Db.JournalFreeEntries.ToListAsync();
        Assert.Equal(2, entries.Count); Assert.All(entries, x => Assert.Equal(target.Id, x.UserId));
        Assert.Single(entries, x => x.Deleted);
    }
    [Fact]
    public async Task CuratedReservationCanHavePersonalNoteWithoutChangingReservation()
    {
        await using var fixture = await Fixture.CreateAsync();
        var request = new SaveJournalNoteRequest("Muy buen café", 0, Guid.NewGuid());
        var result = await fixture.Service.SaveAsync(fixture.Context, fixture.Trip.Id, fixture.Activity.Id, request, default);
        Assert.True(result.Saved);
        Assert.Equal("Texto editorial", fixture.Activity.Notes);
        Assert.Equal(new TimeOnly(17, 0), fixture.Activity.StartsAt);
        Assert.Equal(0, fixture.Trip.PlanRevision);
        Assert.Equal(1, result.Entry.Revision);
    }

    [Fact]
    public async Task RetryIsIdempotentAndStaleWriteReturnsServerNote()
    {
        await using var fixture = await Fixture.CreateAsync();
        var request = new SaveJournalNoteRequest("Primera nota", 0, Guid.NewGuid());
        var first = await fixture.Service.SaveAsync(fixture.Context, fixture.Trip.Id, fixture.Activity.Id, request, default);
        var retry = await fixture.Service.SaveAsync(fixture.Context, fixture.Trip.Id, fixture.Activity.Id, request, default);
        Assert.Equal(first, retry);
        var conflict = await fixture.Service.SaveAsync(fixture.Context, fixture.Trip.Id, fixture.Activity.Id,
            new("Otra versión", 0, Guid.NewGuid()), default);
        Assert.False(conflict.Saved);
        Assert.Equal("Primera nota", conflict.Entry.Notes);
        Assert.Single(await fixture.Db.JournalNotes.ToListAsync());
    }

    [Fact]
    public async Task OtherTripCannotBeReadOrWritten()
    {
        await using var fixture = await Fixture.CreateAsync();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => fixture.Service.ListAsync(fixture.Context, Guid.NewGuid(), default));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => fixture.Service.SaveAsync(fixture.Context, Guid.NewGuid(), fixture.Activity.Id,
            new("Privado", 0, Guid.NewGuid()), default));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => fixture.Service.SaveAsync(fixture.Context, fixture.Trip.Id, Guid.NewGuid(),
            new("Privado", 0, Guid.NewGuid()), default));
    }

    [Fact]
    public async Task DeletingActivityRetainsSnapshotAndAllowsEditingMemory()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.Service.SaveAsync(fixture.Context, fixture.Trip.Id, fixture.Activity.Id, new("Recuerdo", 0, Guid.NewGuid()), default);
        fixture.Db.Reservations.Remove(fixture.Activity); await fixture.Db.SaveChangesAsync();
        var result = await fixture.Service.SaveAsync(fixture.Context, fixture.Trip.Id, fixture.Activity.Id, new("Recuerdo ampliado", 1, Guid.NewGuid()), default);
        Assert.True(result.Saved);
        Assert.Equal("Café", result.Entry.Title);
    }

    [Fact]
    public async Task TravelerNoteUpdatesLegacyCopyAndRevisionProtectsOldEditors()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Activity.Owner = ItineraryItemOwner.Traveler; await fixture.Db.SaveChangesAsync();
        await fixture.Service.SaveAsync(fixture.Context, fixture.Trip.Id, fixture.Activity.Id, new("Mi nota", 0, Guid.NewGuid()), default);
        Assert.Equal("Mi nota", fixture.Activity.Notes);
        Assert.Equal(1, fixture.Trip.PlanRevision);
    }

    [Fact]
    public async Task FreeSessionCanWriteWithoutEditingEntitlement()
    {
        await using var fixture = await Fixture.CreateAsync(SessionAccessMode.FreeMapPreview);
        var result = await fixture.Service.SaveAsync(fixture.Context, fixture.Trip.Id, fixture.Activity.Id, new("Recuerdo gratis", 0, Guid.NewGuid()), default);
        Assert.True(result.Saved);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        public required TravelCompanionDbContext Db { get; init; }
        public required JournalService Service { get; init; }
        public required DefaultHttpContext Context { get; init; }
        public required Trip Trip { get; init; }
        public required Reservation Activity { get; init; }
        public static async Task<Fixture> CreateAsync(SessionAccessMode mode = SessionAccessMode.Trip)
        {
            var db = new TravelCompanionDbContext(new DbContextOptionsBuilder<TravelCompanionDbContext>()
                .UseInMemoryDatabase($"journal-{Guid.NewGuid()}").Options);
            var user = new AppUser { Id = Guid.NewGuid(), Email = "journal@test.local", DisplayName = "Test" };
            var trip = new Trip { Id = Guid.NewGuid(), AppUserId = user.Id, TravelerName = "Test", StartsOn = new(2026, 10, 1), EndsOn = new(2026, 10, 3) };
            var activity = new Reservation { Id = Guid.NewGuid(), TripId = trip.Id, Title = "Café", City = "Tokyo", Date = trip.StartsOn,
                StartsAt = new(17, 0), LocationName = "Café", Address = "", ConfirmationCode = "secret", Notes = "Texto editorial" };
            db.AddRange(user, trip, activity); await db.SaveChangesAsync();
            var sessions = new UserSessionService(db);
            var (_, token) = await sessions.CreateSessionAsync(user, tripId: trip.Id, accessMode: mode);
            var context = new DefaultHttpContext(); context.Request.Headers.Authorization = $"Bearer {token}";
            return new() { Db = db, Service = new(db, sessions), Context = context, Trip = trip, Activity = activity };
        }
        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }
}

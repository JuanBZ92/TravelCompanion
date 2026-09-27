using TravelCompanion.Mobile.Services;
using TravelCompanion.Shared;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Mobile.Tests;

[Collection("Free map session")]
public sealed class JournalStoreTests
{
    [Fact]
    public async Task VerifiedLinkTransfersPendingNoteAndLocalPhotosToNewOwner()
    {
        var sessions = new AuthSessionService(); var account = Session(); var store = new JournalStore(new(), sessions, new());
        try
        {
            await sessions.SaveAsync(account); var scope = store.Scope(); var memory = Memory(scope.TripId);
            await store.SaveAsync(scope, memory, "Mi recuerdo");
            await store.AddPhotosAsync(scope, memory, [new FileResult()]);
            var linked = account with { UserId = Guid.NewGuid(), LinkedFromUserId = account.UserId };
            await store.TransferLinkedTripAsync(linked with { LinkedFromUserId = null });
            Assert.Single(await store.LoadAsync(scope, [], false, default));
            await store.TransferLinkedTripAsync(linked);
            await sessions.SaveAsync(linked);
            var moved = Assert.Single(await store.LoadAsync(store.Scope(), [], false, default));
            Assert.Equal("Mi recuerdo", moved.Text);
            Assert.NotNull(await store.PhotoAsync(store.Scope(), Assert.Single(moved.Images).Id));
            await sessions.SaveAsync(account);
            Assert.Empty(await store.LoadAsync(store.Scope(), [], false, default));
        }
        finally { sessions.Clear(); }
    }
    private static AuthSessionDto Session() => new(Guid.NewGuid(), "test@example.com", "Test", false, "token", Guid.NewGuid(),
        AccessMode: SessionAccessMode.Trip, ExperienceMode: ExperienceMode.CuratedPremium, Capabilities: new(false, false, true, true, false));
    private static JournalMemory Memory(Guid trip) => new(new(Guid.NewGuid(), trip, "Café", "Tokyo", new(2026, 9, 27), "", 0, DateTimeOffset.UtcNow));

    [Fact]
    public async Task OfflineDraftSurvivesLogoutAndOnlyAppearsInItsOwnAccountAndTrip()
    {
        var sessions = new AuthSessionService(); var account = Session(); var disk = new OfflineCacheService();
        var store = new JournalStore(disk, sessions, new());
        try
        {
            await sessions.SaveAsync(account);
            var scope = store.Scope(); var memory = Memory(scope.TripId);
            await store.SaveAsync(scope, memory, "Recuerdo sin conexión");
            var draft = Assert.Single(await store.LoadAsync(scope, [], true, default));
            var mutation = draft.Pending!.MutationId;
            sessions.Clear();
            await sessions.SaveAsync(Session()); Assert.Empty(await store.LoadAsync(store.Scope(), [], false, default));
            await sessions.SaveAsync(account with { TripId = Guid.NewGuid() }); Assert.Empty(await store.LoadAsync(store.Scope(), [], false, default));
            await sessions.SaveAsync(account);
            draft = Assert.Single(await store.LoadAsync(store.Scope(), [], false, default));
            Assert.Equal("Recuerdo sin conexión", draft.Text); Assert.Equal(mutation, draft.Pending!.MutationId);
        }
        finally { sessions.Clear(); }
    }

    [Fact]
    public async Task ConflictKeepsBothVersionsUntilExplicitResolution()
    {
        var sessions = new AuthSessionService(); var account = Session();
        var api = new TravelCompanionApiClient(); var store = new JournalStore(new(), sessions, api);
        try
        {
            await sessions.SaveAsync(account); var scope = store.Scope(); var memory = Memory(scope.TripId);
            var remote = memory.Note with { Notes = "Desde otro celular", Revision = 3 };
            api.SaveJournal = _ => Task.FromResult(new JournalSaveResult(false, remote));
            await store.SaveAsync(scope, memory, "Mi borrador");
            var draft = Assert.Single(await store.LoadAsync(scope, [], true, default));
            Assert.Equal("Mi borrador", draft.Text); Assert.Equal(remote, draft.Conflict);
            await store.ResolveAsync(scope, draft, true);
            draft = Assert.Single(await store.LoadAsync(scope, [], false, default));
            Assert.Null(draft.Conflict); Assert.Equal(3, draft.Pending!.ExpectedRevision); Assert.Equal("Mi borrador", draft.Text);
            api.SaveJournal = value => Task.FromResult(new JournalSaveResult(true, remote with { Notes = value.Notes, Revision = 4 }));
            var saved = Assert.Single(await store.LoadAsync(scope, [], true, default));
            Assert.Null(saved.Pending); Assert.Equal("Mi borrador", saved.Note.Notes);
        }
        finally { sessions.Clear(); }
    }

    [Fact]
    public async Task SessionSwitchDuringNetworkReadCannotPublishIntoAnotherAccount()
    {
        var sessions = new AuthSessionService(); var api = new TravelCompanionApiClient();
        var store = new JournalStore(new(), sessions, api);
        try
        {
            await sessions.SaveAsync(Session()); var scope = store.Scope();
            api.FetchJournal = async () => { await sessions.SaveAsync(Session()); return [Memory(scope.TripId).Note]; };
            await Assert.ThrowsAsync<OperationCanceledException>(() => store.LoadAsync(scope, [], true, default));
            Assert.Empty(await store.LoadAsync(store.Scope(), [], false, default));
        }
        finally { sessions.Clear(); }
    }

    [Fact]
    public async Task PhotosAreLimitedAndDeletingTripDoesNotRemoveOtherTrip()
    {
        var sessions = new AuthSessionService(); var account = Session(); var store = new JournalStore(new(), sessions, new());
        try
        {
            await sessions.SaveAsync(account); var scope = store.Scope(); var memory = Memory(scope.TripId);
            await store.AddPhotosAsync(scope, memory, Enumerable.Range(0, 12).Select(_ => new FileResult()));
            var entry = Assert.Single(await store.LoadAsync(scope, [], false, default)); Assert.Equal(10, entry.Images.Length);
            Assert.NotNull(await store.PhotoAsync(scope, entry.Images[0].Id));
            await store.ChangePhotoAsync(scope, memory.Note.ActivityId, entry.Images[0].Id, true);
            Assert.Null(await store.PhotoAsync(scope, entry.Images[0].Id));
            var other = account with { TripId = Guid.NewGuid() }; await sessions.SaveAsync(other);
            await store.SaveAsync(store.Scope(), Memory(other.TripId!.Value), "Otro viaje");
            await store.DeleteTripAsync(account.UserId, account.TripId!.Value);
            Assert.Single(await store.LoadAsync(store.Scope(), [], false, default));
            await store.DeleteAccountAsync(account.UserId);
            await Assert.ThrowsAsync<OperationCanceledException>(() => store.LoadAsync(store.Scope(), [], false, default));
        }
        finally { sessions.Clear(); }
    }
}

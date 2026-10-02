using TravelCompanion.Mobile.Services;
using TravelCompanion.Shared;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Mobile.Tests;

[Collection("Free map session")]
public sealed class JournalStoreTests
{
    [Fact]
    public void DisplayTitleUsesTitleThenPlaceWithoutInventingActivityIdentity()
    {
        var free = JournalMemory.NewFree(Guid.NewGuid(), new(2026, 10, 2));
        var withPlace = free with { FreeEntry = free.FreeEntry! with { Place = "Rikugien Gardens" } };
        Assert.Equal("Rikugien Gardens", JournalText.DisplayTitle(withPlace));
        var withTitle = withPlace with { FreeEntry = withPlace.FreeEntry! with { Title = "Tarde entre jardines" } };
        Assert.Equal("Tarde entre jardines", JournalText.DisplayTitle(withTitle));
        Assert.True(withTitle.IsFree);
        Assert.Equal(Guid.Empty, withTitle.Note.ActivityId);
    }

    [Fact]
    public async Task OpeningLocalJournalDoesNotRewriteUnchangedEncryptedIndex()
    {
        var sessions = new AuthSessionService(); var disk = new OfflineCacheService();
        var store = new JournalStore(disk, sessions, new());
        try
        {
            await sessions.SaveAsync(Session()); var scope = store.Scope();
            await store.SaveAsync(scope, JournalMemory.NewFree(scope.TripId, new(2026, 10, 2)), "Texto");
            var writes = disk.Writes;
            Assert.Single(await store.LoadAsync(scope, [], false, default));
            Assert.Single(await store.LoadAsync(scope, [], false, default));
            Assert.Equal(writes, disk.Writes);
        }
        finally { sessions.Clear(); }
    }

    [Fact]
    public async Task ChosenActivityCanBeStoredAsPlaceWithoutCreatingLinkedNote()
    {
        var sessions = new AuthSessionService(); var store = new JournalStore(new(), sessions, new());
        try
        {
            await sessions.SaveAsync(Session()); var scope = store.Scope();
            var memory = JournalMemory.NewFree(scope.TripId, new(2026, 10, 2));
            memory = memory with { FreeEntry = memory.FreeEntry! with { Place = "Museo de arte" } };
            await store.SaveDraftAsync(scope, memory, "Una tarde especial");
            await store.SaveAsync(scope, memory, "Una tarde especial");
            var saved = Assert.Single(await store.LoadAsync(scope, [], false, default));
            Assert.True(saved.IsFree);
            Assert.Equal("Museo de arte", saved.City);
            Assert.Equal("Una tarde especial", saved.Text);
            Assert.Equal(Guid.Empty, saved.Note.ActivityId);
        }
        finally { sessions.Clear(); }
    }

    [Fact]
    public async Task DeletionConflictRemainsVisibleUntilUserResolvesIt()
    {
        var sessions = new AuthSessionService(); var api = new TravelCompanionApiClient(); var store = new JournalStore(new(), sessions, api);
        try {
            await sessions.SaveAsync(Session()); var scope = store.Scope();
            var dto = JournalMemory.NewFree(scope.TripId, new(2026, 10, 1)).FreeEntry! with { Notes = "First", Revision = 1 };
            api.FetchJournalFree = () => Task.FromResult(new List<JournalFreeEntryDto> { dto });
            var saved = Assert.Single(await store.LoadAsync(scope, [], true, default));
            await store.DeleteFreeLocalAsync(scope, saved);
            dto = dto with { Notes = "Edited elsewhere", Revision = 2 };
            api.DeleteJournalFree = _ => Task.FromResult(new JournalFreeSaveResult(false, dto));
            var conflict = Assert.Single(await store.LoadAsync(scope, [], true, default));
            Assert.True(conflict.HasConflict); Assert.NotNull(conflict.DeletePending);
            Assert.True(Assert.Single(await store.LoadAsync(scope, [], true, default)).HasConflict);
            await store.ResolveAsync(scope, conflict, false);
            var remote = Assert.Single(await store.LoadAsync(scope, [], false, default));
            Assert.False(remote.HasConflict); Assert.Null(remote.DeletePending); Assert.Equal("Edited elsewhere", remote.Text);
        } finally { sessions.Clear(); }
    }
    [Fact]
    public async Task FailedOrCancelledPhotoSelectionDoesNotReplaceDraftAndAccountSwitchDiscardsIt()
    {
        var sessions = new AuthSessionService(); var store = new JournalStore(new(), sessions, new());
        try {
            await sessions.SaveAsync(Session()); var scope = store.Scope();
            var memory = JournalMemory.NewFree(scope.TripId, new(2026, 10, 1));
            await store.SaveDraftAsync(scope, memory, "Keep this");
            Assert.Equal(memory, await store.AddDraftPhotosAsync(scope, memory, "Keep this", []));
            await Assert.ThrowsAsync<IOException>(() => store.AddDraftPhotosAsync(scope, memory, "Keep this",
                [new FileResult(), new FileResult { Read = () => throw new IOException("unreadable photo") }]));
            var retained = Assert.Single(await store.DraftsAsync(scope));
            Assert.Empty(retained.Memory.Images); Assert.Equal("Keep this", retained.Text);
            await Assert.ThrowsAsync<OperationCanceledException>(() => store.AddDraftPhotosAsync(scope, memory, "Keep this",
                [new FileResult { Read = async () => { await sessions.SaveAsync(Session()); return new MemoryStream([1]); } }]));
            Assert.Empty(await store.DraftsAsync(store.Scope()));
            Assert.Empty(await store.LoadAsync(store.Scope(), [], false, default));
        } finally { sessions.Clear(); }
    }
    [Fact]
    public void LegacyJsonRetainsPendingConflictAndCover()
    {
        var memory = Memory(Guid.NewGuid());
        var photo = new JournalPhoto(Guid.NewGuid());
        var pending = new SaveJournalNoteRequest("Offline", 1, Guid.NewGuid());
        var conflict = memory.Note with { Revision = 2, Notes = "Remote" };
        var json = System.Text.Json.JsonSerializer.Serialize(new { memory.Note, Pending = pending, Conflict = conflict, Photos = new[] { photo }, CoverId = photo.Id });
        var restored = System.Text.Json.JsonSerializer.Deserialize<JournalMemory>(json)!;
        Assert.False(restored.IsFree); Assert.False(restored.IsDraft);
        Assert.Equal(pending, restored.Pending); Assert.Equal(conflict, restored.Conflict);
        Assert.Equal(photo.Id, restored.CoverId); Assert.Single(restored.Images);
    }

    [Fact]
    public async Task FreeDraftPhotosTransferOnVerifiedLinkAndAreErasedWithTrip()
    {
        var sessions = new AuthSessionService(); var account = Session(); var disk = new OfflineCacheService();
        var store = new JournalStore(disk, sessions, new());
        try {
            await sessions.SaveAsync(account); var scope = store.Scope();
            var draft = await store.AddDraftPhotosAsync(scope, JournalMemory.NewFree(scope.TripId, new(2026, 10, 1)), "Local", [new FileResult()]);
            var target = account with { UserId = Guid.NewGuid(), LinkedFromUserId = account.UserId };
            await store.TransferLinkedTripAsync(target); await sessions.SaveAsync(target);
            var moved = Assert.Single(await store.DraftsAsync(store.Scope()));
            Assert.Equal("Local", moved.Text); Assert.NotNull(await store.PhotoAsync(store.Scope(), moved.Memory.Images[0].Id));
            Assert.Empty(await store.LoadAsync(store.Scope(), [], false, default));
            await store.DeleteTripAsync(target.UserId, target.TripId!.Value);
            Assert.DoesNotContain(disk.Entries.Keys, x => x.StartsWith($"personal-journal-{target.UserId}-{target.TripId}-"));
        } finally { sessions.Clear(); }
    }

    [Fact]
    public async Task InterruptedResponseKeepsFreeMutationForRetry()
    {
        var sessions = new AuthSessionService(); var api = new TravelCompanionApiClient();
        var store = new JournalStore(new(), sessions, api);
        try {
            await sessions.SaveAsync(Session()); var scope = store.Scope();
            var memory = JournalMemory.NewFree(scope.TripId, new(2026, 10, 1));
            await store.SaveAsync(scope, memory, "Keep me");
            api.FetchJournalFree = () => throw new IOException("stream closed");
            var saved = Assert.Single(await store.LoadAsync(scope, [], true, default));
            Assert.Equal("Keep me", saved.Text); Assert.NotNull(saved.FreePending);
        } finally { sessions.Clear(); }
    }
    [Fact]
    public async Task FreeDraftSurvivesRestartButIsNotConfirmedOrSynced()
    {
        var sessions = new AuthSessionService(); var disk = new OfflineCacheService(); var api = new TravelCompanionApiClient();
        var store = new JournalStore(disk, sessions, api); var account = Session();
        try {
            await sessions.SaveAsync(account); var scope = store.Scope();
            var memory = JournalMemory.NewFree(scope.TripId, new(2025, 12, 1));
            await store.SaveDraftAsync(scope, memory, "Preparación");
            store = new JournalStore(disk, sessions, api);
            Assert.Empty(await store.LoadAsync(scope, [], true, default));
            Assert.Equal("Preparación", Assert.Single(await store.DraftsAsync(scope)).Text);
            await sessions.SaveAsync(Session());
            Assert.Empty(await store.DraftsAsync(store.Scope()));
            await Assert.ThrowsAsync<OperationCanceledException>(() => store.SaveDraftAsync(scope, memory, "Wrong account"));
            await sessions.SaveAsync(account); scope = store.Scope();
            await store.DiscardDraftAsync(scope, memory);
            Assert.Empty(await store.DraftsAsync(scope));
        } finally { sessions.Clear(); }
    }

    [Fact]
    public async Task PhotoOnlyDraftUsesCoverAndStaysSeparateFromAlbumUntilSaved()
    {
        var sessions = new AuthSessionService(); var store = new JournalStore(new(), sessions, new());
        try {
            await sessions.SaveAsync(Session()); var scope = store.Scope();
            var memory = JournalMemory.NewFree(scope.TripId, new(2026, 10, 1));
            await Assert.ThrowsAsync<ArgumentException>(() => store.SaveAsync(scope, memory, ""));
            await Assert.ThrowsAsync<ArgumentException>(() => store.SaveDraftAsync(scope, memory, new string('x', 2001)));
            memory = await store.AddDraftPhotosAsync(scope, memory, "", Enumerable.Range(0, 12).Select(_ => new FileResult()));
            Assert.Equal(10, memory.Images.Length); Assert.Equal(memory.Images[0].Id, memory.CoverId);
            Assert.Empty(await store.LoadAsync(scope, [], false, default));
            memory = memory with { CoverId = memory.Images[3].Id };
            await store.SaveAsync(scope, memory, "");
            await store.DiscardDraftAsync(scope, memory);
            var saved = Assert.Single(await store.LoadAsync(scope, [], false, default));
            Assert.False(saved.IsDraft); Assert.Equal(memory.CoverId, saved.CoverId);
            Assert.NotNull(await store.PhotoAsync(scope, saved.CoverId!.Value));
            var edit = saved with { Photos = [] };
            await store.SaveDraftAsync(scope, edit, "Unsaved edit");
            Assert.Equal(10, Assert.Single(await store.LoadAsync(scope, [], false, default)).Images.Length);
            await store.DiscardDraftAsync(scope, edit);
            Assert.Equal(10, Assert.Single(await store.LoadAsync(scope, [], false, default)).Images.Length);
        } finally { sessions.Clear(); }
    }

    [Fact]
    public async Task FreeSyncRetainsMutationsResolvesConflictsAndHonorsTombstones()
    {
        var sessions = new AuthSessionService(); var api = new TravelCompanionApiClient(); var store = new JournalStore(new(), sessions, api);
        try {
            await sessions.SaveAsync(Session()); var scope = store.Scope();
            var memory = JournalMemory.NewFree(scope.TripId, new(2026, 10, 1));
            await store.SaveAsync(scope, memory, "Offline");
            var pending = Assert.Single(await store.LoadAsync(scope, [], true, default));
            var mutation = pending.FreePending!.MutationId;
            Assert.Equal(mutation, Assert.Single(await store.LoadAsync(scope, [], true, default)).FreePending!.MutationId);
            var remote = memory.FreeEntry! with { Notes = "Other device", Revision = 2 };
            api.SaveJournalFree = _ => Task.FromResult(new JournalFreeSaveResult(false, remote));
            var conflict = Assert.Single(await store.LoadAsync(scope, [], true, default));
            Assert.True(conflict.HasConflict); Assert.Equal("Offline", conflict.Text);
            await store.ResolveAsync(scope, conflict, true);
            api.SaveJournalFree = p => Task.FromResult(new JournalFreeSaveResult(true, remote with { Notes = p.Notes, Revision = 3 }));
            var saved = Assert.Single(await store.LoadAsync(scope, [], true, default));
            Assert.Null(saved.FreePending); Assert.Equal("Offline", saved.Text);
            api.FetchJournalFree = () => Task.FromResult(new List<JournalFreeEntryDto> { remote with { Deleted = true, Revision = 4 } });
            Assert.Empty(await store.LoadAsync(scope, [], true, default));
            await Assert.ThrowsAsync<InvalidOperationException>(() => store.SaveAsync(scope, saved, "Cannot resurrect"));
        } finally { sessions.Clear(); }
    }

    [Fact]
    public async Task SeveralFreeMemoriesOnSameDateKeepSeparateIdentitiesAndLegacyNotes()
    {
        var sessions = new AuthSessionService(); var store = new JournalStore(new(), sessions, new());
        try {
            await sessions.SaveAsync(Session()); var scope = store.Scope();
            var date = new DateOnly(2026, 10, 1);
            await store.SaveAsync(scope, JournalMemory.NewFree(scope.TripId, date), "One");
            await store.SaveAsync(scope, JournalMemory.NewFree(scope.TripId, date), "Two");
            await store.SaveAsync(scope, Memory(scope.TripId), "Legacy");
            var all = await store.LoadAsync(scope, [], false, default);
            Assert.Equal(3, all.Count); Assert.Equal(3, all.Select(x => x.Key).Distinct().Count());
            Assert.False(all[0].IsFree); Assert.All(all.Where(x => x.IsFree), x => Assert.Equal(Guid.Empty, x.Note.ActivityId));
        } finally { sessions.Clear(); }
    }
    [Fact]
    public async Task CorrectedLegacySourceRemovesOnlyUntouchedPreview()
    {
        var sessions = new AuthSessionService();
        var store = new JournalStore(new(), sessions, new());
        try
        {
            await sessions.SaveAsync(Session());
            var scope = store.Scope();
            var item = new ScheduleItemDto(Guid.NewGuid(), null, ReservationType.Event,
                new(2026, 10, 1), new(12, 0), null, null, "Cafe", "Tokyo", "", "", "",
                "Texto demo", null, null, null, null, null, null, Owner: ItineraryItemOwner.Traveler);
            Assert.Single(await store.LoadAsync(scope, [item], false, default));
            Assert.Empty(await store.LoadAsync(scope, [item with { Notes = "" }], false, default));
            var preview = Assert.Single(await store.LoadAsync(scope, [item], false, default));
            await store.SaveAsync(scope, preview, "Mi recuerdo personal");
            var retained = Assert.Single(await store.LoadAsync(scope, [item with { Notes = "" }], false, default));
            Assert.Equal("Mi recuerdo personal", retained.Text);
        }
        finally { sessions.Clear(); }
    }

    [Fact]
    public async Task FreeReadOnlyTripCanKeepNotesAndPhotosWithoutItineraryEditing()
    {
        var sessions = new AuthSessionService();
        var store = new JournalStore(new(), sessions, new());
        try
        {
            await sessions.SaveAsync(Session() with
            {
                AccessMode = SessionAccessMode.FreeMapPreview,
                ExperienceMode = ExperienceMode.FreePreview,
                Capabilities = new(false, false, false, false, false)
            });
            Assert.False(sessions.CanEditItinerary);
            var scope = store.Scope();
            var memory = Memory(scope.TripId);
            await store.SaveAsync(scope, memory, "Mi recuerdo en Free");
            await store.AddPhotosAsync(scope, memory, [new FileResult()]);
            var saved = Assert.Single(await store.LoadAsync(scope, [], false, default));
            Assert.Equal("Mi recuerdo en Free", saved.Text);
            Assert.NotNull(await store.PhotoAsync(scope, Assert.Single(saved.Images).Id));
        }
        finally { sessions.Clear(); }
    }

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

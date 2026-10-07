using TravelCompanion.Mobile.Services;
using TravelCompanion.Shared;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Mobile.Tests;

[Collection("Free map session")]
public sealed class JournalStoreTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EditingAndAddingPhotosWhileSaveIsInFlightKeepsNewMutation(bool free)
    {
        var sessions = new AuthSessionService(); var api = new TravelCompanionApiClient(); var store = new JournalStore(new(), sessions, api);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            await sessions.SaveAsync(Session()); var scope = store.Scope();
            var memory = free ? JournalMemory.NewFree(scope.TripId, new(2026, 10, 2)) : Memory(scope.TripId);
            await store.SaveAsync(scope, memory, "First version");
            var original = Assert.Single(await store.LoadAsync(scope, [], false, default));
            api.SaveJournal = async request => { entered.SetResult(); await release.Task; return new(true, original.Note with { Notes = request.Notes, Revision = 1 }); };
            api.SaveJournalFree = async request => { entered.SetResult(); await release.Task; return new(true, original.FreeEntry! with { Notes = request.Notes, Revision = 1 }); };
            var syncing = store.LoadAsync(scope, [], true, default);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            // The mocked response cannot finish until after all local writes complete.
            await store.SaveAsync(scope, original, "Newer local writing").WaitAsync(TimeSpan.FromSeconds(5));
            var newer = Assert.Single(await store.LoadAsync(scope, [], false, default));
            var mutation = free ? newer.FreePending!.MutationId : newer.Pending!.MutationId;
            await store.SaveDraftAsync(scope, newer, "Unfinished draft").WaitAsync(TimeSpan.FromSeconds(5));
            var attached = await store.AddConfirmedPhotosAsync(scope, newer, [new FileResult()]).WaitAsync(TimeSpan.FromSeconds(5));
            release.SetResult();
            var retained = Assert.Single(await syncing);
            Assert.Equal("Newer local writing", retained.Text);
            Assert.Equal(mutation, free ? retained.FreePending!.MutationId : retained.Pending!.MutationId);
            Assert.Equal(1, free ? retained.FreePending!.ExpectedRevision : retained.Pending!.ExpectedRevision);
            Assert.Equal(attached.Images, retained.Images); Assert.Equal(attached.CoverId, retained.CoverId);
            Assert.Equal("Unfinished draft", Assert.Single(await store.DraftsAsync(scope)).Text);
            Assert.NotNull(await store.PhotoAsync(scope, attached.Images[0].Id, true));
            api.SaveJournal = request => Task.FromResult(new JournalSaveResult(true, retained.Note with { Notes = request.Notes, Revision = 2 }));
            api.SaveJournalFree = request => Task.FromResult(new JournalFreeSaveResult(true, retained.FreeEntry! with { Notes = request.Notes, Revision = 2 }));
            var confirmed = Assert.Single(await store.LoadAsync(scope, [], true, default));
            Assert.Equal("Newer local writing", confirmed.Text); Assert.Null(confirmed.Pending); Assert.Null(confirmed.FreePending);
        }
        finally { release.TrySetResult(); sessions.Clear(); }
    }

    [Fact]
    public async Task LocalReadAndDraftDoNotWaitForRemoteFetchAndDeletedTripCannotBeRecreated()
    {
        var sessions = new AuthSessionService(); var api = new TravelCompanionApiClient(); var store = new JournalStore(new(), sessions, api);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            await sessions.SaveAsync(Session()); var scope = store.Scope(); var memory = Memory(scope.TripId);
            api.FetchJournal = async () => { entered.SetResult(); await release.Task; return [memory.Note with { Notes = "Remote" }]; };
            var syncing = store.LoadAsync(scope, [], true, default);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await store.SaveDraftAsync(scope, memory, "Keep draft").WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Empty(await store.ReadConfirmedLocalAsync(scope).WaitAsync(TimeSpan.FromSeconds(5)));
            await store.DeleteTripAsync(scope.UserId, scope.TripId).WaitAsync(TimeSpan.FromSeconds(5));
            release.SetResult();
            await Assert.ThrowsAsync<OperationCanceledException>(() => syncing);
            await Assert.ThrowsAsync<OperationCanceledException>(() => store.ReadConfirmedLocalAsync(scope));
        }
        finally { release.TrySetResult(); sessions.Clear(); }
    }

    [Fact]
    public async Task DeletingDuringSaveKeepsDeletionAndRebasesOverAcknowledgedSave()
    {
        var sessions = new AuthSessionService(); var api = new TravelCompanionApiClient(); var store = new JournalStore(new(), sessions, api);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            await sessions.SaveAsync(Session()); var scope = store.Scope(); var memory = JournalMemory.NewFree(scope.TripId, new(2026, 10, 2));
            await store.SaveAsync(scope, memory, "First");
            var before = Assert.Single(await store.LoadAsync(scope, [], false, default));
            api.SaveJournalFree = async request => { entered.SetResult(); await release.Task; return new(true, before.FreeEntry! with { Notes = request.Notes, Revision = 1 }); };
            var syncing = store.LoadAsync(scope, [], true, default); await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await store.DeleteFreeLocalAsync(scope, before).WaitAsync(TimeSpan.FromSeconds(5));
            release.SetResult(); Assert.Empty(await syncing);
            DeleteJournalFreeEntryRequest? deletion = null;
            api.DeleteJournalFree = request => { deletion = request; return Task.FromResult(new JournalFreeSaveResult(true,
                before.FreeEntry! with { Revision = 2, Deleted = true })); };
            Assert.Empty(await store.LoadAsync(scope, [], true, default));
            Assert.NotNull(deletion); Assert.Equal(1, deletion.ExpectedRevision);
        }
        finally { release.TrySetResult(); sessions.Clear(); }
    }

    [Fact]
    public async Task RemoteTombstoneDoesNotSilentlyDiscardWritingMadeDuringFetch()
    {
        var sessions = new AuthSessionService(); var api = new TravelCompanionApiClient(); var store = new JournalStore(new(), sessions, api);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            await sessions.SaveAsync(Session()); var scope = store.Scope();
            var dto = JournalMemory.NewFree(scope.TripId, new(2026, 10, 2)).FreeEntry! with { Notes = "Original", Revision = 1 };
            api.FetchJournalFree = () => Task.FromResult(new List<JournalFreeEntryDto> { dto });
            var before = Assert.Single(await store.LoadAsync(scope, [], true, default));
            api.FetchJournalFree = async () => { entered.SetResult(); await release.Task; return [dto with { Deleted = true, Revision = 2 }]; };
            var syncing = store.LoadAsync(scope, [], true, default); await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await store.SaveAsync(scope, before, "Writing before noticing deletion").WaitAsync(TimeSpan.FromSeconds(5));
            var current = Assert.Single(await store.LoadAsync(scope, [], false, default));
            var withPhoto = await store.AddConfirmedPhotosAsync(scope, current, [new FileResult()]).WaitAsync(TimeSpan.FromSeconds(5));
            release.SetResult();
            var conflict = Assert.Single(await syncing);
            Assert.Equal("Writing before noticing deletion", conflict.Text); Assert.True(conflict.FreeConflict!.Deleted);
            Assert.NotNull(await store.PhotoAsync(scope, withPhoto.Images[0].Id));
            api.FetchJournalFree = () => Task.FromResult(new List<JournalFreeEntryDto> { dto with { Deleted = true, Revision = 2 } });
            Assert.Equal(conflict, Assert.Single(await store.LoadAsync(scope, [], true, default)));
            await store.ResolveAsync(scope, conflict, false);
            Assert.Empty(await store.ReadConfirmedLocalAsync(scope)); Assert.Null(await store.PhotoAsync(scope, withPhoto.Images[0].Id));
        }
        finally { release.TrySetResult(); sessions.Clear(); }
    }

    [Fact]
    public async Task RemoteTombstoneKeepsPreviouslySavedOfflineWritingAndPhotosUntilResolution()
    {
        var sessions = new AuthSessionService(); var api = new TravelCompanionApiClient(); var store = new JournalStore(new(), sessions, api);
        try
        {
            await sessions.SaveAsync(Session()); var scope = store.Scope();
            var original = JournalMemory.NewFree(scope.TripId, new(2026, 10, 2)).FreeEntry! with { Notes = "Original", Revision = 1 };
            api.FetchJournalFree = () => Task.FromResult(new List<JournalFreeEntryDto> { original });
            var before = Assert.Single(await store.LoadAsync(scope, [], true, default));
            await store.SaveAsync(scope, before, "Written offline before synchronizing");
            var pending = Assert.Single(await store.ReadConfirmedLocalAsync(scope));
            var withPhoto = await store.AddConfirmedPhotosAsync(scope, pending, [new FileResult()]);
            var tombstone = original with { Deleted = true, Revision = 2 };
            var sent = 0;
            api.FetchJournalFree = () => Task.FromResult(new List<JournalFreeEntryDto> { tombstone });
            api.SaveJournalFree = _ => { sent++; return Task.FromResult(new JournalFreeSaveResult(false, tombstone)); };

            var conflict = Assert.Single(await store.LoadAsync(scope, [], true, default));
            Assert.Equal(pending.Text, conflict.Text); Assert.Equal(pending.FreePending!.MutationId, conflict.FreePending!.MutationId);
            Assert.Equal(tombstone, conflict.FreeConflict); Assert.False(conflict.Deleted);
            Assert.Equal(withPhoto.Images, conflict.Images); Assert.NotNull(await store.PhotoAsync(scope, withPhoto.Images[0].Id, true));
            Assert.Equal(conflict, Assert.Single(await store.LoadAsync(scope, [], true, default)));
            Assert.Equal(0, sent);

            await store.ResolveAsync(scope, conflict, false);
            Assert.Empty(await store.ReadConfirmedLocalAsync(scope));
            Assert.Null(await store.PhotoAsync(scope, withPhoto.Images[0].Id));
            Assert.Null(await store.PhotoAsync(scope, withPhoto.Images[0].Id, true));
        }
        finally { sessions.Clear(); }
    }

    [Fact]
    public async Task DeletionReturnedBySaveKeepsWritingAndPhotosAndCannotResurrectOnResolution()
    {
        var sessions = new AuthSessionService(); var api = new TravelCompanionApiClient(); var store = new JournalStore(new(), sessions, api);
        try
        {
            await sessions.SaveAsync(Session()); var scope = store.Scope();
            var original = JournalMemory.NewFree(scope.TripId, new(2026, 10, 2)).FreeEntry! with { Notes = "Original", Revision = 1 };
            api.FetchJournalFree = () => Task.FromResult(new List<JournalFreeEntryDto> { original });
            var before = Assert.Single(await store.LoadAsync(scope, [], true, default));
            await store.SaveAsync(scope, before, "Unsynchronized personal writing");
            var pending = Assert.Single(await store.ReadConfirmedLocalAsync(scope));
            var withPhoto = await store.AddConfirmedPhotosAsync(scope, pending, [new FileResult()]);
            var tombstone = original with { Deleted = true, Revision = 2 };
            api.SaveJournalFree = _ => Task.FromResult(new JournalFreeSaveResult(false, tombstone));

            var conflict = Assert.Single(await store.LoadAsync(scope, [], true, default));
            Assert.Equal(pending.Text, conflict.Text); Assert.Equal(pending.FreePending!.MutationId, conflict.FreePending!.MutationId);
            Assert.Equal(tombstone, conflict.FreeConflict); Assert.False(conflict.Deleted);
            Assert.NotNull(await store.PhotoAsync(scope, withPhoto.Images[0].Id));
            api.FetchJournalFree = () => Task.FromResult(new List<JournalFreeEntryDto> { tombstone });
            Assert.Equal(conflict, Assert.Single(await store.LoadAsync(scope, [], true, default)));

            await store.ResolveAsync(scope, conflict, true);
            Assert.Empty(await store.ReadConfirmedLocalAsync(scope));
            Assert.Null(await store.PhotoAsync(scope, withPhoto.Images[0].Id));
            Assert.Null(await store.PhotoAsync(scope, withPhoto.Images[0].Id, true));
            await Assert.ThrowsAsync<InvalidOperationException>(() => store.SaveAsync(scope, conflict, "Cannot resurrect"));
        }
        finally { sessions.Clear(); }
    }

    [Fact]
    public async Task PhotoAttachedDuringTombstoneFetchRemainsVisibleUntilExplicitResolution()
    {
        var sessions = new AuthSessionService(); var api = new TravelCompanionApiClient(); var store = new JournalStore(new(), sessions, api);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            await sessions.SaveAsync(Session()); var scope = store.Scope();
            var original = JournalMemory.NewFree(scope.TripId, new(2026, 10, 2)).FreeEntry! with { Notes = "Original", Revision = 1 };
            api.FetchJournalFree = () => Task.FromResult(new List<JournalFreeEntryDto> { original });
            var before = Assert.Single(await store.LoadAsync(scope, [], true, default));
            var tombstone = original with { Deleted = true, Revision = 2 };
            api.FetchJournalFree = async () => { entered.SetResult(); await release.Task; return [tombstone]; };
            var syncing = store.LoadAsync(scope, [], true, default); await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var withPhoto = await store.AddConfirmedPhotosAsync(scope, before, [new FileResult()]);
            Assert.Null(withPhoto.FreePending);
            release.SetResult();
            var conflict = Assert.Single(await syncing);
            Assert.Equal(tombstone, conflict.FreeConflict); Assert.Null(conflict.FreePending); Assert.False(conflict.Deleted);
            Assert.Equal(withPhoto.Images, conflict.Images); Assert.NotNull(await store.PhotoAsync(scope, withPhoto.Images[0].Id, true));
            api.FetchJournalFree = () => Task.FromResult(new List<JournalFreeEntryDto> { tombstone });
            Assert.Equal(conflict, Assert.Single(await store.LoadAsync(scope, [], true, default)));
            await store.ResolveAsync(scope, conflict, false);
            Assert.Empty(await store.ReadConfirmedLocalAsync(scope));
            Assert.Null(await store.PhotoAsync(scope, withPhoto.Images[0].Id));
        }
        finally { release.TrySetResult(); sessions.Clear(); }
    }

    [Fact]
    public async Task LegacyPhotoMigratesOnceAndSubsequentThumbnailReadDoesNotLoadFullPayload()
    {
        var sessions = new AuthSessionService(); var disk = new OfflineCacheService(); var store = new JournalStore(disk, sessions, new());
        try
        {
            await sessions.SaveAsync(Session()); var scope = store.Scope(); var photo = new JournalPhoto(Guid.NewGuid());
            var prefix = $"personal-journal-{scope.UserId}-{scope.TripId}-";
            var legacy = new JournalImageData(new byte[2 * 1024 * 1024], [7, 8]);
            await disk.SaveAsync(prefix + photo.Id, legacy);
            await disk.SaveAsync(prefix + "index", new List<JournalMemory> { Memory(scope.TripId) with { Photos = [photo], CoverId = photo.Id } });
            Assert.Equal(legacy.Thumbnail, await store.PhotoAsync(scope, photo.Id, true));
            Assert.False(disk.Entries.ContainsKey(prefix + photo.Id));
            var reads = disk.Reads;
            Assert.Equal(legacy.Thumbnail, await store.PhotoAsync(scope, photo.Id, true));
            Assert.Equal(1, disk.Reads - reads);
            Assert.Equal(legacy.Image, await store.PhotoAsync(scope, photo.Id));
            Assert.Equal(photo.Id, Assert.Single(await store.ReadConfirmedLocalAsync(scope)).CoverId);
            await store.ChangePhotoAsync(scope, Assert.Single(await store.ReadConfirmedLocalAsync(scope)).Id, photo.Id, true);
            Assert.Null(await store.PhotoAsync(scope, photo.Id)); Assert.Null(await store.PhotoAsync(scope, photo.Id, true));
        }
        finally { sessions.Clear(); }
    }

    [Fact]
    public async Task FailedLegacyMigrationKeepsPhotoReadableAndCanRetry()
    {
        var sessions = new AuthSessionService(); var disk = new OfflineCacheService(); var store = new JournalStore(disk, sessions, new());
        try
        {
            await sessions.SaveAsync(Session()); var scope = store.Scope(); var photo = new JournalPhoto(Guid.NewGuid());
            var prefix = $"personal-journal-{scope.UserId}-{scope.TripId}-";
            var legacy = new JournalImageData([1, 2, 3], [4]);
            await disk.SaveAsync(prefix + photo.Id, legacy);
            await disk.SaveAsync(prefix + "index", new List<JournalMemory> { Memory(scope.TripId) with { Photos = [photo] } });
            var writes = 0; disk.BeforeSave = () => ++writes == 2 ? throw new IOException("No space") : Task.CompletedTask;
            Assert.Equal(legacy.Thumbnail, await store.PhotoAsync(scope, photo.Id, true));
            Assert.True(disk.Entries.ContainsKey(prefix + photo.Id));
            disk.BeforeSave = null;
            Assert.Equal(legacy.Image, await store.PhotoAsync(scope, photo.Id));
            Assert.Equal(legacy.Thumbnail, await store.PhotoAsync(scope, photo.Id, true));
            Assert.False(disk.Entries.ContainsKey(prefix + photo.Id));
        }
        finally { sessions.Clear(); }
    }

    [Fact]
    public async Task CancelledLegacyMigrationRetainsReadablePhotoAndRetryCompletesSplit()
    {
        var sessions = new AuthSessionService(); var disk = new OfflineCacheService(); var store = new JournalStore(disk, sessions, new());
        using var cancellation = new CancellationTokenSource();
        try
        {
            await sessions.SaveAsync(Session()); var scope = store.Scope(); var photo = new JournalPhoto(Guid.NewGuid());
            var prefix = $"personal-journal-{scope.UserId}-{scope.TripId}-";
            var legacy = new JournalImageData([1, 2, 3], [4]);
            await disk.SaveAsync(prefix + photo.Id, legacy);
            await disk.SaveAsync(prefix + "index", new List<JournalMemory> { Memory(scope.TripId) with { Photos = [photo] } });
            var writes = 0;
            disk.BeforeSave = () => { if (++writes == 2) cancellation.Cancel(); return Task.CompletedTask; };
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.PhotoAsync(scope, photo.Id, true, cancellation.Token));
            Assert.True(disk.Entries.ContainsKey(prefix + photo.Id));
            disk.BeforeSave = null;
            Assert.Equal(legacy.Image, await store.PhotoAsync(scope, photo.Id));
            Assert.Equal(legacy.Thumbnail, await store.PhotoAsync(scope, photo.Id, true));
            Assert.False(disk.Entries.ContainsKey(prefix + photo.Id));
            Assert.Equal(photo.Id, Assert.Single(await store.ReadConfirmedLocalAsync(scope)).Images[0].Id);
        }
        finally { sessions.Clear(); }
    }

    [Fact]
    public async Task FailedAccountLinkCopyKeepsSourceWritingAndPhotosAndCanRetry()
    {
        var sessions = new AuthSessionService(); var disk = new OfflineCacheService(); var store = new JournalStore(disk, sessions, new());
        try
        {
            var account = Session(); await sessions.SaveAsync(account); var source = store.Scope();
            var memory = Memory(source.TripId); await store.SaveAsync(source, memory, "Writing retained after failed link");
            await store.AddPhotosAsync(source, memory, [new FileResult()]);
            var current = Assert.Single(await store.ReadConfirmedLocalAsync(source));
            var image = await store.PhotoAsync(source, current.Images[0].Id);
            var thumbnail = await store.PhotoAsync(source, current.Images[0].Id, true);
            var verified = account with { UserId = Guid.NewGuid(), LinkedFromUserId = account.UserId };
            var writes = 0;
            disk.BeforeSave = () => ++writes == 2 ? throw new IOException("No space") : Task.CompletedTask;
            await Assert.ThrowsAsync<IOException>(() => store.TransferLinkedTripAsync(verified));
            disk.BeforeSave = null;
            Assert.Equal(current, Assert.Single(await store.ReadConfirmedLocalAsync(source)));
            Assert.Equal(image, await store.PhotoAsync(source, current.Images[0].Id));
            Assert.Equal(thumbnail, await store.PhotoAsync(source, current.Images[0].Id, true));
            await store.TransferLinkedTripAsync(verified); await sessions.SaveAsync(verified);
            var target = store.Scope();
            Assert.Equal(current, Assert.Single(await store.ReadConfirmedLocalAsync(target)));
            Assert.Equal(image, await store.PhotoAsync(target, current.Images[0].Id));
            Assert.Equal(thumbnail, await store.PhotoAsync(target, current.Images[0].Id, true));
            Assert.DoesNotContain(disk.Entries.Keys, key => key.StartsWith($"personal-journal-{source.UserId}-{source.TripId}-"));
        }
        finally { sessions.Clear(); }
    }

    [Fact]
    public async Task LocalConfirmedSearchExcludesDraftsAndDeletedEntriesAndRejectsCrossTripWriting()
    {
        var sessions = new AuthSessionService(); var store = new JournalStore(new(), sessions, new());
        try
        {
            var account = Session(); await sessions.SaveAsync(account); var scope = store.Scope();
            var confirmed = JournalMemory.NewFree(scope.TripId, new(2026, 10, 2));
            var draft = JournalMemory.NewFree(scope.TripId, new(2026, 10, 3));
            var deleted = JournalMemory.NewFree(scope.TripId, new(2026, 10, 4));
            await store.SaveAsync(scope, confirmed, "Find me"); await store.SaveDraftAsync(scope, draft, "Private unfinished draft");
            await store.SaveAsync(scope, deleted, "Deleted"); await store.DeleteFreeLocalAsync(scope, deleted);
            Assert.Equal("Find me", Assert.Single(await store.ReadConfirmedLocalAsync(scope)).Text);
            await sessions.SaveAsync(account with { TripId = Guid.NewGuid() }); var otherScope = store.Scope();
            await Assert.ThrowsAsync<OperationCanceledException>(() => store.SaveAsync(otherScope, confirmed, "Wrong trip"));
            await Assert.ThrowsAsync<OperationCanceledException>(() => store.SaveDraftAsync(otherScope, draft, "Wrong trip"));
            Assert.Empty(await store.ReadConfirmedLocalAsync(otherScope));
        }
        finally { sessions.Clear(); }
    }

    [Fact]
    public async Task LegacyAndSplitPhotosTransferTogetherWithoutCrossAccountLeakage()
    {
        var sessions = new AuthSessionService(); var disk = new OfflineCacheService(); var store = new JournalStore(disk, sessions, new());
        try
        {
            var account = Session(); await sessions.SaveAsync(account); var source = store.Scope();
            var memory = Memory(source.TripId); await store.SaveAsync(source, memory, "Confirmed");
            await store.AddPhotosAsync(source, memory, [new FileResult()]);
            var current = Assert.Single(await store.LoadAsync(source, [], false, default));
            var legacyPhoto = new JournalPhoto(Guid.NewGuid()); var legacy = new JournalImageData([5, 6], [7]);
            var prefix = $"personal-journal-{source.UserId}-{source.TripId}-";
            await disk.SaveAsync(prefix + legacyPhoto.Id, legacy);
            await store.SaveDraftAsync(source, current with { Photos = [..current.Images, legacyPhoto], CoverId = legacyPhoto.Id }, "Draft writing");
            await sessions.SaveAsync(Session()); Assert.Null(await store.PhotoAsync(store.Scope(), legacyPhoto.Id));
            await sessions.SaveAsync(account);
            var verified = account with { UserId = Guid.NewGuid(), LinkedFromUserId = account.UserId };
            await store.TransferLinkedTripAsync(verified); await sessions.SaveAsync(verified); var target = store.Scope();
            var draft = Assert.Single(await store.DraftsAsync(target));
            Assert.Equal(legacyPhoto.Id, draft.Memory.CoverId); Assert.Equal("Draft writing", draft.Text);
            Assert.Equal(legacy.Thumbnail, await store.PhotoAsync(target, legacyPhoto.Id, true));
            Assert.Equal(legacy.Image, await store.PhotoAsync(target, legacyPhoto.Id));
            Assert.NotNull(await store.PhotoAsync(target, current.Images[0].Id, true));
            Assert.DoesNotContain(disk.Entries.Keys, key => key.StartsWith(prefix));
            await store.DiscardDraftAsync(target, draft.Memory);
            Assert.Null(await store.PhotoAsync(target, legacyPhoto.Id));
            Assert.NotNull(await store.PhotoAsync(target, current.Images[0].Id, true));
            await store.DeleteTripAsync(target.UserId, target.TripId);
            Assert.DoesNotContain(disk.Entries.Keys, key => key.StartsWith($"personal-journal-{target.UserId}-{target.TripId}-"));
        }
        finally { sessions.Clear(); }
    }

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
    public async Task FailedRemoteSyncReportsFailureAndKeepsPendingLocalMemory()
    {
        var sessions = new AuthSessionService(); var api = new TravelCompanionApiClient();
        var store = new JournalStore(new(), sessions, api);
        try
        {
            await sessions.SaveAsync(Session()); var scope = store.Scope();
            await store.SaveAsync(scope, JournalMemory.NewFree(scope.TripId, new(2026, 10, 2)), "Local");
            api.FetchJournalFree = () => throw new HttpRequestException("Server unavailable");
            var failures = 0;
            var memory = Assert.Single(await store.LoadAsync(scope, [], true, default, () => failures++));
            Assert.Equal(1, failures);
            Assert.Equal("Local", memory.Text);
            Assert.NotNull(memory.FreePending);
            api.FetchJournalFree = () => throw new TaskCanceledException();
            failures = 0;
            memory = Assert.Single(await store.LoadAsync(scope, [], true, default, () => failures++));
            Assert.Equal(1, failures);
            Assert.NotNull(memory.FreePending);
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReaderPhotoAttachmentPreservesConfirmedTextConflictAndDraft(bool free)
    {
        var sessions = new AuthSessionService(); var disk = new OfflineCacheService();
        var api = new TravelCompanionApiClient(); var store = new JournalStore(disk, sessions, api);
        try
        {
            await sessions.SaveAsync(Session()); var scope = store.Scope();
            var entry = free ? JournalMemory.NewFree(scope.TripId, new(2026, 10, 2)) : Memory(scope.TripId);
            await store.SaveAsync(scope, entry, "Confirmed local text");
            api.SaveJournal = _ => Task.FromResult(new JournalSaveResult(false, entry.Note with { Notes = "Remote", Revision = 3 }));
            if (free) api.SaveJournalFree = _ => Task.FromResult(new JournalFreeSaveResult(false,
                entry.FreeEntry! with { Title = "Remote title", Notes = "Remote", Revision = 3 }));
            var before = Assert.Single(await store.LoadAsync(scope, [], true, default));
            Assert.True(before.HasConflict);
            await store.SaveDraftAsync(scope, before, "Unfinished writing");
            var draft = Assert.Single(await store.DraftsAsync(scope));
            var attached = await store.AddConfirmedPhotosAsync(scope, before, [new FileResult(), new FileResult()]);
            Assert.Equal(before, attached with { Photos = before.Photos, CoverId = before.CoverId });
            Assert.Equal(2, attached.Images.Length);
            Assert.Equal(attached.Images[0].Id, attached.CoverId);
            var keptDraft = Assert.Single(await store.DraftsAsync(scope));
            Assert.Equal(draft, keptDraft with { Memory = draft.Memory });
            Assert.Equal(draft.Memory, keptDraft.Memory with { Photos = draft.Memory.Photos, CoverId = draft.Memory.CoverId });
            Assert.Equal(attached.Images, keptDraft.Memory.Images);
            Assert.Equal(attached, Assert.Single(await store.LoadAsync(scope, [], false, default)));
            Assert.NotNull(await store.PhotoAsync(scope, attached.Images[0].Id));
        }
        finally { sessions.Clear(); }
    }

    [Fact]
    public async Task ReaderAttachmentUsesLatestConfirmedContentAndRetainsChosenCover()
    {
        var sessions = new AuthSessionService(); var store = new JournalStore(new(), sessions, new());
        try
        {
            await sessions.SaveAsync(Session()); var scope = store.Scope(); var entry = Memory(scope.TripId);
            await store.SaveAsync(scope, entry, "Original");
            await store.AddPhotosAsync(scope, entry, [new FileResult(), new FileResult()]);
            var before = Assert.Single(await store.LoadAsync(scope, [], false, default));
            await store.ChangePhotoAsync(scope, before.Id, before.Images[1].Id, false);
            var attached = await store.AddConfirmedPhotosAsync(scope, before, [new FileResult
            {
                Read = async () =>
                {
                    await store.SaveAsync(scope, before with { CoverId = before.Images[1].Id }, "Updated while picker was open");
                    return new MemoryStream([1]);
                }
            }]);
            Assert.Equal("Updated while picker was open", attached.Text);
            Assert.Equal(before.Images[1].Id, attached.CoverId);
            Assert.Equal(3, attached.Images.Length);
        }
        finally { sessions.Clear(); }
    }

    [Fact]
    public async Task ReaderPickerCancellationAndUnreadableBatchDoNotChangePhotosOrWriting()
    {
        var sessions = new AuthSessionService(); var disk = new OfflineCacheService();
        var store = new JournalStore(disk, sessions, new());
        try
        {
            await sessions.SaveAsync(Session()); var scope = store.Scope();
            await store.SaveAsync(scope, Memory(scope.TripId), "Keep this");
            var before = Assert.Single(await store.LoadAsync(scope, [], false, default));
            var writes = disk.Writes;
            Assert.Equal(before, await store.AddConfirmedPhotosAsync(scope, before, []));
            await Assert.ThrowsAsync<IOException>(() => store.AddConfirmedPhotosAsync(scope, before,
                [new FileResult(), new FileResult { Read = () => throw new IOException("Unreadable") }]));
            Assert.Equal(writes, disk.Writes);
            Assert.Equal(before, Assert.Single(await store.LoadAsync(scope, [], false, default)));
        }
        finally { sessions.Clear(); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedPhotoBatchRollsBackPayloadsAndPreservesPreviousIndex(bool failIndex)
    {
        var sessions = new AuthSessionService(); var disk = new OfflineCacheService();
        var store = new JournalStore(disk, sessions, new());
        try
        {
            await sessions.SaveAsync(Session()); var scope = store.Scope(); var entry = Memory(scope.TripId);
            await store.SaveAsync(scope, entry, "Keep this");
            await store.AddPhotosAsync(scope, entry, [new FileResult()]);
            var before = Assert.Single(await store.LoadAsync(scope, [], false, default));
            var keys = disk.Entries.Keys.Order().ToArray(); var writes = 0;
            disk.BeforeSave = () => ++writes == (failIndex ? 5 : 2)
                ? throw new IOException("Disk full") : Task.CompletedTask;
            await Assert.ThrowsAsync<IOException>(() => store.AddConfirmedPhotosAsync(scope, before,
                [new FileResult(), new FileResult()]));
            disk.BeforeSave = null;
            Assert.Equal(keys, disk.Entries.Keys.Order().ToArray());
            Assert.Equal(before, Assert.Single(await store.LoadAsync(scope, [], false, default)));
            Assert.NotNull(await store.PhotoAsync(scope, before.Images[0].Id));
        }
        finally { sessions.Clear(); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReaderCannotAttachToDeletedOrUnconfirmedMemory(bool deleted)
    {
        var sessions = new AuthSessionService(); var disk = new OfflineCacheService();
        var store = new JournalStore(disk, sessions, new());
        try
        {
            await sessions.SaveAsync(Session()); var scope = store.Scope();
            var memory = JournalMemory.NewFree(scope.TripId, new(2026, 10, 2));
            if (deleted)
            {
                await store.SaveAsync(scope, memory, "Confirmed");
                memory = Assert.Single(await store.LoadAsync(scope, [], false, default));
                await store.DeleteFreeLocalAsync(scope, memory);
            }
            var keys = disk.Entries.Keys.Order().ToArray();
            await Assert.ThrowsAsync<InvalidOperationException>(() => store.AddConfirmedPhotosAsync(scope, memory, [new FileResult()]));
            Assert.Equal(keys, disk.Entries.Keys.Order().ToArray());
            Assert.Empty(await store.LoadAsync(scope, [], false, default));
        }
        finally { sessions.Clear(); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SessionOrTripChangeDuringReaderSelectionDiscardsBatch(bool changeTrip)
    {
        var sessions = new AuthSessionService(); var account = Session(); var disk = new OfflineCacheService();
        var store = new JournalStore(disk, sessions, new());
        try
        {
            await sessions.SaveAsync(account); var scope = store.Scope();
            await store.SaveAsync(scope, Memory(scope.TripId), "Private writing");
            var before = Assert.Single(await store.LoadAsync(scope, [], false, default));
            var keys = disk.Entries.Keys.Order().ToArray();
            await Assert.ThrowsAsync<OperationCanceledException>(() => store.AddConfirmedPhotosAsync(scope, before,
                [new FileResult { Read = async () =>
                    { await sessions.SaveAsync(changeTrip ? account with { TripId = Guid.NewGuid() } : Session()); return new MemoryStream([1]); } }]));
            Assert.Equal(keys, disk.Entries.Keys.Order().ToArray());
            Assert.Empty(await store.LoadAsync(store.Scope(), [], false, default));
        }
        finally { sessions.Clear(); }
    }

    [Fact]
    public async Task ConcurrentReaderAttachmentsRespectTenPhotoLimit()
    {
        var sessions = new AuthSessionService(); var store = new JournalStore(new(), sessions, new());
        try
        {
            await sessions.SaveAsync(Session()); var scope = store.Scope(); var entry = Memory(scope.TripId);
            await store.SaveAsync(scope, entry, "Confirmed");
            await store.AddPhotosAsync(scope, entry, Enumerable.Range(0, 8).Select(_ => new FileResult()));
            var before = Assert.Single(await store.LoadAsync(scope, [], false, default));
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var file = new FileResult { Read = async () => { await release.Task; return new MemoryStream([1]); } };
            var first = store.AddConfirmedPhotosAsync(scope, before, [file, file]);
            var second = store.AddConfirmedPhotosAsync(scope, before, [file, file]);
            release.SetResult(); await Task.WhenAll(first, second);
            var saved = Assert.Single(await store.LoadAsync(scope, [], false, default));
            Assert.Equal(10, saved.Images.Length);
            Assert.Equal(10, saved.Images.Select(x => x.Id).Distinct().Count());
            Assert.Equal(before.CoverId, saved.CoverId);
            Assert.Equal(before.Pending, saved.Pending);
        }
        finally { sessions.Clear(); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PhotosAttachedInReaderSurviveSavingOrDiscardingPreviousDraft(bool saveDraft)
    {
        var sessions = new AuthSessionService(); var store = new JournalStore(new(), sessions, new());
        try
        {
            await sessions.SaveAsync(Session()); var scope = store.Scope();
            var entry = JournalMemory.NewFree(scope.TripId, new(2026, 10, 2));
            await store.SaveAsync(scope, entry, "Confirmed text");
            var confirmed = Assert.Single(await store.LoadAsync(scope, [], false, default));
            var draft = await store.AddDraftPhotosAsync(scope, confirmed with
                { FreeEntry = confirmed.FreeEntry! with { Title = "Unfinished title" },
                    FreePending = confirmed.FreePending! with { Title = "Unfinished title" } }, "Unfinished text", [new FileResult()]);
            var localPhoto = Assert.Single(draft.Images).Id;
            var attached = await store.AddConfirmedPhotosAsync(scope, confirmed, [new FileResult()]);
            var confirmedPhoto = Assert.Single(attached.Images).Id;
            var updatedDraft = Assert.Single(await store.DraftsAsync(scope));
            Assert.Equal("Unfinished title", updatedDraft.Memory.Title);
            Assert.Equal("Unfinished text", updatedDraft.Text);
            Assert.Equal(localPhoto, updatedDraft.Memory.CoverId);
            Assert.Equal(2, updatedDraft.Memory.Images.Length);
            if (saveDraft) await store.SaveAsync(scope, updatedDraft.Memory, updatedDraft.Text);
            await store.DiscardDraftAsync(scope, updatedDraft.Memory);
            var retained = Assert.Single(await store.LoadAsync(scope, [], false, default));
            Assert.Contains(retained.Images, x => x.Id == confirmedPhoto);
            Assert.NotNull(await store.PhotoAsync(scope, confirmedPhoto));
            Assert.Equal(saveDraft ? "Unfinished text" : "Confirmed text", retained.Text);
            Assert.Equal(saveDraft, retained.Images.Any(x => x.Id == localPhoto));
            Assert.Empty(await store.DraftsAsync(scope));
        }
        finally { sessions.Clear(); }
    }

    [Fact]
    public async Task ReaderAttachmentBlocksFullDraftAndLimitsBatchToDraftSpace()
    {
        var sessions = new AuthSessionService(); var disk = new OfflineCacheService();
        var store = new JournalStore(disk, sessions, new());
        try
        {
            await sessions.SaveAsync(Session()); var scope = store.Scope(); var entry = Memory(scope.TripId);
            await store.SaveAsync(scope, entry, "Confirmed");
            var before = Assert.Single(await store.LoadAsync(scope, [], false, default));
            await store.AddDraftPhotosAsync(scope, before, "Draft", Enumerable.Range(0, 9).Select(_ => new FileResult()));
            var attached = await store.AddConfirmedPhotosAsync(scope, before, [new FileResult(), new FileResult()]);
            Assert.Single(attached.Images);
            var fullDraft = Assert.Single(await store.DraftsAsync(scope));
            Assert.Equal(10, fullDraft.Memory.Images.Length);
            var keys = disk.Entries.Keys.Order().ToArray();
            await Assert.ThrowsAsync<InvalidOperationException>(() => store.AddConfirmedPhotosAsync(scope, attached, [new FileResult()]));
            Assert.Equal(keys, disk.Entries.Keys.Order().ToArray());
            Assert.Equal(fullDraft, Assert.Single(await store.DraftsAsync(scope)));
            Assert.Equal(attached, Assert.Single(await store.LoadAsync(scope, [], false, default)));
        }
        finally { sessions.Clear(); }
    }

    [Fact]
    public async Task IndexWriteFailureRestoresDraftBeforeRemovingNewPayloads()
    {
        var sessions = new AuthSessionService(); var disk = new OfflineCacheService();
        var store = new JournalStore(disk, sessions, new());
        try
        {
            await sessions.SaveAsync(Session()); var scope = store.Scope(); var entry = Memory(scope.TripId);
            await store.SaveAsync(scope, entry, "Confirmed");
            var before = Assert.Single(await store.LoadAsync(scope, [], false, default));
            await store.AddDraftPhotosAsync(scope, before, "Draft", [new FileResult()]);
            var draft = Assert.Single(await store.DraftsAsync(scope));
            var keys = disk.Entries.Keys.Order().ToArray(); var writes = 0;
            disk.BeforeSave = () => ++writes == 4 ? throw new IOException("Index write failed") : Task.CompletedTask;
            await Assert.ThrowsAsync<IOException>(() => store.AddConfirmedPhotosAsync(scope, before, [new FileResult()]));
            disk.BeforeSave = null;
            Assert.Equal(keys, disk.Entries.Keys.Order().ToArray());
            Assert.Equal(draft, Assert.Single(await store.DraftsAsync(scope)));
            Assert.Equal(before, Assert.Single(await store.LoadAsync(scope, [], false, default)));
            Assert.NotNull(await store.PhotoAsync(scope, draft.Memory.Images[0].Id));
        }
        finally { sessions.Clear(); }
    }
}

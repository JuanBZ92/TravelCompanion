using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Mobile.Services;

public sealed record JournalPhoto(Guid Id);
public sealed record JournalPhotoPayload(byte[] Bytes);
public sealed record JournalAcknowledgement(Guid MutationId, int Revision);
public sealed record JournalMemory(JournalNoteDto Note, SaveJournalNoteRequest? Pending = null,
    JournalNoteDto? Conflict = null, JournalPhoto[]? Photos = null, Guid? CoverId = null,
    JournalFreeEntryDto? FreeEntry = null, SaveJournalFreeEntryRequest? FreePending = null,
    JournalFreeEntryDto? FreeConflict = null, DeleteJournalFreeEntryRequest? DeletePending = null,
    bool IsDraft = false, JournalAcknowledgement? Acknowledgement = null)
{
    public bool IsFree => FreeEntry is not null;
    public Guid Id => FreeEntry?.Id ?? Note.ActivityId;
    public string Key => (IsFree ? "free-" : "activity-") + Id;
    public string Text => FreePending?.Notes ?? FreeEntry?.Notes ?? Pending?.Notes ?? Note.Notes;
    public string Title => FreePending?.Title ?? FreeEntry?.Title ?? Note.Title;
    public string City => FreePending?.Place ?? FreeEntry?.Place ?? Note.City;
    public DateOnly Date => FreePending?.Date ?? FreeEntry?.Date ?? Note.Date;
    public int Revision => FreeEntry?.Revision ?? Note.Revision;
    public bool Deleted => FreeEntry?.Deleted == true || (DeletePending is not null && FreeConflict is null);
    public bool HasConflict => Conflict is not null || FreeConflict is not null;
    public JournalPhoto[] Images => Photos ?? [];
    public bool HasContent => !string.IsNullOrWhiteSpace(Text) || Images.Length > 0;
    public string Status => HasConflict ? JournalText.Get("JournalConflict") : Pending is not null || FreePending is not null
        || DeletePending is not null ? JournalText.Get("JournalPending") : "";
    public static JournalMemory NewFree(Guid trip, DateOnly date) => FromFree(new(Guid.NewGuid(), trip, "", "", date, "", 0, DateTimeOffset.MinValue, false)) with { IsDraft = true };
    public static JournalMemory FromFree(JournalFreeEntryDto entry) => new(
        new(Guid.Empty, entry.TripId, "", "", entry.Date, "", 0, DateTimeOffset.MinValue), FreeEntry: entry);
}

public readonly record struct JournalScope(Guid UserId, Guid TripId, long Version);

// Personal content is intentionally outside the disposable bootstrap cache.
public sealed partial class JournalStore(OfflineCacheService cache, AuthSessionService sessions, TravelCompanionApiClient api)
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly SemaphoreSlim syncGate = new(1, 1);
    private readonly HashSet<Guid> deletedUsers = [];
    private readonly HashSet<(Guid, Guid)> deletedTrips = [];
    public JournalScope Scope() => sessions.HasSession && sessions.CurrentUserId is { } user && sessions.CurrentTripId is { } trip
        ? new(user, trip, sessions.ContextVersion) : throw new InvalidOperationException("Abrí un viaje para usar Journal.");
    public bool IsCurrent(JournalScope scope) => sessions.HasSession && sessions.CurrentUserId == scope.UserId
        && sessions.CurrentTripId == scope.TripId && sessions.ContextVersion == scope.Version;
    private void Check(JournalScope scope)
    {
        if (!IsCurrent(scope) || deletedUsers.Contains(scope.UserId) || deletedTrips.Contains((scope.UserId, scope.TripId)))
            throw new OperationCanceledException("La sesión cambió.");
    }
    private void CheckEntry(JournalScope scope, JournalMemory entry)
    {
        Check(scope);
        if (entry.Note.TripId != scope.TripId || entry.FreeEntry is { } free && free.TripId != scope.TripId)
            throw new OperationCanceledException("The memory belongs to another trip.");
    }
    private static string Prefix(JournalScope s) => $"personal-journal-{s.UserId}-{s.TripId}-";
    private async Task<List<JournalMemory>> ReadAsync(JournalScope s, CancellationToken ct) =>
        ((await cache.GetAsync<List<JournalMemory>>(Prefix(s) + "index", cancellationToken: ct))?.Value ?? []).ToList();
    private Task WriteAsync(JournalScope s, List<JournalMemory> entries, CancellationToken ct) => cache.SaveAsync(Prefix(s) + "index", entries, ct);

    public async Task ReplayPendingAsync(CancellationToken ct)
    {
        var scope = Scope();
        if (HasPendingSynchronization(await ReadLocalSnapshotAsync(scope, ct)))
            await LoadAsync(scope, [], true, ct);
    }

    public async Task<List<JournalMemory>> LoadAsync(JournalScope scope, IEnumerable<ScheduleItemDto> items, bool sync,
        CancellationToken ct, Action? onSyncFailure = null)
    {
        await gate.WaitAsync(ct);
        try
        {
            Check(scope);
            var currentItems = items.ToDictionary(x => x.Id);
            // Background replay has no activity seed; avoid decrypting the same index twice.
            if (currentItems.Count > 0)
            {
                var entries = await ReadAsync(scope, ct);
                // Discard only untouched legacy previews whose source was corrected. Never
                // discard saved notes, offline edits, photos, or memories of deleted activities.
                var changed = entries.RemoveAll(x => !x.IsFree && !x.IsDraft && x.Note.Revision == 0 && x.Note.UpdatedAt == DateTimeOffset.MinValue
                    && x.Pending is null && x.Conflict is null && x.Images.Length == 0
                    && currentItems.TryGetValue(x.Note.ActivityId, out var source)
                    && JournalEntries.Build([source]).Count == 0) > 0;
                foreach (var legacy in JournalEntries.Build(currentItems.Values))
                    if (entries.All(x => x.IsFree || x.Note.ActivityId != legacy.Item.Id))
                    {
                        entries.Add(new(new(legacy.Item.Id, scope.TripId, legacy.Title, legacy.Item.City,
                            legacy.Item.Date, legacy.Notes, 0, DateTimeOffset.MinValue)));
                        changed = true;
                    }
                Check(scope);
                if (changed) await WriteAsync(scope, entries, ct);
            }
        }
        finally { gate.Release(); }
        if (sync) await SyncAsync(scope, ct, onSyncFailure);
        return Ordered(await ReadLocalSnapshotAsync(scope, ct));
    }

    // Search and other local readers must not seed activities or wait for the network.
    public async Task<IReadOnlyList<JournalMemory>> ReadConfirmedLocalAsync(JournalScope scope, CancellationToken ct = default) =>
        Ordered(await ReadLocalSnapshotAsync(scope, ct)).Where(x => !x.IsDraft && x.HasContent).ToArray();

    private static List<JournalMemory> Ordered(IEnumerable<JournalMemory> entries) => entries.Where(x => !x.Deleted)
        .OrderBy(x => x.Date).ThenBy(x => x.Title).ThenBy(x => x.Key).ToList();

    private async Task<List<JournalMemory>> ReadLocalSnapshotAsync(JournalScope scope, CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try { Check(scope); var entries = await ReadAsync(scope, ct); Check(scope); return entries; }
        finally { gate.Release(); }
    }

    private async Task<JournalSynchronizationState> SyncAsync(JournalScope scope, CancellationToken ct, Action? onSyncFailure)
    {
        // Only remote work is serialized here. Local saves/photos/drafts use the short gate above.
        Check(scope);
        BeginSynchronization(scope);
        var acquired = false;
        var outcome = JournalSynchronizationState.Pending;
        Exception? failure = null;
        var failureEvent = "journal_sync_failed";
        try
        {
            await syncGate.WaitAsync(ct);
            acquired = true;
            Check(scope);
            if (Connectivity.Current.NetworkAccess != NetworkAccess.Internet)
                return outcome = JournalSynchronizationState.Offline;
            var token = await sessions.GetTokenAsync();
            Check(scope);
            if (string.IsNullOrWhiteSpace(token))
            {
                failure = new InvalidOperationException("Journal session token unavailable.");
                failureEvent = "journal_sync_token_unavailable";
                return outcome = JournalSynchronizationState.Failed;
            }
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(12));
            var remote = await api.GetJournalAsync(token, scope.TripId, timeout.Token);
            await ApplyRemoteNotesAsync(scope, remote, ct);
            var snapshot = await ReadLocalSnapshotAsync(scope, ct);
            foreach (var entry in snapshot.Where(x => !x.IsFree && !x.IsDraft && x.Pending is not null && !x.HasConflict))
            {
                var result = await api.SaveJournalAsync(token, scope.TripId, entry.Id, entry.Pending!, timeout.Token);
                await ApplyNoteResultAsync(scope, entry, result, ct);
            }
            await SyncFreeAsync(scope, token, timeout.Token, ct);
            outcome = HasPendingSynchronization(await ReadLocalSnapshotAsync(scope, ct))
                ? JournalSynchronizationState.Pending : JournalSynchronizationState.Idle;
            return outcome;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested || !IsCurrent(scope)) { throw; }
        catch (Exception exception)
        {
            failure = exception;
            return outcome = Connectivity.Current.NetworkAccess == NetworkAccess.Internet
                ? JournalSynchronizationState.Failed : JournalSynchronizationState.Offline;
        }
        finally
        {
            if (acquired) syncGate.Release();
            // Never call observers while either persistence semaphore is held.
            if (failure is not null) RecordSynchronizationFailure(failure, failureEvent);
            CompleteSynchronization(scope, outcome);
            if (IsCurrent(scope) && outcome is JournalSynchronizationState.Failed or JournalSynchronizationState.Offline)
                onSyncFailure?.Invoke();
        }
    }

    private async Task ApplyRemoteNotesAsync(JournalScope scope, IEnumerable<JournalNoteDto> remote, CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            Check(scope); var entries = await ReadAsync(scope, ct); var changed = false;
            foreach (var note in remote.Where(x => x.TripId == scope.TripId))
            {
                var index = entries.FindIndex(x => !x.IsFree && x.Id == note.ActivityId);
                if (index < 0) { entries.Add(new(note)); changed = true; }
                else if (entries[index].Pending is null && note.Revision >= entries[index].Note.Revision)
                {
                    var updated = entries[index] with { Note = note, Conflict = null };
                    if (updated != entries[index]) { entries[index] = updated; changed = true; }
                }
            }
            Check(scope); if (changed) await WriteAsync(scope, entries, ct);
        }
        finally { gate.Release(); }
    }

    private async Task ApplyNoteResultAsync(JournalScope scope, JournalMemory sent, JournalSaveResult result, CancellationToken ct)
    {
        if (result is null || result.Entry is null)
            throw new InvalidDataException("Journal acknowledgement unavailable.");
        await gate.WaitAsync(ct);
        try
        {
            Check(scope); var entries = await ReadAsync(scope, ct);
            var index = entries.FindIndex(x => x.Key == sent.Key);
            if (index < 0 || result.Entry.TripId != scope.TripId) return;
            var current = entries[index];
            if (current.Pending?.MutationId == sent.Pending!.MutationId)
                entries[index] = result.Saved ? current with { Note = result.Entry, Pending = null, Conflict = null,
                    Acknowledgement = new(sent.Pending.MutationId, result.Entry.Revision) }
                    : current with { Conflict = result.Entry };
            else if (result.Saved && result.Entry.Revision >= current.Note.Revision)
            {
                // A later edit is still pending. Rebase only over our acknowledged predecessor;
                // its mutation ID/text/photos remain untouched and it has never been sent yet.
                var pending = current.Pending is { } newer && newer.ExpectedRevision == sent.Pending.ExpectedRevision
                    ? newer with { ExpectedRevision = result.Entry.Revision } : current.Pending;
                entries[index] = current with { Note = result.Entry, Pending = pending,
                    Acknowledgement = new(sent.Pending.MutationId, result.Entry.Revision) };
            }
            else return;
            Check(scope); await WriteAsync(scope, entries, ct);
        }
        finally { gate.Release(); }
    }

    public Task SaveAsync(JournalScope scope, JournalMemory entry, string text, CancellationToken ct = default,
        IReadOnlyList<JournalMemory>? knownConfirmations = null) =>
        SaveConfirmedAsync(scope, entry, text, ct, knownConfirmations);

    public async Task<JournalMemory> SaveConfirmedAsync(JournalScope scope, JournalMemory entry, string text, CancellationToken ct = default,
        IReadOnlyList<JournalMemory>? knownConfirmations = null)
    {
#if ANDROID
        using var measurement = MobileOperationMeasurement.Start("journal_save_measured");
#endif
        CheckEntry(scope, entry);
        if (entry.IsFree) return await SaveFreeLocalAsync(scope, entry, text, ct, knownConfirmations);
        if (text.Length > 2000) throw new ArgumentException("La nota admite hasta 2000 caracteres.");
        await gate.WaitAsync(ct);
        try
        {
            CheckEntry(scope, entry);
            var entries = await ReadAsync(scope, ct);
            var index = entries.FindIndex(x => x.Key == entry.Key);
            var current = index >= 0 ? entries[index] : entry;
            // Advance only over an exact acknowledgement of this editor's own save.
            // A newer revision from another writer remains a real conflict.
            var expectedRevision = ExpectedSaveRevision(entry, current, knownConfirmations);
            if (string.IsNullOrWhiteSpace(text) && entry.Images.Length == 0) throw new ArgumentException(JournalText.Get("JournalEmptyValidation"));
            var updated = current with { Pending = new(text.Trim(), expectedRevision, Guid.NewGuid()), IsDraft = false,
                Photos = entry.Photos, CoverId = entry.CoverId };
            if (index < 0) entries.Add(updated); else entries[index] = updated;
            Check(scope);
            await WriteAsync(scope, entries, ct);
            await DeleteUnreferencedPhotosAsync(scope, current.Images, entries, ct);
            return updated;
        }
        finally { gate.Release(); }
    }

    // Called under the local gate, so an arriving acknowledgement and a new edit
    // cannot change the persisted revision between this check and the write.
    private static int ExpectedSaveRevision(JournalMemory edited, JournalMemory current,
        IReadOnlyList<JournalMemory>? knownConfirmations)
    {
        if (knownConfirmations is null || current.Revision <= edited.Revision || current.HasConflict
            || current.Deleted || current.IsDraft || current.DeletePending is not null) return edited.Revision;
        foreach (var known in knownConfirmations)
        {
            if (known.Key != current.Key || known.Note.TripId != current.Note.TripId
                || known.IsDraft || known.DeletePending is not null) continue;
            if (current.FreeEntry is { } free && known.FreePending is { } freeSave
                && IsOwnAcknowledgement(current, freeSave.MutationId, freeSave.ExpectedRevision)
                && freeSave.Notes == free.Notes && freeSave.Title == free.Title
                && freeSave.Place == free.Place && freeSave.Date == free.Date)
                return free.Revision;
            if (!current.IsFree && known.Pending is { } activitySave
                && IsOwnAcknowledgement(current, activitySave.MutationId, activitySave.ExpectedRevision)
                && activitySave.Notes == current.Note.Notes)
                return current.Note.Revision;
        }
        return edited.Revision;
    }

    private static bool IsOwnAcknowledgement(JournalMemory current, Guid mutationId, int expectedRevision) =>
        current.Acknowledgement is { } acknowledgement
            ? acknowledgement.MutationId == mutationId && acknowledgement.Revision == current.Revision
            : (long)expectedRevision + 1 == current.Revision;

    public async Task ResolveAsync(JournalScope scope, JournalMemory entry, bool useLocal)
    {
        if (entry.IsFree) { await ResolveFreeAsync(scope, entry, useLocal); return; }
        await gate.WaitAsync();
        try
        {
            Check(scope);
            var entries = await ReadAsync(scope, default);
            var i = entries.FindIndex(x => x.Note.ActivityId == entry.Note.ActivityId);
            if (i < 0 || entries[i].Conflict is not { } server) return;
            entries[i] = entries[i] with { Note = server, Conflict = null,
                Pending = useLocal ? new(entries[i].Text, server.Revision, Guid.NewGuid()) : null };
            await WriteAsync(scope, entries, default);
        }
        finally { gate.Release(); }
    }

    public async Task AddPhotosAsync(JournalScope scope, JournalMemory entry, IEnumerable<FileResult> files)
    {
        await AddPhotoBatchAsync(scope, entry, files, false);
    }

    // Attaching from the reader changes only local photo metadata, never confirmed notes or sync mutations.
    public Task<JournalMemory> AddConfirmedPhotosAsync(JournalScope scope, JournalMemory entry, IEnumerable<FileResult> files) =>
        AddPhotoBatchAsync(scope, entry, files, true);

    private async Task<JournalMemory> AddPhotoBatchAsync(JournalScope scope, JournalMemory entry,
        IEnumerable<FileResult> files, bool requireConfirmed)
    {
        Check(scope);
        if (entry.Note.TripId != scope.TripId || (entry.FreeEntry is { } free && free.TripId != scope.TripId))
            throw new OperationCanceledException();
        var normalized = new List<JournalImageData>();
        foreach (var file in files.Take(Math.Max(0, 10 - entry.Images.Length)))
        {
            Check(scope);
            await using var input = await file.OpenReadAsync();
            normalized.Add(await JournalMedia.NormalizeAsync(input));
            Check(scope);
        }
        if (normalized.Count == 0) return entry;
        await gate.WaitAsync();
        var created = new List<Guid>();
        List<JournalDraft>? previousDrafts = null;
        var draftWritten = false;
        try
        {
            Check(scope);
            var entries = (await ReadAsync(scope, default)).ToList();
            Check(scope);
            var index = entries.FindIndex(x => x.Key == entry.Key);
            var current = index >= 0 ? entries[index] : entry;
            if (current.Deleted || current.DeletePending is not null ||
                (requireConfirmed && (index < 0 || current.IsDraft)))
                throw new InvalidOperationException(JournalText.Get("JournalDeleted"));
            var drafts = requireConfirmed ? (await ReadDraftsAsync(scope, default)).ToList() : [];
            Check(scope);
            var draftIndex = drafts.FindIndex(x => x.Memory.Key == current.Key);
            var remaining = Math.Max(0, 10 - current.Images.Length);
            if (draftIndex >= 0)
            {
                var draftRemaining = Math.Max(0, 10 - drafts[draftIndex].Memory.Images.Length);
                if (draftRemaining == 0) throw new InvalidOperationException(JournalText.Get("JournalDraftPhotoLimit"));
                remaining = Math.Min(remaining, draftRemaining);
            }
            var updated = current;
            foreach (var photo in normalized.Take(remaining))
            {
                Check(scope);
                var id = Guid.NewGuid();
                created.Add(id);
                await SavePhotoPayloadAsync(scope, id, photo);
                updated = updated with { Photos = [..updated.Images, new(id)], CoverId = updated.CoverId ?? id };
                Check(scope);
            }
            if (created.Count == 0) return current;
            if (index < 0) entries.Add(updated); else entries[index] = updated;
            if (draftIndex >= 0)
            {
                previousDrafts = drafts.ToList();
                var draft = drafts[draftIndex];
                var appended = updated.Images.Where(x => created.Contains(x.Id)).ToArray();
                drafts[draftIndex] = draft with { Memory = draft.Memory with
                    { Photos = [..draft.Memory.Images, ..appended], CoverId = draft.Memory.CoverId ?? draft.Memory.Images.FirstOrDefault()?.Id ?? appended[0].Id } };
                Check(scope);
                await cache.SaveAsync(Prefix(scope) + "drafts", drafts);
                draftWritten = true;
            }
            Check(scope);
            await WriteAsync(scope, entries, default);
            return updated;
        }
        catch (Exception exception)
        {
            if (draftWritten)
            {
                try { await cache.SaveAsync(Prefix(scope) + "drafts", previousDrafts!); }
                catch (Exception rollback)
                {
                    // Retain payloads referenced by the recoverable draft if disk failure prevents rollback.
                    throw new AggregateException(exception, rollback);
                }
            }
            foreach (var id in created) await DeletePhotoPayloadAsync(scope, id);
            throw;
        }
        finally { gate.Release(); }
    }

    public async Task ChangePhotoAsync(JournalScope scope, Guid activityId, Guid photoId, bool remove, bool isFree = false)
    {
        await gate.WaitAsync();
        try
        {
            Check(scope);
            var entries = await ReadAsync(scope, default);
            var i = entries.FindIndex(x => x.Id == activityId && x.IsFree == isFree);
            if (i < 0 || !entries[i].Images.Any(x => x.Id == photoId)) return;
            var photos = remove ? entries[i].Images.Where(x => x.Id != photoId).ToArray() : entries[i].Images;
            entries[i] = entries[i] with { Photos = photos, CoverId = remove
                ? (entries[i].CoverId == photoId ? photos.FirstOrDefault()?.Id : entries[i].CoverId) : photoId };
            await WriteAsync(scope, entries, default);
            if (remove) await DeleteUnreferencedPhotosAsync(scope, [new(photoId)], entries, default);
        }
        finally { gate.Release(); }
    }

    private static string PhotoPayloadKey(JournalScope scope, Guid id) => Prefix(scope) + "photo-" + id;
    private static string ThumbnailPayloadKey(JournalScope scope, Guid id) => Prefix(scope) + "thumbnail-" + id;

    private async Task SavePhotoPayloadAsync(JournalScope scope, Guid id, JournalImageData data, CancellationToken ct = default)
    {
        Check(scope);
        await cache.SaveAsync(PhotoPayloadKey(scope, id), new JournalPhotoPayload(data.Image), ct);
        Check(scope);
        await cache.SaveAsync(ThumbnailPayloadKey(scope, id), new JournalPhotoPayload(data.Thumbnail), ct);
        Check(scope);
    }

    private async Task DeletePhotoPayloadAsync(JournalScope scope, Guid id)
    {
        await cache.DeleteAsync(PhotoPayloadKey(scope, id));
        await cache.DeleteAsync(ThumbnailPayloadKey(scope, id));
        await cache.DeleteAsync(Prefix(scope) + id); // Legacy combined payload.
    }

    public async Task<byte[]?> PhotoAsync(JournalScope scope, Guid id, bool thumbnail = false, CancellationToken ct = default)
    {
#if ANDROID
        using var measurement = MobileOperationMeasurement.Start(thumbnail ? "journal_thumbnail_measured" : "journal_photo_measured");
#endif
        Check(scope);
        var key = thumbnail ? ThumbnailPayloadKey(scope, id) : PhotoPayloadKey(scope, id);
        var separate = (await cache.GetAsync<JournalPhotoPayload>(key, cancellationToken: ct))?.Value;
        ct.ThrowIfCancellationRequested();
        Check(scope);
        if (separate is not null) return separate.Bytes;
        var legacy = (await cache.GetAsync<JournalImageData>(Prefix(scope) + id, cancellationToken: ct))?.Value;
        ct.ThrowIfCancellationRequested(); Check(scope);
        if (legacy is null) return null;
        // Migrate on first access, keeping the old file until both encrypted parts are durable.
        // Recheck references under the local gate so a simultaneous deletion cannot resurrect it.
        await gate.WaitAsync(ct);
        try
        {
            Check(scope);
            var entries = await ReadAsync(scope, ct); var drafts = await ReadDraftsAsync(scope, ct);
            Check(scope);
            if (!entries.Concat(drafts.Select(x => x.Memory)).Any(x => x.Images.Any(p => p.Id == id))) return null;
            try
            {
                await SavePhotoPayloadAsync(scope, id, legacy, ct);
                await cache.DeleteAsync(Prefix(scope) + id);
            }
            catch (OperationCanceledException) { throw; }
            catch (IOException)
            {
                // Disk exhaustion must not make an existing photo unreadable.
                await cache.DeleteAsync(PhotoPayloadKey(scope, id));
                await cache.DeleteAsync(ThumbnailPayloadKey(scope, id));
            }
            ct.ThrowIfCancellationRequested(); Check(scope);
            return thumbnail ? legacy.Thumbnail : legacy.Image;
        }
        finally { gate.Release(); }
    }

    public async Task DeleteAccountAsync(Guid user)
    {
        await gate.WaitAsync();
        try { deletedUsers.Add(user); await cache.DeleteByPrefixAsync($"personal-journal-{user}-"); }
        finally { gate.Release(); }
    }

    // Called only after the API has verified and linked the anonymous trip to this account.
    public async Task TransferLinkedTripAsync(AuthSessionDto verified)
    {
        if (!sessions.HasSession || sessions.CurrentTripId is null || verified.TripId != sessions.CurrentTripId
            || verified.LinkedFromUserId != sessions.CurrentUserId
            || verified.UserId == sessions.CurrentUserId) return;
        var source = Scope();
        var target = source with { UserId = verified.UserId };
        await gate.WaitAsync();
        try
        {
            Check(source);
            var memories = await ReadAsync(source, default);
            var drafts = await ReadDraftsAsync(source, default);
            foreach (var photo in memories.Concat(drafts.Select(x => x.Memory)).SelectMany(x => x.Images).DistinctBy(x => x.Id))
            {
                var image = await cache.GetAsync<JournalPhotoPayload>(PhotoPayloadKey(source, photo.Id));
                var thumbnail = await cache.GetAsync<JournalPhotoPayload>(ThumbnailPayloadKey(source, photo.Id));
                var legacy = image is null || thumbnail is null ? await cache.GetAsync<JournalImageData>(Prefix(source) + photo.Id) : null;
                Check(source);
                if (image is not null || legacy is not null)
                    await cache.SaveAsync(PhotoPayloadKey(target, photo.Id), image?.Value ?? new JournalPhotoPayload(legacy!.Value.Image));
                if (thumbnail is not null || legacy is not null)
                    await cache.SaveAsync(ThumbnailPayloadKey(target, photo.Id), thumbnail?.Value ?? new JournalPhotoPayload(legacy!.Value.Thumbnail));
                Check(source);
            }
            if (memories.Count > 0) await WriteAsync(target, memories, default);
            if (drafts.Count > 0) await cache.SaveAsync(Prefix(target) + "drafts", drafts);
            Check(source);
            await cache.DeleteByPrefixAsync(Prefix(source));
        }
        finally { gate.Release(); }
    }
    public async Task DeleteTripAsync(Guid user, Guid trip)
    {
        await gate.WaitAsync();
        try { deletedTrips.Add((user, trip)); await cache.DeleteByPrefixAsync($"personal-journal-{user}-{trip}-"); }
        finally { gate.Release(); }
    }
}

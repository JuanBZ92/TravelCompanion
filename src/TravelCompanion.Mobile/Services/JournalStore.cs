using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Mobile.Services;

public sealed record JournalPhoto(Guid Id);
public sealed record JournalMemory(JournalNoteDto Note, SaveJournalNoteRequest? Pending = null,
    JournalNoteDto? Conflict = null, JournalPhoto[]? Photos = null, Guid? CoverId = null,
    JournalFreeEntryDto? FreeEntry = null, SaveJournalFreeEntryRequest? FreePending = null,
    JournalFreeEntryDto? FreeConflict = null, DeleteJournalFreeEntryRequest? DeletePending = null,
    bool IsDraft = false)
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
    private static string Prefix(JournalScope s) => $"personal-journal-{s.UserId}-{s.TripId}-";
    private async Task<List<JournalMemory>> ReadAsync(JournalScope s, CancellationToken ct) =>
        (await cache.GetAsync<List<JournalMemory>>(Prefix(s) + "index", cancellationToken: ct))?.Value ?? [];
    private Task WriteAsync(JournalScope s, List<JournalMemory> entries, CancellationToken ct) => cache.SaveAsync(Prefix(s) + "index", entries, ct);

    public async Task ReplayPendingAsync(CancellationToken ct)
    {
        var scope = Scope();
        if ((await ReadAsync(scope, ct)).Any(x => !x.IsDraft && !x.HasConflict
            && (x.Pending is not null || x.FreePending is not null || x.DeletePending is not null)))
            await LoadAsync(scope, [], true, ct);
    }

    public async Task<List<JournalMemory>> LoadAsync(JournalScope scope, IEnumerable<ScheduleItemDto> items, bool sync, CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            Check(scope);
            var entries = await ReadAsync(scope, ct);
            var currentItems = items.ToDictionary(x => x.Id);
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
            if (sync)
            {
                var token = await sessions.GetTokenAsync();
                Check(scope);
                if (!string.IsNullOrEmpty(token))
                {
                    try
                    {
                        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                        timeout.CancelAfter(TimeSpan.FromSeconds(12));
                        var remote = await api.GetJournalAsync(token, scope.TripId, timeout.Token);
                        Check(scope);
                        foreach (var note in remote)
                        {
                            var index = entries.FindIndex(x => x.Note.ActivityId == note.ActivityId);
                            if (index < 0) entries.Add(new(note));
                            else if (entries[index].Pending is null) entries[index] = entries[index] with { Note = note, Conflict = null };
                        }
                        for (var i = 0; i < entries.Count; i++)
                        {
                            if (entries[i].IsFree || entries[i].IsDraft || entries[i].Pending is not { } pending || entries[i].Conflict is not null) continue;
                            var result = await api.SaveJournalAsync(token, scope.TripId, entries[i].Note.ActivityId, pending, timeout.Token);
                            Check(scope);
                            entries[i] = result.Saved ? entries[i] with { Note = result.Entry, Pending = null, Conflict = null }
                                : entries[i] with { Conflict = result.Entry };
                            await WriteAsync(scope, entries, ct);
                        }
                        await SyncFreeAsync(scope, entries, token, timeout.Token);
                    }
                    catch (HttpRequestException) { /* Keep both the local draft and its mutation ID for retry. */ }
                    catch (IOException) { /* A dropped response must not discard confirmed local content. */ }
#if ANDROID
                    catch (Java.IO.IOException) { /* Android's HTTP handler can expose the native exception directly. */ }
#endif
                    catch (TaskCanceledException) when (!ct.IsCancellationRequested) { }
                }
            }
            Check(scope);
            if (sync || changed) await WriteAsync(scope, entries, ct);
            return entries.Where(x => !x.Deleted).OrderBy(x => x.Date).ThenBy(x => x.Title).ThenBy(x => x.Key).ToList();
        }
        finally { gate.Release(); }
    }

    public async Task SaveAsync(JournalScope scope, JournalMemory entry, string text, CancellationToken ct = default)
    {
        if (entry.IsFree) { await SaveFreeLocalAsync(scope, entry, text, ct); return; }
        if (text.Length > 2000) throw new ArgumentException("La nota admite hasta 2000 caracteres.");
        await gate.WaitAsync(ct);
        try
        {
            Check(scope);
            var entries = await ReadAsync(scope, ct);
            var index = entries.FindIndex(x => x.Key == entry.Key);
            var current = index >= 0 ? entries[index] : entry;
            // The editor's revision is used, never an unseen newer revision.
            if (string.IsNullOrWhiteSpace(text) && entry.Images.Length == 0) throw new ArgumentException(JournalText.Get("JournalEmptyValidation"));
            var updated = current with { Pending = new(text.Trim(), entry.Note.Revision, Guid.NewGuid()), Conflict = null, IsDraft = false,
                Photos = entry.Photos, CoverId = entry.CoverId };
            if (index < 0) entries.Add(updated); else entries[index] = updated;
            Check(scope);
            await WriteAsync(scope, entries, ct);
            await DeleteUnreferencedPhotosAsync(scope, current.Images, entries, ct);
        }
        finally { gate.Release(); }
    }

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
        foreach (var file in files)
        {
            Check(scope);
            await using var input = await file.OpenReadAsync();
            var photo = await JournalMedia.NormalizeAsync(input);
            await gate.WaitAsync();
            try
            {
                Check(scope);
                var entries = await ReadAsync(scope, default);
                var index = entries.FindIndex(x => x.Key == entry.Key);
                var current = index >= 0 ? entries[index] : entry;
                if (current.Images.Length >= 10) break;
                var id = Guid.NewGuid();
                await cache.SaveAsync(Prefix(scope) + id, photo);
                var updated = current with { Photos = [..current.Images, new(id)], CoverId = current.CoverId ?? id };
                if (index < 0) entries.Add(updated); else entries[index] = updated;
                await WriteAsync(scope, entries, default);
            }
            finally { gate.Release(); }
        }
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
            if (remove) await cache.DeleteAsync(Prefix(scope) + photoId);
        }
        finally { gate.Release(); }
    }

    public async Task<byte[]?> PhotoAsync(JournalScope scope, Guid id, bool thumbnail = false)
    {
        Check(scope);
        var data = (await cache.GetAsync<JournalImageData>(Prefix(scope) + id))?.Value;
        Check(scope);
        return thumbnail ? data?.Thumbnail : data?.Image;
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
                var payload = await cache.GetAsync<JournalImageData>(Prefix(source) + photo.Id);
                if (payload is not null) await cache.SaveAsync(Prefix(target) + photo.Id, payload.Value);
            }
            if (memories.Count > 0) await WriteAsync(target, memories, default);
            if (drafts.Count > 0) await cache.SaveAsync(Prefix(target) + "drafts", drafts);
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

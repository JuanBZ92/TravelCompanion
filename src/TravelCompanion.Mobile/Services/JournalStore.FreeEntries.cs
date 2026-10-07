using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Mobile.Services;

public sealed record JournalDraft(JournalMemory Memory, string Text, DateTimeOffset SavedAt);

public sealed partial class JournalStore
{
    private async Task<List<JournalDraft>> ReadDraftsAsync(JournalScope scope, CancellationToken ct) =>
        (await cache.GetAsync<List<JournalDraft>>(Prefix(scope) + "drafts", cancellationToken: ct))?.Value ?? [];

    public async Task<IReadOnlyList<JournalDraft>> DraftsAsync(JournalScope scope, CancellationToken ct = default)
    {
        Check(scope);
        var drafts = await ReadDraftsAsync(scope, ct);
        Check(scope);
        return drafts.OrderByDescending(x => x.SavedAt).ToList();
    }

    public async Task SaveDraftAsync(JournalScope scope, JournalMemory memory, string text, CancellationToken ct = default)
    {
        CheckEntry(scope, memory);
        ValidateContent(memory, text);
        await gate.WaitAsync(ct);
        try
        {
            Check(scope);
            var drafts = await ReadDraftsAsync(scope, ct);
            Check(scope);
            var previousPhotos = drafts.Where(x => x.Memory.Key == memory.Key).SelectMany(x => x.Memory.Images).ToArray();
            drafts.RemoveAll(x => x.Memory.Key == memory.Key);
            drafts.Add(new(memory, text, DateTimeOffset.UtcNow));
            await cache.SaveAsync(Prefix(scope) + "drafts", drafts, ct);
            if (previousPhotos.Length > 0)
                await DeleteUnreferencedPhotosAsync(scope, previousPhotos, await ReadAsync(scope, ct), ct);
        }
        finally { gate.Release(); }
    }

    public async Task DiscardDraftAsync(JournalScope scope, JournalMemory memory, CancellationToken ct = default)
    {
        await gate.WaitAsync(ct);
        try
        {
            Check(scope);
            var drafts = await ReadDraftsAsync(scope, ct);
            var removed = drafts.Where(x => x.Memory.Key == memory.Key).SelectMany(x => x.Memory.Images).ToArray();
            drafts.RemoveAll(x => x.Memory.Key == memory.Key);
            await cache.SaveAsync(Prefix(scope) + "drafts", drafts, ct);
            var entries = await ReadAsync(scope, ct);
            foreach (var photo in removed.Where(p => !entries.Any(e => e.Images.Any(x => x.Id == p.Id))))
                await DeletePhotoPayloadAsync(scope, photo.Id);
            var unconfirmed = entries.FirstOrDefault(x => x.Key == memory.Key && x.IsDraft);
            if (unconfirmed is not null)
            {
                entries.Remove(unconfirmed);
                await WriteAsync(scope, entries, ct);
                foreach (var photo in unconfirmed.Images) await DeletePhotoPayloadAsync(scope, photo.Id);
            }
        }
        finally { gate.Release(); }
    }

    private static void ValidateContent(JournalMemory memory, string text)
    {
        if (text.Length > 2000 || memory.Title.Length > 120 || memory.City.Length > 200 || memory.Date == default)
            throw new ArgumentException("Invalid journal draft.");
    }

    private async Task SaveFreeLocalAsync(JournalScope scope, JournalMemory entry, string text, CancellationToken ct)
    {
        CheckEntry(scope, entry);
        ValidateContent(entry, text);
        await gate.WaitAsync(ct);
        try
        {
            Check(scope);
            var entries = await ReadAsync(scope, ct);
            var index = entries.FindIndex(x => x.Key == entry.Key);
            var current = index < 0 ? entry : entries[index];
            if (current.Deleted) throw new InvalidOperationException(JournalText.Get("JournalDeleted"));
            if (string.IsNullOrWhiteSpace(text) && entry.Images.Length == 0)
                throw new ArgumentException(JournalText.Get("JournalEmptyValidation"));
            var saved = current with { FreePending = new(entry.Title.Trim(), entry.City.Trim(), entry.Date,
                text.Trim(), entry.Revision, Guid.NewGuid()), FreeConflict = null, IsDraft = false,
                Photos = entry.Photos, CoverId = entry.CoverId };
            if (index < 0) entries.Add(saved); else entries[index] = saved;
            Check(scope);
            await WriteAsync(scope, entries, ct);
            await DeleteUnreferencedPhotosAsync(scope, current.Images, entries, ct);
        }
        finally { gate.Release(); }
    }

    public async Task DeleteFreeLocalAsync(JournalScope scope, JournalMemory entry)
    {
        CheckEntry(scope, entry);
        if (!entry.IsFree) throw new ArgumentException("Only free entries can be deleted here.");
        await gate.WaitAsync();
        try
        {
            Check(scope);
            var entries = await ReadAsync(scope, default);
            Check(scope);
            var i = entries.FindIndex(x => x.Key == entry.Key);
            if (i < 0) return;
            entries[i] = entries[i] with { DeletePending = new(entry.Revision, Guid.NewGuid()), FreePending = null, FreeConflict = null };
            await WriteAsync(scope, entries, default);
        }
        finally { gate.Release(); }
    }

    private async Task SyncFreeAsync(JournalScope scope, string token, CancellationToken networkCt, CancellationToken localCt)
    {
        var beforeRead = (await ReadLocalSnapshotAsync(scope, localCt)).Where(x => x.IsFree).ToDictionary(x => x.Key);
        var remote = await api.GetJournalFreeAsync(token, scope.TripId, networkCt);
        await gate.WaitAsync(localCt);
        try
        {
            Check(scope); var entries = await ReadAsync(scope, localCt); var changed = false;
            var removedPhotos = new List<JournalPhoto>();
            foreach (var note in remote.Where(x => x.TripId == scope.TripId))
            {
                var i = entries.FindIndex(x => x.IsFree && x.Id == note.Id);
                if (i < 0) { entries.Add(JournalMemory.FromFree(note)); changed = true; continue; }
                var current = entries[i];
                if (note.Revision < current.Revision) continue;
                if (note.Deleted)
                {
                    var original = beforeRead.GetValueOrDefault(current.Key);
                    var editedDuringRead = original is null || current.FreePending?.MutationId != original.FreePending?.MutationId
                        || current.DeletePending?.MutationId != original.DeletePending?.MutationId
                        || !current.Images.SequenceEqual(original.Images) || current.CoverId != original.CoverId;
                    // Preserve all unacknowledged writing, including edits saved offline before GET.
                    // The tombstone remains authoritative and explicit resolution cannot resurrect it.
                    if (current.FreePending is not null || editedDuringRead || current.FreeConflict?.Deleted == true)
                        entries[i] = current with { FreeConflict = current.FreeConflict is { } conflict && conflict.Revision > note.Revision ? conflict : note };
                    else
                    {
                        removedPhotos.AddRange(current.Images);
                        entries[i] = current with { FreeEntry = note, FreePending = null, DeletePending = null,
                            FreeConflict = null, Photos = [], CoverId = null };
                    }
                }
                else if (current.FreePending is null && current.DeletePending is null && !current.HasConflict)
                    entries[i] = current with { FreeEntry = note, FreeConflict = null };
                if (entries[i] != current) changed = true;
            }
            Check(scope);
            if (changed) await WriteAsync(scope, entries, localCt);
            if (removedPhotos.Count > 0) await DeleteUnreferencedPhotosAsync(scope, removedPhotos, entries, localCt);
        }
        finally { gate.Release(); }
        var snapshot = await ReadLocalSnapshotAsync(scope, localCt);
        foreach (var entry in snapshot.Where(x => x.IsFree && !x.IsDraft && !x.HasConflict))
        {
            JournalFreeSaveResult? result = null;
            if (entry.DeletePending is { } deletion)
                result = await api.DeleteJournalFreeAsync(token, scope.TripId, entry.Id, deletion, networkCt);
            else if (entry.FreePending is { } pending)
                result = await api.SaveJournalFreeAsync(token, scope.TripId, entry.Id, pending, networkCt);
            if (result is null) continue;
            await gate.WaitAsync(localCt);
            try
            {
                Check(scope); var entries = await ReadAsync(scope, localCt);
                var i = entries.FindIndex(x => x.Key == entry.Key);
                if (i < 0 || result.Entry.TripId != scope.TripId) continue;
                var current = entries[i];
                var sameMutation = entry.DeletePending is { } sentDeletion
                    ? current.DeletePending?.MutationId == sentDeletion.MutationId
                    : current.FreePending?.MutationId == entry.FreePending!.MutationId;
                var accepted = result.Saved || entry.DeletePending is not null && result.Entry.Deleted;
                if (sameMutation)
                    entries[i] = accepted
                        ? current with { FreeEntry = result.Entry, FreePending = null, DeletePending = null, FreeConflict = null }
                        : current with { FreeConflict = result.Entry };
                else if (result.Entry.Deleted)
                    entries[i] = current with { FreeConflict = result.Entry };
                else if (result.Saved && result.Entry.Revision >= current.Revision)
                {
                    var oldRevision = entry.FreePending?.ExpectedRevision ?? entry.DeletePending!.ExpectedRevision;
                    entries[i] = current with { FreeEntry = result.Entry,
                        FreePending = current.FreePending is { } newer && newer.ExpectedRevision == oldRevision
                            ? newer with { ExpectedRevision = result.Entry.Revision } : current.FreePending,
                        DeletePending = current.DeletePending is { } nextDelete && nextDelete.ExpectedRevision == oldRevision
                            ? nextDelete with { ExpectedRevision = result.Entry.Revision } : current.DeletePending };
                }
                else continue;
                var deleted = sameMutation && accepted && result.Entry.Deleted;
                if (deleted) entries[i] = entries[i] with { Photos = [], CoverId = null };
                Check(scope); await WriteAsync(scope, entries, localCt);
                if (deleted) await DeleteUnreferencedPhotosAsync(scope, current.Images, entries, localCt);
            }
            finally { gate.Release(); }
        }
    }

    private async Task ResolveFreeAsync(JournalScope scope, JournalMemory entry, bool useLocal)
    {
        await gate.WaitAsync();
        try
        {
            Check(scope);
            var entries = await ReadAsync(scope, default);
            var i = entries.FindIndex(x => x.Key == entry.Key);
            if (i < 0 || entries[i].FreeConflict is not { } server) return;
            var current = entries[i];
            entries[i] = current with { FreeEntry = server, FreeConflict = null,
                FreePending = useLocal && !server.Deleted && current.DeletePending is null
                    ? new(current.Title, current.City, current.Date, current.Text, server.Revision, Guid.NewGuid()) : null,
                DeletePending = useLocal && !server.Deleted && current.DeletePending is not null
                    ? new(server.Revision, Guid.NewGuid()) : null,
                Photos = server.Deleted ? [] : current.Photos, CoverId = server.Deleted ? null : current.CoverId };
            await WriteAsync(scope, entries, default);
            if (server.Deleted) await DeleteUnreferencedPhotosAsync(scope, current.Images, entries, default);
        }
        finally { gate.Release(); }
    }

    private async Task DeleteUnreferencedPhotosAsync(JournalScope scope, IEnumerable<JournalPhoto> candidates,
        List<JournalMemory> entries, CancellationToken ct)
    {
        var drafts = await ReadDraftsAsync(scope, ct);
        var retained = entries.Concat(drafts.Select(x => x.Memory)).SelectMany(x => x.Images).Select(x => x.Id).ToHashSet();
        Check(scope);
        foreach (var photo in candidates.Where(x => !retained.Contains(x.Id))) await DeletePhotoPayloadAsync(scope, photo.Id);
    }
}

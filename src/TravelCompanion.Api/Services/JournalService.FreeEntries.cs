using Microsoft.EntityFrameworkCore;
using TravelCompanion.Api.Data;
using TravelCompanion.Api.Models;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Api.Services;

public sealed partial class JournalService
{
    public async Task<IReadOnlyList<JournalFreeEntryDto>> ListFreeAsync(HttpContext context, Guid tripId, CancellationToken ct)
    {
        var trip = await AuthorizeAsync(context, tripId, ct);
        // Tombstones are returned so an offline device can remove stale copies.
        return (await db.JournalFreeEntries.AsNoTracking()
            .Where(x => x.UserId == trip.AppUserId && x.TripId == tripId)
            .OrderBy(x => x.Date).ThenBy(x => x.Id).ToListAsync(ct)).Select(ToFreeDto).ToArray();
    }

    public Task<JournalFreeSaveResult> SaveFreeAsync(HttpContext context, Guid tripId, Guid id,
        SaveJournalFreeEntryRequest request, CancellationToken ct)
    {
        if (request.Title is null || request.Title.Length > 120 || request.Place is null || request.Place.Length > 200
            || request.Notes is null || request.Notes.Length > 2000 || request.Date == default)
            throw new ArgumentException("Invalid journal entry.");
        return MutateFreeAsync(context, tripId, id, request.ExpectedRevision, request.MutationId, request, ct);
    }

    public Task<JournalFreeSaveResult> DeleteFreeAsync(HttpContext context, Guid tripId, Guid id,
        DeleteJournalFreeEntryRequest request, CancellationToken ct) =>
        MutateFreeAsync(context, tripId, id, request.ExpectedRevision, request.MutationId, null, ct);

    private async Task<JournalFreeSaveResult> MutateFreeAsync(HttpContext context, Guid tripId, Guid id,
        int revision, Guid mutation, SaveJournalFreeEntryRequest? request, CancellationToken ct)
    {
        if (id == Guid.Empty || mutation == Guid.Empty || revision < 0) throw new ArgumentException("Invalid mutation.");
        if (DbExecutionStrategy.ShouldExecute(db))
            return await DbExecutionStrategy.ExecuteAsync(db,
                () => MutateFreeAsync(context, tripId, id, revision, mutation, request, ct), ct);
        var trip = await AuthorizeAsync(context, tripId, ct);
        var owner = trip.AppUserId;
        await using var transaction = db.Database.IsRelational() ? await db.Database.BeginTransactionAsync(ct) : null;
        await TripConcurrencyLock.LockAsync(db, tripId, ct);
        await db.Entry(trip).ReloadAsync(ct);
        if (trip.AppUserId != owner || trip.PublicationStatus != TripPublicationStatus.Published
            || !await db.AppUsers.AnyAsync(x => x.Id == owner && x.DeletedAtUtc == null, ct))
            throw new UnauthorizedAccessException();
        var entry = await db.JournalFreeEntries.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (entry is not null && (entry.UserId != owner || entry.TripId != tripId)) throw new UnauthorizedAccessException();
        if (entry?.MutationId == mutation) return new(true, ToFreeDto(entry));
        // A deleted identifier can never be recreated by delayed writes.
        if (entry is not null && (entry.Deleted || entry.Revision != revision)) return new(false, ToFreeDto(entry));
        if (entry is null)
        {
            if (revision != 0) throw new KeyNotFoundException();
            entry = new() { Id = id, TripId = tripId, UserId = owner!.Value };
            db.JournalFreeEntries.Add(entry);
        }
        entry.Title = request?.Title.Trim() ?? "";
        entry.Place = request?.Place.Trim() ?? "";
        entry.Date = request?.Date ?? (entry.Date == default ? DateOnly.FromDateTime(DateTime.UtcNow) : entry.Date);
        entry.Notes = request?.Notes.Trim() ?? "";
        entry.Deleted = request is null;
        entry.Revision++;
        entry.MutationId = mutation;
        // PostgreSQL stores microseconds; return exactly the timestamp a retry will read.
        var now = DateTimeOffset.UtcNow;
        entry.UpdatedAt = now.AddTicks(-(now.Ticks % 10));
        await db.SaveChangesAsync(ct);
        if (transaction is not null) await transaction.CommitAsync(ct);
        return new(true, ToFreeDto(entry));
    }

    private static JournalFreeEntryDto ToFreeDto(JournalFreeEntry entry) => new(entry.Id, entry.TripId,
        entry.Title, entry.Place, entry.Date, entry.Notes, entry.Revision, entry.UpdatedAt, entry.Deleted);
}

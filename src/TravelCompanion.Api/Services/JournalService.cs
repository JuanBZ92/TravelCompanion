using Microsoft.EntityFrameworkCore;
using TravelCompanion.Api.Data;
using TravelCompanion.Api.Models;
using TravelCompanion.Shared;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Api.Services;

public sealed partial class JournalService(TravelCompanionDbContext db, UserSessionService sessions)
{
    private async Task<Trip> AuthorizeAsync(HttpContext context, Guid tripId, CancellationToken ct)
    {
        var session = await sessions.GetSessionContextAsync(context, ct) ?? throw new UnauthorizedAccessException();
        if (session.TripId != tripId || session.User.DeletedAtUtc.HasValue) throw new UnauthorizedAccessException();
        return await db.Trips.SingleOrDefaultAsync(t => t.Id == tripId && t.AppUserId == session.User.Id
            && t.PublicationStatus == TripPublicationStatus.Published, ct) ?? throw new UnauthorizedAccessException();
    }

    public async Task<IReadOnlyList<JournalNoteDto>> ListAsync(HttpContext context, Guid tripId, CancellationToken ct)
    {
        var trip = await AuthorizeAsync(context, tripId, ct);
        return (await db.JournalNotes.AsNoTracking().Where(x => x.TripId == tripId && x.UserId == trip.AppUserId)
            .OrderBy(x => x.Date).ThenBy(x => x.Title).ToListAsync(ct)).Select(ToDto).ToArray();
    }

    public async Task<JournalSaveResult> SaveAsync(HttpContext context, Guid tripId, Guid activityId,
        SaveJournalNoteRequest request, CancellationToken ct)
    {
        if (request.MutationId == Guid.Empty || request.ExpectedRevision < 0 || request.Notes is null || request.Notes.Length > 2000)
            throw new ArgumentException("La nota o su revisión no son válidas.");
        if (DbExecutionStrategy.ShouldExecute(db))
            return await DbExecutionStrategy.ExecuteAsync(db, () => SaveAsync(context, tripId, activityId, request, ct), ct);
        // Authorize before locking; reload after the lock to use the current itinerary revision.
        var trip = await AuthorizeAsync(context, tripId, ct);
        var ownerId = trip.AppUserId;
        await using var transaction = db.Database.IsRelational() ? await db.Database.BeginTransactionAsync(ct) : null;
        await TripConcurrencyLock.LockAsync(db, tripId, ct);
        await db.Entry(trip).ReloadAsync(ct);
        if (trip.AppUserId != ownerId || trip.PublicationStatus != TripPublicationStatus.Published
            || !await db.AppUsers.AnyAsync(x => x.Id == ownerId && x.DeletedAtUtc == null, ct))
            throw new UnauthorizedAccessException();
        var note = await db.JournalNotes.SingleOrDefaultAsync(x => x.UserId == trip.AppUserId
            && x.TripId == tripId && x.ActivityId == activityId, ct);
        if (note?.MutationId == request.MutationId) return new(true, ToDto(note));
        if (note is not null && note.Revision != request.ExpectedRevision) return new(false, ToDto(note));
        var activity = await db.Reservations.SingleOrDefaultAsync(x => x.TripId == tripId && x.Id == activityId, ct);
        if (note is null)
        {
            if (activity is null) throw new KeyNotFoundException("La actividad ya no está disponible.");
            note = Snapshot(trip, activity);
            if (request.ExpectedRevision != 0) return new(false, ToDto(note));
            db.JournalNotes.Add(note);
        }
        if (activity is not null)
        {
            note.Title = activity.Title;
            note.City = activity.City;
            note.Date = activity.Date;
            // Legacy clients still read personal notes here. Curated reservation copy is untouched.
            if (activity.Owner == ItineraryItemOwner.Traveler && activity.Notes != request.Notes.Trim())
            {
                activity.Notes = request.Notes.Trim();
                trip.PlanRevision++;
                trip.UpdatedAtUtc = DateTimeOffset.UtcNow;
            }
        }
        note.Notes = request.Notes.Trim();
        note.Revision++;
        note.MutationId = request.MutationId;
        note.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        if (transaction is not null) await transaction.CommitAsync(ct);
        return new(true, ToDto(note));
    }

    internal static JournalNote Snapshot(Trip trip, Reservation activity) => new()
    {
        Id = Guid.NewGuid(), UserId = trip.AppUserId!.Value, TripId = trip.Id, ActivityId = activity.Id,
        Title = activity.Title, City = activity.City, Date = activity.Date, UpdatedAt = DateTimeOffset.UtcNow
    };

    public static async Task SyncLegacyAsync(TravelCompanionDbContext db, Trip trip, Reservation activity, CancellationToken ct)
    {
        if (trip.AppUserId is null || activity.Owner != ItineraryItemOwner.Traveler) return;
        var recommendation = activity.Recommendation;
        if (recommendation is null && activity.RecommendationId.HasValue)
            recommendation = await db.Recommendations.AsNoTracking().SingleOrDefaultAsync(x => x.Id == activity.RecommendationId, ct);
        var text = activity.Notes.Trim();
        var note = await db.JournalNotes.SingleOrDefaultAsync(x => x.UserId == trip.AppUserId
            && x.TripId == trip.Id && x.ActivityId == activity.Id, ct);
        // Only classify unimported legacy text. A Journal note is explicitly personal,
        // even if the traveler chose words also present in the catalog description.
        if (note is null && (text.Equals("Guardado desde Travel Assistant.", StringComparison.OrdinalIgnoreCase)
            || text == recommendation?.Description?.Trim()
            || text == recommendation?.DescriptionEn?.Trim())) text = "";
        if (note is null && text.Length == 0) return;
        if (note is null) { note = Snapshot(trip, activity); db.JournalNotes.Add(note); }
        if (note.Notes != text || note.Revision == 0)
        {
            note.Notes = text; note.Revision++; note.MutationId = Guid.Empty; note.UpdatedAt = DateTimeOffset.UtcNow;
        }
        note.Title = activity.Title; note.City = activity.City; note.Date = activity.Date;
    }

    private static JournalNoteDto ToDto(JournalNote note) => new(note.ActivityId, note.TripId, note.Title,
        note.City, note.Date, note.Notes, note.Revision, note.UpdatedAt);
}

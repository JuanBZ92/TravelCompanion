using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TravelCompanion.Api.Data;
using TravelCompanion.Api.Models;
using TravelCompanion.Api.Services;
using TravelCompanion.Shared;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Api.Controllers;

[ApiController]
[Route("api/mobile/trips/{tripId:guid}/preparation")]
public sealed class TripPreparationController(TravelCompanionDbContext db, UserSessionService sessions) : ControllerBase
{
    private async Task<bool> CanAccessAsync(Guid tripId, CancellationToken ct)
    {
        var session = await sessions.GetSessionContextAsync(HttpContext, ct);
        return session is not null && session.TripId == tripId && session.User.DeletedAtUtc is null
            && await db.Trips.AnyAsync(t => t.Id == tripId && t.AppUserId == session.User.Id
                && t.AppUser != null && t.AppUser.DeletedAtUtc == null
                && t.PublicationStatus == TripPublicationStatus.Published, ct);
    }

    [HttpGet]
    public async Task<IActionResult> List(Guid tripId, CancellationToken ct)
    {
        if (!await CanAccessAsync(tripId, ct)) return Unauthorized();
        var saved = await db.TripPreparationItems.AsNoTracking().Where(x => x.TripId == tripId).ToListAsync(ct);
        return Ok(TripPreparationKeys.All.Select(key => saved.SingleOrDefault(x => x.Key == key) is { } item
            ? ToDto(item) : new TripPreparationItemDto(key, false, 0)));
    }

    [HttpPut("{key}")]
    public async Task<IActionResult> Save(Guid tripId, string key, SaveTripPreparationItemRequest request, CancellationToken ct)
    {
        if (!TripPreparationKeys.All.Contains(key) || request.ExpectedRevision < 0) return BadRequest();
        if (DbExecutionStrategy.ShouldExecute(db))
            return await DbExecutionStrategy.ExecuteAsync(db, () => Save(tripId, key, request, ct), ct);
        if (!await CanAccessAsync(tripId, ct)) return Unauthorized();
        await using var transaction = db.Database.IsRelational() ? await db.Database.BeginTransactionAsync(ct) : null;
        try { await TripConcurrencyLock.LockAsync(db, tripId, ct); }
        catch (KeyNotFoundException) { return NotFound(); }
        // Linking and deletion also lock the trip; reauthorize after acquiring the lock.
        db.ChangeTracker.Clear();
        if (!await CanAccessAsync(tripId, ct)) return Unauthorized();
        var item = await db.TripPreparationItems.SingleOrDefaultAsync(x => x.TripId == tripId && x.Key == key, ct);
        if (item is not null && item.Completed == request.Completed) return Ok(ToDto(item));
        if ((item?.Revision ?? 0) != request.ExpectedRevision)
            return Conflict(item is null ? new TripPreparationItemDto(key, false, 0) : ToDto(item));
        if (item is null)
        {
            item = new TripPreparationItem { TripId = tripId, Key = key };
            db.TripPreparationItems.Add(item);
        }
        item.Completed = request.Completed;
        item.Revision++;
        await db.SaveChangesAsync(ct);
        if (transaction is not null) await transaction.CommitAsync(ct);
        return Ok(ToDto(item));
    }

    private static TripPreparationItemDto ToDto(TripPreparationItem item) => new(item.Key, item.Completed, item.Revision);
}

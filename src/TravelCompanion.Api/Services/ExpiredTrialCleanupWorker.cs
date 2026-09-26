using Microsoft.EntityFrameworkCore;
using TravelCompanion.Api.Data;
using TravelCompanion.Shared;

namespace TravelCompanion.Api.Services;

public sealed class ExpiredTrialCleanupWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<ExpiredTrialCleanupWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
        using var timer = new PeriodicTimer(TimeSpan.FromHours(6));
        do
        {
            try
            {
                await PurgeAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Expired free trial cleanup failed.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    internal async Task<int> PurgeAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<TravelCompanionDbContext>();
        var now = DateTimeOffset.UtcNow;
        await using var transaction = dbContext.Database.IsRelational()
            ? await dbContext.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, cancellationToken)
            : null;
        var candidates = await dbContext.BuilderAccessGrants
            .Where(grant => grant.IsTrial && grant.FreePolicy == FreeAccessPolicy.TimedTrial
                && grant.ConvertedAtUtc == null
                && grant.TrialDraftExpiresAtUtc != null
                && grant.TrialDraftExpiresAtUtc <= now
                && grant.TripId != null)
            .Where(grant => !dbContext.StorePurchaseIntents.Any(intent => intent.TripId == grant.TripId
                && (intent.State == TravelCompanion.Shared.Dtos.PurchaseIntentState.Verifying
                    || intent.State == TravelCompanion.Shared.Dtos.PurchaseIntentState.Pending
                    || intent.State == TravelCompanion.Shared.Dtos.PurchaseIntentState.Active)))
            .ToListAsync(cancellationToken);
        if (candidates.Count == 0)
        {
            return 0;
        }

        var tripIds = candidates.Select(grant => grant.TripId!.Value).ToList();
        foreach (var tripId in tripIds.Order())
            await TripConcurrencyLock.LockAsync(dbContext, tripId, cancellationToken);
        dbContext.ChangeTracker.Clear();
        var grants = await dbContext.BuilderAccessGrants
            .Where(grant => tripIds.Contains(grant.TripId!.Value)
                && grant.IsTrial && grant.FreePolicy == FreeAccessPolicy.TimedTrial && grant.ConvertedAtUtc == null
                && grant.TrialDraftExpiresAtUtc != null && grant.TrialDraftExpiresAtUtc <= now)
            .ToListAsync(cancellationToken);
        tripIds = grants.Select(grant => grant.TripId!.Value).ToList();
        if (tripIds.Count == 0)
        {
            if (transaction is not null) await transaction.CommitAsync(cancellationToken);
            return 0;
        }
        var intents = await dbContext.StorePurchaseIntents
            .Where(item => tripIds.Contains(item.TripId)).ToListAsync(cancellationToken);
        var protectedTripIds = intents.Where(item => item.State is TravelCompanion.Shared.Dtos.PurchaseIntentState.Verifying
                or TravelCompanion.Shared.Dtos.PurchaseIntentState.Pending
                or TravelCompanion.Shared.Dtos.PurchaseIntentState.Active)
            .Select(item => item.TripId).ToHashSet();
        var retainedTripIds = intents.Select(item => item.TripId).ToHashSet();
        var trips = await dbContext.Trips
            .Include(item => item.Reservations)
            .Include(item => item.DayPlans).ThenInclude(item => item.Blocks)
            .Include(item => item.Documents)
            .Include(item => item.PlanDraft)
            .Include(item => item.ThematicRoutes).ThenInclude(item => item.Stops)
            .Where(item => tripIds.Contains(item.Id)).ToListAsync(cancellationToken);
        var reservationIds = await dbContext.Reservations
            .Where(item => tripIds.Contains(item.TripId))
            .Select(item => item.Id)
            .ToListAsync(cancellationToken);
        if (reservationIds.Count > 0)
        {
            var notifications = await dbContext.NotificationOutboxItems
                .Where(item => item.ReservationId.HasValue && reservationIds.Contains(item.ReservationId.Value))
                .ToListAsync(cancellationToken);
            dbContext.NotificationOutboxItems.RemoveRange(notifications);
        }

        var sessions = await dbContext.AppUserSessions
            .Where(session => session.TripId.HasValue && tripIds.Contains(session.TripId.Value))
            .ToListAsync(cancellationToken);
        foreach (var session in sessions)
        {
            session.TripId = null;
        }
        foreach (var trip in trips.Where(item => retainedTripIds.Contains(item.Id)))
        {
            if (protectedTripIds.Contains(trip.Id))
            {
                var snapshot = TripDraftSnapshotCodec.Capture(trip);
                foreach (var intent in intents.Where(item => item.TripId == trip.Id
                    && item.State is TravelCompanion.Shared.Dtos.PurchaseIntentState.Verifying
                        or TravelCompanion.Shared.Dtos.PurchaseIntentState.Pending
                        or TravelCompanion.Shared.Dtos.PurchaseIntentState.Active))
                    intent.DraftSnapshotJson = snapshot;
            }
            foreach (var intent in intents.Where(item => item.TripId == trip.Id
                && item.State is TravelCompanion.Shared.Dtos.PurchaseIntentState.Cancelled
                    or TravelCompanion.Shared.Dtos.PurchaseIntentState.Failed
                    or TravelCompanion.Shared.Dtos.PurchaseIntentState.Refunded))
                intent.DraftSnapshotJson = null;
            var itemIds = trip.Reservations.Select(item => item.Id).ToHashSet();
            var linkedStops = await dbContext.ThematicRouteStops
                .Where(item => item.ItineraryItemId.HasValue && itemIds.Contains(item.ItineraryItemId.Value))
                .ToListAsync(cancellationToken);
            foreach (var stop in linkedStops) stop.ItineraryItemId = null;
            dbContext.Reservations.RemoveRange(trip.Reservations);
            dbContext.TripDayBlocks.RemoveRange(trip.DayPlans.SelectMany(item => item.Blocks));
            dbContext.TripDayPlans.RemoveRange(trip.DayPlans);
            dbContext.TravelDocuments.RemoveRange(trip.Documents);
            if (trip.PlanDraft is not null) dbContext.TripPlanDrafts.Remove(trip.PlanDraft);
            dbContext.ItineraryProposals.RemoveRange(await dbContext.ItineraryProposals
                .Where(item => item.TripId == trip.Id).ToListAsync(cancellationToken));
            dbContext.ItineraryOperations.RemoveRange(await dbContext.ItineraryOperations
                .Where(item => item.TripId == trip.Id).ToListAsync(cancellationToken));
            trip.IsArchived = true;
            trip.DraftPurgedAtUtc = now;
            trip.BuilderSegmentsJson = null;
            trip.PlanRevision++;
            trip.UpdatedAtUtc = now;
            var grant = grants.Single(item => item.TripId == trip.Id);
            grant.Status = BuilderAccessStatus.Expired;
            grant.TrialDraftExpiresAtUtc = null;
        }
        var deletableTrips = trips.Where(item => !retainedTripIds.Contains(item.Id)).ToList();
        foreach (var grant in grants.Where(item => !retainedTripIds.Contains(item.TripId!.Value)))
            grant.TripId = null;
        dbContext.Trips.RemoveRange(deletableTrips);
        await dbContext.SaveChangesAsync(cancellationToken);
        if (transaction is not null) await transaction.CommitAsync(cancellationToken);
        logger.LogInformation("Expired free trial drafts purged. Deleted={DeletedCount}, snapshotted={SnapshotCount}.",
            deletableTrips.Count, protectedTripIds.Count);
        return trips.Count;
    }
}

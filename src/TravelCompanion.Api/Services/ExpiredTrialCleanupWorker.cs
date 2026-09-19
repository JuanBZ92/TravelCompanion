using Microsoft.EntityFrameworkCore;
using TravelCompanion.Api.Data;

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
        var grants = await dbContext.BuilderAccessGrants
            .Where(grant => grant.IsTrial
                && grant.ConvertedAtUtc == null
                && grant.TrialDraftExpiresAtUtc != null
                && grant.TrialDraftExpiresAtUtc <= now
                && grant.TripId != null)
            .ToListAsync(cancellationToken);
        if (grants.Count == 0)
        {
            return 0;
        }

        var tripIds = grants.Select(grant => grant.TripId!.Value).ToList();
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
        foreach (var grant in grants)
        {
            grant.TripId = null;
        }

        var trips = await dbContext.Trips.Where(trip => tripIds.Contains(trip.Id)).ToListAsync(cancellationToken);
        dbContext.Trips.RemoveRange(trips);
        await dbContext.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Expired free trial drafts deleted. Count={DraftCount}.", trips.Count);
        return trips.Count;
    }
}

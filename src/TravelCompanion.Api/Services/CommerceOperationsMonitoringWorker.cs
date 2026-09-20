using Microsoft.EntityFrameworkCore;
using TravelCompanion.Api.Data;

namespace TravelCompanion.Api.Services;

public sealed class CommerceOperationsMonitoringWorker(
    IServiceScopeFactory scopeFactory,
    CommerceOperationsTelemetry telemetry,
    ILogger<CommerceOperationsMonitoringWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(5));
        do
        {
            try { await RecordSnapshotAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception exception) { logger.LogError(exception, "Commerce operational monitoring failed."); }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    internal async Task RecordSnapshotAsync(CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TravelCompanionDbContext>();
        var now = DateTimeOffset.UtcNow;
        var paymentsWithoutPass = await db.StorePurchaseTransactions.AsNoTracking()
            .CountAsync(transaction => transaction.RevocationReason != "duplicate_trip_purchase_review"
                && !db.BuilderAccessGrants.Any(grant => grant.PurchaseTransactionId == transaction.Id), ct);
        var exhaustedNotifications = await db.StoreNotificationReceipts.AsNoTracking()
            .CountAsync(item => item.ProcessedAtUtc == null && item.RetryCount >= 100, ct);
        var pendingFinalizations = await db.StorePurchaseTransactions.AsNoTracking()
            .CountAsync(item => !item.AcknowledgedOrConsumed && item.ProtectedProviderToken != null
                && item.VerifiedAtUtc < now.AddMinutes(-15), ct);
        var repeatedSyncFailures = await db.TripSynchronizationWorks.AsNoTracking()
            .CountAsync(item => item.ProcessedAtUtc == null && item.AttemptCount >= 5, ct);
        var duplicatePurchases = await db.StorePurchaseTransactions.AsNoTracking()
            .CountAsync(item => item.RevocationReason == "duplicate_trip_purchase_review", ct);

        telemetry.RecordBacklog(paymentsWithoutPass, exhaustedNotifications,
            pendingFinalizations, repeatedSyncFailures, duplicatePurchases);
        if (paymentsWithoutPass + exhaustedNotifications + pendingFinalizations + repeatedSyncFailures > 0)
            logger.LogWarning(
                "Commerce operational backlog: paymentsWithoutPass={PaymentsWithoutPass}, exhaustedNotifications={ExhaustedNotifications}, pendingFinalizations={PendingFinalizations}, repeatedSyncFailures={RepeatedSyncFailures}.",
                paymentsWithoutPass, exhaustedNotifications, pendingFinalizations, repeatedSyncFailures);
    }
}

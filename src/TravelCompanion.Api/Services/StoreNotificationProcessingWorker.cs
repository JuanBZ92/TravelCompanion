using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using TravelCompanion.Api.Data;

namespace TravelCompanion.Api.Services;

public sealed class StoreNotificationProcessingWorker(
    IServiceScopeFactory scopeFactory,
    IDataProtectionProvider dataProtectionProvider,
    ILogger<StoreNotificationProcessingWorker> logger) : BackgroundService
{
    private readonly IDataProtector payloadProtector = dataProtectionProvider.CreateProtector("TravelCompanion.StoreNotificationPayload.v1");

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await ProcessBatchAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
            catch (Exception exception)
            {
                logger.LogError(exception, "Store notification processing failed.");
            }
        }
    }

    internal async Task ProcessBatchAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<TravelCompanionDbContext>();
        var purchaseService = scope.ServiceProvider.GetRequiredService<StorePurchaseService>();
        var receipts = await dbContext.StoreNotificationReceipts
            .Where(item => item.ProcessedAtUtc == null && item.ProtectedPayload != null && item.RetryCount < 100)
            .OrderBy(item => item.ReceivedAtUtc).Take(25).ToListAsync(cancellationToken);
        foreach (var receipt in receipts)
        {
            try
            {
                var payload = payloadProtector.Unprotect(receipt.ProtectedPayload!);
                if (await purchaseService.ProcessProviderNotificationAsync(
                        receipt.Provider, receipt.Environment, payload, cancellationToken))
                {
                    receipt.ProcessedAtUtc = DateTimeOffset.UtcNow;
                    receipt.LastError = null;
                }
                else
                {
                    receipt.LastError = "purchase_intent_not_ready";
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                receipt.LastError = exception.Message.Length > 1000 ? exception.Message[..1000] : exception.Message;
            }
            receipt.RetryCount++;
            await dbContext.SaveChangesAsync(cancellationToken);
        }
    }
}

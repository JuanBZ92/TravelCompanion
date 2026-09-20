namespace TravelCompanion.Api.Services;

public sealed class StorePurchaseReconciliationWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<StorePurchaseReconciliationWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(2));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<StorePurchaseService>().ReconcileAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
            catch (Exception exception)
            {
                logger.LogError(exception, "Store purchase reconciliation failed.");
            }
        }
    }
}

using TravelCompanion.Api.Data;

namespace TravelCompanion.Api.Services;

public sealed class ProductAnalyticsRetentionWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<ProductAnalyticsRetentionWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var backlog = false;
            try
            {
                using var operation = DatabaseOperation.Begin("analytics.retention", logger);
                await using var scope = scopeFactory.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<TravelCompanionDbContext>();
                var cutoff = DateTimeOffset.UtcNow.AddDays(-90);
                for (var batch = 0; batch < 10; batch++)
                {
                    using var batchOperation = DatabaseOperation.Begin("analytics.retention.batch", logger);
                    var removed = await ProductAnalyticsRetention.ProcessBatchAsync(db, cutoff, stoppingToken);
                    batchOperation.Rows = removed;
                    operation.Rows += removed;
                    backlog = removed == 5000;
                    if (!backlog) break;
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception exception)
            {
                backlog = true;
                logger.LogError(exception, "Product analytics retention cleanup failed.");
            }
            await Task.Delay(backlog ? TimeSpan.FromMinutes(1) : TimeSpan.FromHours(6), stoppingToken);
        }
    }
}

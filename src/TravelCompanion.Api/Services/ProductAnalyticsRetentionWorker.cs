using Microsoft.EntityFrameworkCore;
using TravelCompanion.Api.Data;

namespace TravelCompanion.Api.Services;

public sealed class ProductAnalyticsRetentionWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<ProductAnalyticsRetentionWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await RemoveExpiredBehaviorEventsAsync(stoppingToken);
        using var timer = new PeriodicTimer(TimeSpan.FromHours(6));
        while (await timer.WaitForNextTickAsync(stoppingToken))
            await RemoveExpiredBehaviorEventsAsync(stoppingToken);
    }

    private async Task RemoveExpiredBehaviorEventsAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<TravelCompanionDbContext>();
            var cutoff = DateTimeOffset.UtcNow.AddDays(-90);
            var expired = await dbContext.ProductAnalyticsEvents
                .Where(item => !item.IsBusinessEvent && item.OccurredAtUtc < cutoff)
                .Take(5000).ToListAsync(cancellationToken);
            if (expired.Count == 0) return;
            var groups = expired.GroupBy(item => new
            {
                Date = DateOnly.FromDateTime(item.OccurredAtUtc.UtcDateTime),
                item.Name,
                Source = item.Source ?? string.Empty,
                Platform = item.Platform ?? string.Empty,
                AppVersion = item.AppVersion ?? string.Empty,
                PaywallVariant = item.PaywallVariant ?? string.Empty
            });
            foreach (var group in groups)
            {
                var key = group.Key;
                var aggregate = await dbContext.ProductAnalyticsDailyAggregates.SingleOrDefaultAsync(item =>
                    item.Date == key.Date && item.Name == key.Name && item.Source == key.Source
                    && item.Platform == key.Platform && item.AppVersion == key.AppVersion
                    && item.PaywallVariant == key.PaywallVariant, cancellationToken);
                if (aggregate is null)
                    dbContext.ProductAnalyticsDailyAggregates.Add(new TravelCompanion.Api.Models.ProductAnalyticsDailyAggregate
                    {
                        Id = Guid.NewGuid(), Date = key.Date, Name = key.Name, Source = key.Source,
                        Platform = key.Platform, AppVersion = key.AppVersion,
                        PaywallVariant = key.PaywallVariant, EventCount = group.Count()
                    });
                else aggregate.EventCount += group.Count();
            }
            dbContext.ProductAnalyticsEvents.RemoveRange(expired);
            await dbContext.SaveChangesAsync(cancellationToken);
            logger.LogInformation("Removed {Count} expired product behavior events.", expired.Count);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception exception)
        {
            logger.LogError(exception, "Product analytics retention cleanup failed.");
        }
    }
}

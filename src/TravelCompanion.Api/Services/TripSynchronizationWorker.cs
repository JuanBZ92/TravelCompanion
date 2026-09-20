using Microsoft.EntityFrameworkCore;
using TravelCompanion.Api.Data;
using TravelCompanion.Api.Models;

namespace TravelCompanion.Api.Services;

public sealed class TripSynchronizationWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<TripSynchronizationWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(20));
        do
        {
            try { await ProcessBatchAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception exception) { logger.LogError(exception, "Trip synchronization work failed."); }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    internal async Task<int> ProcessBatchAsync(CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TravelCompanionDbContext>();
        var now = DateTimeOffset.UtcNow;
        var workItems = await db.TripSynchronizationWorks
            .Where(item => item.ProcessedAtUtc == null && item.NextAttemptAtUtc <= now)
            .OrderBy(item => item.CreatedAtUtc).Take(25).ToListAsync(ct);
        foreach (var work in workItems)
        {
            try
            {
                var scopeName = MobileDataVersionScopes.Today(work.AppUserId);
                var version = await db.MobileDataVersions.SingleOrDefaultAsync(item => item.Scope == scopeName, ct);
                if (version is null)
                {
                    version = new MobileDataVersion { Scope = scopeName, Version = 2, UpdatedAtUtc = now };
                    db.MobileDataVersions.Add(version);
                }
                else
                {
                    version.Version++;
                    version.UpdatedAtUtc = now;
                }
                work.ProcessedAtUtc = now;
                work.LastError = null;
                await db.SaveChangesAsync(ct);
            }
            catch (Exception exception) when (!ct.IsCancellationRequested)
            {
                work.AttemptCount++;
                work.LastError = exception.Message[..Math.Min(1000, exception.Message.Length)];
                work.NextAttemptAtUtc = now.AddMinutes(Math.Min(60, Math.Pow(2, Math.Min(6, work.AttemptCount))));
                await db.SaveChangesAsync(ct);
            }
        }
        return workItems.Count;
    }
}

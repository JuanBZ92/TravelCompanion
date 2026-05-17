using Microsoft.Extensions.Options;
using TravelCompanion.Notifications.Worker.Options;
using TravelCompanion.Notifications.Worker.Services;

namespace TravelCompanion.Notifications.Worker;

public sealed class NotificationWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<NotificationWorkerOptions> options,
    ILogger<NotificationWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var workerOptions = options.Value;
        var pollInterval = TimeSpan.FromSeconds(Math.Max(10, workerOptions.PollIntervalSeconds));
        logger.LogInformation(
            "Notification worker starting. Enabled={Enabled}; PollInterval={PollInterval}; LookAheadHours={LookAheadHours}.",
            workerOptions.Enabled,
            pollInterval,
            workerOptions.LookAheadHours);

        while (!stoppingToken.IsCancellationRequested)
        {
            if (workerOptions.Enabled)
            {
                await RunOnceAsync(stoppingToken);
            }

            await Task.Delay(pollInterval, stoppingToken);
        }
    }

    private async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var scheduler = scope.ServiceProvider.GetRequiredService<TravelNotificationScheduler>();
            var now = DateTimeOffset.UtcNow;
            var enqueued = await scheduler.EnqueueUpcomingScheduleRemindersAsync(now, cancellationToken);
            var dispatched = await scheduler.DispatchDueNotificationsAsync(now, cancellationToken);
            logger.LogInformation(
                "Notification worker tick complete. Enqueued={Enqueued}; Dispatched={Dispatched}.",
                enqueued,
                dispatched);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Notification worker tick failed.");
        }
    }
}

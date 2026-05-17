using TravelCompanion.Api.Models;

namespace TravelCompanion.Notifications.Worker.Services;

public sealed class LoggingNotificationSender(ILogger<LoggingNotificationSender> logger) : INotificationSender
{
    public Task SendAsync(
        NotificationOutboxItem notification,
        IReadOnlyList<NotificationDeviceRegistration> devices,
        CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "Notification sender dry-run. NotificationId={NotificationId}; Kind={Kind}; UserId={UserId}; DeviceCount={DeviceCount}; Title={Title}; DeepLink={DeepLink}.",
            notification.Id,
            notification.Kind,
            notification.UserId,
            devices.Count,
            notification.Title,
            notification.DeepLink);

        return Task.CompletedTask;
    }
}

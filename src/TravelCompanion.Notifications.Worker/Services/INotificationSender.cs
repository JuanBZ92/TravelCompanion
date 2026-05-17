using TravelCompanion.Api.Models;

namespace TravelCompanion.Notifications.Worker.Services;

public interface INotificationSender
{
    Task SendAsync(
        NotificationOutboxItem notification,
        IReadOnlyList<NotificationDeviceRegistration> devices,
        CancellationToken cancellationToken);
}

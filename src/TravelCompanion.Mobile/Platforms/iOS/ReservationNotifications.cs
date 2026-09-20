using Foundation;
using UserNotifications;
using TravelCompanion.Mobile.Services;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Mobile.Platforms.iOS;

public sealed class ReservationNotifications : ILocalReservationNotifications
{
    private static readonly ReminderDelegate ForegroundDelegate = new();

    public Task<bool> RequestPermissionAsync()
    {
        UNUserNotificationCenter.Current.Delegate = ForegroundDelegate;
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        UNUserNotificationCenter.Current.RequestAuthorization(UNAuthorizationOptions.Alert | UNAuthorizationOptions.Sound,
            (granted, error) => completion.TrySetResult(granted && error is null));
        return completion.Task;
    }

    public async Task ReplaceAsync(IReadOnlyList<ReservationReminderDto> reminders)
    {
        var center = UNUserNotificationCenter.Current;
        center.Delegate = ForegroundDelegate;
        var previous = await center.GetPendingNotificationRequestsAsync();
        center.RemovePendingNotificationRequests(previous.Where(item => item.Identifier.StartsWith("reservation-", StringComparison.Ordinal)
                || item.Identifier.StartsWith("city-change-", StringComparison.Ordinal))
            .Select(item => item.Identifier).ToArray());
        if (reminders.Count == 0) center.RemoveAllDeliveredNotifications();
        foreach (var reminder in reminders.Where(item => item.NotifyAtUtc > DateTimeOffset.UtcNow).OrderBy(item => item.NotifyAtUtc).Take(60))
        {
            using var content = new UNMutableNotificationContent
            {
                Title = reminder.Title, Body = reminder.Body, Sound = UNNotificationSound.Default
            };
            var time = reminder.NotifyAtUtc.UtcDateTime;
            using var components = new NSDateComponents
            {
                Year = time.Year, Month = time.Month, Day = time.Day, Hour = time.Hour, Minute = time.Minute,
                Second = time.Second, TimeZone = NSTimeZone.FromName("UTC")
            };
            using var trigger = UNCalendarNotificationTrigger.CreateTrigger(components, false);
            using var request = UNNotificationRequest.FromIdentifier(reminder.Id, content, trigger);
            await center.AddNotificationRequestAsync(request);
        }
    }

    private sealed class ReminderDelegate : UNUserNotificationCenterDelegate
    {
        public override void WillPresentNotification(UNUserNotificationCenter center, UNNotification notification,
            Action<UNNotificationPresentationOptions> completionHandler) =>
            completionHandler(UNNotificationPresentationOptions.Banner | UNNotificationPresentationOptions.Sound);
    }
}

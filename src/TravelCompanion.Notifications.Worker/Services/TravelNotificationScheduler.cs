using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TravelCompanion.Api.Data;
using TravelCompanion.Api.Models;
using TravelCompanion.Notifications.Worker.Options;

namespace TravelCompanion.Notifications.Worker.Services;

public sealed class TravelNotificationScheduler(
    TravelCompanionDbContext dbContext,
    IOptions<NotificationWorkerOptions> options,
    INotificationSender sender,
    ILogger<TravelNotificationScheduler> logger)
{
    private const string ScheduleReminderKind = "schedule_reminder";

    public async Task<int> EnqueueUpcomingScheduleRemindersAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var workerOptions = options.Value;
        var timeZone = ResolveTimeZone(workerOptions.ScheduleTimeZoneId);
        var localNow = TimeZoneInfo.ConvertTime(now, timeZone);
        var localHorizon = TimeZoneInfo.ConvertTime(now.AddHours(workerOptions.LookAheadHours), timeZone);
        var startDate = DateOnly.FromDateTime(localNow.DateTime.Date);
        var endDate = DateOnly.FromDateTime(localHorizon.DateTime.Date.AddDays(1));
        var staleBefore = now.AddMinutes(-Math.Max(0, workerOptions.StaleNotificationGraceMinutes));

        var reservations = await dbContext.Reservations
            .AsNoTracking()
            .Include(reservation => reservation.Trip)
            .Where(reservation =>
                reservation.Trip != null
                && reservation.Trip.AppUserId != null
                && reservation.Date >= startDate
                && reservation.Date <= endDate)
            .OrderBy(reservation => reservation.Date)
            .ThenBy(reservation => reservation.StartsAt)
            .ToListAsync(cancellationToken);

        var added = 0;
        foreach (var reservation in reservations)
        {
            var userId = reservation.Trip!.AppUserId!.Value;
            var reservationStartUtc = ToUtc(reservation.Date, reservation.StartsAt, timeZone);
            if (reservationStartUtc <= now)
            {
                continue;
            }

            foreach (var leadMinutes in workerOptions.ReservationReminderLeadMinutes.Distinct().Where(value => value > 0))
            {
                var scheduledForUtc = reservationStartUtc.AddMinutes(-leadMinutes);
                if (scheduledForUtc < staleBefore || scheduledForUtc > now.AddHours(workerOptions.LookAheadHours))
                {
                    continue;
                }

                var deduplicationKey = $"schedule:{reservation.Id}:lead:{leadMinutes}";
                var exists = await dbContext.NotificationOutboxItems
                    .AnyAsync(notification => notification.DeduplicationKey == deduplicationKey, cancellationToken);
                if (exists)
                {
                    continue;
                }

                dbContext.NotificationOutboxItems.Add(new NotificationOutboxItem
                {
                    Id = Guid.NewGuid(),
                    UserId = userId,
                    ReservationId = reservation.Id,
                    DeduplicationKey = deduplicationKey,
                    Kind = ScheduleReminderKind,
                    Title = CreateTitle(reservation, leadMinutes),
                    Body = CreateBody(reservation, leadMinutes),
                    DeepLink = $"travelcompanion://schedule/{reservation.Id}",
                    ScheduledForUtc = scheduledForUtc,
                    CreatedAtUtc = now
                });
                added++;
            }
        }

        if (added > 0)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        logger.LogInformation(
            "Schedule reminder enqueue complete. ReservationsScanned={ReservationCount}; NotificationsAdded={NotificationCount}.",
            reservations.Count,
            added);

        return added;
    }

    public async Task<int> DispatchDueNotificationsAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var workerOptions = options.Value;
        var notifications = await dbContext.NotificationOutboxItems
            .Where(notification =>
                notification.Status == NotificationOutboxStatuses.Pending
                && notification.ScheduledForUtc <= now)
            .OrderBy(notification => notification.ScheduledForUtc)
            .Take(workerOptions.SendBatchSize)
            .ToListAsync(cancellationToken);

        if (notifications.Count == 0)
        {
            return 0;
        }

        var userIds = notifications.Select(notification => notification.UserId).Distinct().ToList();
        var devices = await dbContext.NotificationDeviceRegistrations
            .Where(device => userIds.Contains(device.UserId) && device.DisabledAtUtc == null)
            .ToListAsync(cancellationToken);

        var sentOrSkipped = 0;
        foreach (var notification in notifications)
        {
            var userDevices = devices
                .Where(device => device.UserId == notification.UserId && IsEnabledForNotification(device, notification))
                .ToList();

            if (userDevices.Count == 0)
            {
                notification.Status = NotificationOutboxStatuses.Skipped;
                notification.SkippedAtUtc = now;
                notification.LastError = "No active notification devices for user.";
                sentOrSkipped++;
                continue;
            }

            try
            {
                await sender.SendAsync(notification, userDevices, cancellationToken);
                notification.Status = NotificationOutboxStatuses.Sent;
                notification.SentAtUtc = now;
                notification.AttemptCount++;
                notification.LastError = null;
                sentOrSkipped++;
            }
            catch (Exception ex)
            {
                notification.Status = NotificationOutboxStatuses.Failed;
                notification.FailedAtUtc = now;
                notification.AttemptCount++;
                notification.LastError = ex.Message;
                logger.LogWarning(
                    ex,
                    "Notification dispatch failed. NotificationId={NotificationId}; UserId={UserId}; AttemptCount={AttemptCount}.",
                    notification.Id,
                    notification.UserId,
                    notification.AttemptCount);
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return sentOrSkipped;
    }

    private static bool IsEnabledForNotification(
        NotificationDeviceRegistration device,
        NotificationOutboxItem notification)
    {
        return notification.Kind == ScheduleReminderKind
            ? device.ScheduleRemindersEnabled
            : device.RecommendationNotificationsEnabled;
    }

    private static DateTimeOffset ToUtc(DateOnly date, TimeOnly time, TimeZoneInfo timeZone)
    {
        var localDateTime = DateTime.SpecifyKind(date.ToDateTime(time), DateTimeKind.Unspecified);
        return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(localDateTime, timeZone), TimeSpan.Zero);
    }

    private static TimeZoneInfo ResolveTimeZone(string configuredTimeZoneId)
    {
        foreach (var timeZoneId in new[] { configuredTimeZoneId, "Asia/Tokyo", "Tokyo Standard Time", "UTC" })
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
            }
            catch (TimeZoneNotFoundException)
            {
            }
            catch (InvalidTimeZoneException)
            {
            }
        }

        return TimeZoneInfo.Utc;
    }

    private static string CreateTitle(Reservation reservation, int leadMinutes)
    {
        return leadMinutes >= 1440
            ? $"Manana: {reservation.Title}"
            : $"Proximo plan: {reservation.Title}";
    }

    private static string CreateBody(Reservation reservation, int leadMinutes)
    {
        var leadLabel = leadMinutes >= 1440
            ? "manana"
            : $"en {leadMinutes / 60} h";
        var location = string.IsNullOrWhiteSpace(reservation.LocationName)
            ? reservation.City
            : reservation.LocationName;

        return $"{reservation.Title} empieza {leadLabel} a las {reservation.StartsAt:HH\\:mm} en {location}.";
    }
}

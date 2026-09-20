using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TravelCompanion.Api.Data;
using TravelCompanion.Api.Models;
using TravelCompanion.Notifications.Worker.Options;
using TravelCompanion.Shared;

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
        var fallbackTimeZone = ResolveTimeZone(workerOptions.ScheduleTimeZoneId);
        var startDate = DateOnly.FromDateTime(now.UtcDateTime.Date.AddDays(-1));
        var endDate = DateOnly.FromDateTime(now.UtcDateTime.Date.AddHours(workerOptions.LookAheadHours).AddDays(2));
        var staleBefore = now.AddMinutes(-Math.Max(0, workerOptions.StaleNotificationGraceMinutes));

        var reservations = await dbContext.Reservations
            .AsNoTracking()
            .Include(reservation => reservation.Trip)
                .ThenInclude(trip => trip!.Destination)
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
            if (!ReservationReminderPolicy.IsEligible(reservation.Type, reservation.TimePrecision,
                reservation.PlanningKind, reservation.Flexibility)) continue;
            var userId = reservation.Trip!.AppUserId!.Value;
            var timeZone = ResolveReservationTimeZone(reservation, fallbackTimeZone);
            var reservationStartUtc = ToUtc(reservation.Date, reservation.StartsAt, timeZone);
            if (reservationStartUtc <= now)
            {
                continue;
            }

            foreach (var leadMinutes in workerOptions.ReservationReminderLeadMinutes
                .Intersect(ReservationReminderPolicy.LeadMinutes(reservation.Type)))
            {
                var scheduledForUtc = reservationStartUtc.AddMinutes(-leadMinutes);
                if (scheduledForUtc < staleBefore || scheduledForUtc > now.AddHours(workerOptions.LookAheadHours))
                {
                    continue;
                }

                var deduplicationKey = $"schedule:{reservation.Id}:lead:{leadMinutes}:at:{reservationStartUtc.UtcTicks}";
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
                    Body = CreateBody(reservation, leadMinutes, timeZone),
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
            if (notification.Kind == ScheduleReminderKind)
            {
                var reservation = await dbContext.Reservations.AsNoTracking().Include(item => item.Trip)
                    .FirstOrDefaultAsync(item => item.Id == notification.ReservationId, cancellationToken);
                var stillValid = reservation?.Trip is { IsArchived: false } trip
                    && trip.AppUserId == notification.UserId
                    && ReservationReminderPolicy.IsEligible(reservation.Type, reservation.TimePrecision,
                        reservation.PlanningKind, reservation.Flexibility)
                    && ReservationReminderPolicy.Create(reservation.Id, reservation.Type, reservation.Date,
                        reservation.StartsAt, reservation.TimeZoneId ?? trip.TimeZoneId, reservation.Title,
                        now.AddMinutes(-workerOptions.StaleNotificationGraceMinutes), false)
                        .Any(item => item.NotifyAtUtc == notification.ScheduledForUtc);
                if (!stillValid)
                {
                    notification.Status = NotificationOutboxStatuses.Skipped;
                    notification.SkippedAtUtc = now;
                    notification.LastError = "Reservation changed, removed, or reminder expired.";
                    sentOrSkipped++;
                    continue;
                }
            }
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

    private static TimeZoneInfo ResolveReservationTimeZone(Reservation reservation, TimeZoneInfo fallbackTimeZone)
    {
        var timeZoneId = reservation.TimeZoneId
            ?? reservation.Trip?.TimeZoneId
            ?? reservation.Trip?.Destination?.TimeZoneId;

        return string.IsNullOrWhiteSpace(timeZoneId)
            ? fallbackTimeZone
            : ResolveTimeZone(timeZoneId, fallbackTimeZone);
    }

    private static TimeZoneInfo ResolveTimeZone(string configuredTimeZoneId)
    {
        return ResolveTimeZone(configuredTimeZoneId, TimeZoneInfo.Utc);
    }

    private static TimeZoneInfo ResolveTimeZone(string configuredTimeZoneId, TimeZoneInfo fallbackTimeZone)
    {
        foreach (var timeZoneId in ExpandTimeZoneCandidates(configuredTimeZoneId))
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

        return fallbackTimeZone;
    }

    private static IEnumerable<string> ExpandTimeZoneCandidates(string configuredTimeZoneId)
    {
        if (!string.IsNullOrWhiteSpace(configuredTimeZoneId))
        {
            yield return configuredTimeZoneId.Trim();

            if (TimeZoneInfo.TryConvertIanaIdToWindowsId(configuredTimeZoneId, out var windowsId))
            {
                yield return windowsId;
            }

            if (TimeZoneInfo.TryConvertWindowsIdToIanaId(configuredTimeZoneId, out var ianaId))
            {
                yield return ianaId;
            }
        }

        yield return "UTC";
    }

    private static string CreateTitle(Reservation reservation, int leadMinutes)
    {
        return leadMinutes >= 1440
            ? $"Manana: {reservation.Title}"
            : $"Proximo plan: {reservation.Title}";
    }

    private static string CreateBody(Reservation reservation, int leadMinutes, TimeZoneInfo timeZone)
    {
        var leadLabel = leadMinutes >= 1440
            ? "manana"
            : leadMinutes < 60 ? $"en {leadMinutes} min" : $"en {leadMinutes / 60} h";
        var location = string.IsNullOrWhiteSpace(reservation.LocationName)
            ? reservation.City
            : reservation.LocationName;

        return $"{reservation.Title} empieza {leadLabel} a las {reservation.StartsAt:HH\\:mm} ({timeZone.Id}) en {location}.";
    }
}

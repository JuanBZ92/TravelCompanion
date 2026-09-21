using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TravelCompanion.Api.Data;
using TravelCompanion.Api.Models;
using TravelCompanion.Api.Services;
using TravelCompanion.Shared.Dtos;
using TravelCompanion.Shared;

namespace TravelCompanion.Api.Controllers;

[ApiController]
[Route("api/notifications")]
public sealed class NotificationsController(
    TravelCompanionDbContext dbContext,
    UserSessionService sessionService) : ControllerBase
{
    [HttpGet("reminders")]
    public async Task<ActionResult<IReadOnlyList<ReservationReminderDto>>> GetReminders(
        [FromQuery] string? locale, CancellationToken cancellationToken)
    {
        var session = await sessionService.GetSessionContextAsync(HttpContext, cancellationToken);
        if (session is null) return Unauthorized();
        var trips = await dbContext.Trips.AsNoTracking().Include(trip => trip.Reservations).Include(trip => trip.DayPlans)
            .Where(trip => trip.Id == session.TripId && trip.AppUserId == session.User.Id
                && !trip.IsArchived && trip.PublicationStatus == TripPublicationStatus.Published)
            .ToListAsync(cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var english = locale?.StartsWith("en", StringComparison.OrdinalIgnoreCase) == true;
        var reminders = trips.SelectMany(trip => trip.Reservations
                .Where(item => ReservationReminderPolicy.IsEligible(item.Type, item.TimePrecision, item.PlanningKind, item.Flexibility, item.ReminderEnabled))
                .SelectMany(item => ReservationReminderPolicy.Create(item.Id, item.Type, item.Date, item.StartsAt,
                    item.TimeZoneId ?? trip.TimeZoneId, item.Title, now, english))
                .Concat(ReservationReminderPolicy.CityChanges(trip.DayPlans.Select(day => (day.Id, day.Date, day.City)),
                    trip.TimeZoneId, now, english)))
            .OrderBy(item => item.NotifyAtUtc).ThenBy(item => item.Id).Take(60).ToList();
        return Ok(reminders);
    }

    [HttpPost("devices")]
    public async Task<ActionResult<NotificationDeviceRegistrationDto>> RegisterDevice(
        RegisterNotificationDeviceRequest request,
        CancellationToken cancellationToken)
    {
        var user = await sessionService.GetUserAsync(HttpContext, cancellationToken);
        if (user is null)
        {
            return Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(request.InstallationId))
        {
            return this.ValidationError(nameof(request.InstallationId), "InstallationId is required.");
        }

        if (string.IsNullOrWhiteSpace(request.PushToken))
        {
            return this.ValidationError(nameof(request.PushToken), "PushToken is required.");
        }

        var platform = NormalizePlatform(request.Platform);
        if (platform is null)
        {
            return this.ValidationError(nameof(request.Platform), "Platform must be fcmv1 or apns.");
        }

        var installationId = request.InstallationId.Trim();
        var now = DateTimeOffset.UtcNow;
        var device = await dbContext.NotificationDeviceRegistrations
            .FirstOrDefaultAsync(existingDevice =>
                existingDevice.UserId == user.Id
                && existingDevice.InstallationId == installationId,
                cancellationToken);

        if (device is null)
        {
            device = new NotificationDeviceRegistration
            {
                Id = Guid.NewGuid(),
                UserId = user.Id,
                InstallationId = installationId,
                Platform = platform,
                PushToken = request.PushToken.Trim(),
                CreatedAtUtc = now
            };
            dbContext.NotificationDeviceRegistrations.Add(device);
        }

        device.Platform = platform;
        device.PushToken = request.PushToken.Trim();
        device.Locale = NormalizeOptional(request.Locale);
        device.TimeZoneId = NormalizeOptional(request.TimeZoneId);
        device.ScheduleRemindersEnabled = request.ScheduleRemindersEnabled;
        device.RecommendationNotificationsEnabled = request.RecommendationNotificationsEnabled;
        device.UpdatedAtUtc = now;
        device.LastSeenAtUtc = now;
        device.DisabledAtUtc = null;

        await dbContext.SaveChangesAsync(cancellationToken);
        return Ok(ToDto(device));
    }

    [HttpDelete("devices/{installationId}")]
    public async Task<IActionResult> DisableDevice(string installationId, CancellationToken cancellationToken)
    {
        var user = await sessionService.GetUserAsync(HttpContext, cancellationToken);
        if (user is null)
        {
            return Unauthorized();
        }

        var normalizedInstallationId = installationId.Trim();
        var device = await dbContext.NotificationDeviceRegistrations
            .FirstOrDefaultAsync(existingDevice =>
                existingDevice.UserId == user.Id
                && existingDevice.InstallationId == normalizedInstallationId,
                cancellationToken);

        if (device is null)
        {
            return NoContent();
        }

        var now = DateTimeOffset.UtcNow;
        device.DisabledAtUtc = now;
        device.UpdatedAtUtc = now;
        await dbContext.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    private static NotificationDeviceRegistrationDto ToDto(NotificationDeviceRegistration device)
    {
        return new NotificationDeviceRegistrationDto(
            device.Id,
            device.InstallationId,
            device.Platform,
            device.Locale,
            device.TimeZoneId,
            device.ScheduleRemindersEnabled,
            device.RecommendationNotificationsEnabled,
            device.LastSeenAtUtc,
            device.DisabledAtUtc is null);
    }

    private static string? NormalizePlatform(string? platform)
    {
        return platform?.Trim().ToLowerInvariant() switch
        {
            "android" or "fcm" or "fcmv1" => "fcmv1",
            "ios" or "apns" => "apns",
            _ => null
        };
    }

    private static string? NormalizeOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}

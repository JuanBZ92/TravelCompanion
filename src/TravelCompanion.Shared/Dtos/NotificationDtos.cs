namespace TravelCompanion.Shared.Dtos;

public sealed record RegisterNotificationDeviceRequest(
    string InstallationId,
    string Platform,
    string PushToken,
    string? Locale,
    string? TimeZoneId,
    bool ScheduleRemindersEnabled = true,
    bool RecommendationNotificationsEnabled = true);

public sealed record NotificationDeviceRegistrationDto(
    Guid Id,
    string InstallationId,
    string Platform,
    string? Locale,
    string? TimeZoneId,
    bool ScheduleRemindersEnabled,
    bool RecommendationNotificationsEnabled,
    DateTimeOffset LastSeenAtUtc,
    bool IsEnabled);

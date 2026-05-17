namespace TravelCompanion.Api.Models;

public sealed class NotificationDeviceRegistration
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public AppUser? User { get; set; }
    public required string InstallationId { get; set; }
    public required string Platform { get; set; }
    public required string PushToken { get; set; }
    public string? Locale { get; set; }
    public string? TimeZoneId { get; set; }
    public bool ScheduleRemindersEnabled { get; set; } = true;
    public bool RecommendationNotificationsEnabled { get; set; } = true;
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset LastSeenAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? DisabledAtUtc { get; set; }
}

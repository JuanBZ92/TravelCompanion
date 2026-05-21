namespace TravelCompanion.Api.Models;

public sealed class NotificationOutboxItem
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public AppUser? User { get; set; }
    public Guid? ReservationId { get; set; }
    public Reservation? Reservation { get; set; }
    public Guid? RecommendationId { get; set; }
    public Recommendation? Recommendation { get; set; }
    public required string DeduplicationKey { get; set; }
    public required string Kind { get; set; }
    public required string Title { get; set; }
    public required string Body { get; set; }
    public string? DeepLink { get; set; }
    public DateTimeOffset ScheduledForUtc { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? SentAtUtc { get; set; }
    public DateTimeOffset? FailedAtUtc { get; set; }
    public DateTimeOffset? SkippedAtUtc { get; set; }
    public int AttemptCount { get; set; }
    public string Status { get; set; } = NotificationOutboxStatuses.Pending;
    public string? LastError { get; set; }
}

public static class NotificationOutboxStatuses
{
    public const string Pending = "pending";
    public const string Sent = "sent";
    public const string Failed = "failed";
    public const string Skipped = "skipped";
}

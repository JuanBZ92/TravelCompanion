namespace TravelCompanion.Notifications.Worker.Options;

public sealed class NotificationWorkerOptions
{
    public const string SectionName = "Notifications";

    public bool Enabled { get; set; } = true;
    public int PollIntervalSeconds { get; set; } = 60;
    public int LookAheadHours { get; set; } = 48;
    public int SendBatchSize { get; set; } = 50;
    public int StaleNotificationGraceMinutes { get; set; } = 30;
    public string ScheduleTimeZoneId { get; set; } = "UTC";
    public int[] ReservationReminderLeadMinutes { get; set; } = [1440, 180];
}

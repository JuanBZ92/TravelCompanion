namespace TravelCompanion.Shared.Dtos;

public sealed record ReservationReminderDto(string Id, Guid? ReservationId, DateTimeOffset NotifyAtUtc,
    string Title, string Body, Guid? TripDayId = null);

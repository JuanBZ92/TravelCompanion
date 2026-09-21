using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Shared;

public static class ReservationReminderPolicy
{
    public static IReadOnlyList<int> LeadMinutes(ReservationType type) =>
        type is ReservationType.Flight or ReservationType.Lodging ? [1440, 180, 45] : [180, 45];
    public static bool IsEligible(ReservationType type, ItineraryTimePrecision precision,
        ScheduleItemKind kind, ItineraryFlexibility flexibility, bool? reminderEnabled = null) => precision == ItineraryTimePrecision.Exact
        && (reminderEnabled ?? (type is ReservationType.Flight or ReservationType.Lodging
            || kind == ScheduleItemKind.ConfirmedReservation
            || flexibility == ItineraryFlexibility.ConfirmedReservation));

    public static IReadOnlyList<ReservationReminderDto> Create(Guid id, ReservationType type,
        DateOnly date, TimeOnly time, string timeZoneId, string title, DateTimeOffset now, bool english)
    {
        var resolved = ResolveStart(date, time, timeZoneId);
        if (resolved is not { } value) return [];
        var (zone, start) = value;
        var label = type switch
        {
            ReservationType.Flight => english ? "Flight" : "Vuelo",
            ReservationType.Lodging => "Check-in",
            _ => english ? "Reservation" : "Reserva"
        };
        return LeadMinutes(type).Select(minutes => new ReservationReminderDto(
                $"reservation-{id:N}-{minutes}", id, start.AddMinutes(-minutes), $"{label}: {title}",
                english ? $"In {(minutes == 1440 ? "24 hours" : minutes == 180 ? "3 hours" : "45 minutes")}, at {time:HH:mm} ({zone.Id})."
                    : $"En {(minutes == 1440 ? "24 horas" : minutes == 180 ? "3 horas" : "45 minutos")}, a las {time:HH:mm} ({zone.Id})."))
            .Where(reminder => reminder.NotifyAtUtc > now).ToList();
    }

    public static IReadOnlyList<ReservationReminderDto> CityChanges(
        IEnumerable<(Guid Id, DateOnly Date, string City)> days, string timeZoneId, DateTimeOffset now, bool english)
    {
        var ordered = days.OrderBy(day => day.Date).ToList();
        var reminders = new List<ReservationReminderDto>();
        for (var i = 1; i < ordered.Count; i++)
        {
            var previous = ordered[i - 1];
            var current = ordered[i];
            if (current.Date != previous.Date.AddDays(1) || string.IsNullOrWhiteSpace(previous.City)
                || string.IsNullOrWhiteSpace(current.City)
                || string.Equals(previous.City.Trim(), current.City.Trim(), StringComparison.OrdinalIgnoreCase)) continue;
            var resolved = ResolveStart(current.Date.AddDays(-1), new TimeOnly(9, 0), timeZoneId);
            if (resolved is not { } value || value.Start <= now) continue;
            reminders.Add(new ReservationReminderDto($"city-change-{current.Id:N}", null, value.Start,
                english ? "City change tomorrow" : "Mañana cambiás de ciudad",
                english ? $"{previous.City.Trim()} → {current.City.Trim()}. Review your transport and accommodation."
                    : $"{previous.City.Trim()} → {current.City.Trim()}. Revisá tu traslado y alojamiento.", current.Id));
        }
        return reminders;
    }

    private static (TimeZoneInfo Zone, DateTimeOffset Start)? ResolveStart(DateOnly date, TimeOnly time, string timeZoneId)
    {
        TimeZoneInfo zone;
        try { zone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId); }
        catch (TimeZoneNotFoundException) { return null; }
        catch (InvalidTimeZoneException) { return null; }
        var local = date.ToDateTime(time, DateTimeKind.Unspecified);
        // Never guess an invalid or ambiguous daylight-saving time.
        if (zone.IsInvalidTime(local) || zone.IsAmbiguousTime(local)) return null;
        var start = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(local, zone));
        return (zone, start);
    }
}

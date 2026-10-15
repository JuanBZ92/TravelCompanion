using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Xml;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Shared;

public sealed record CalendarEvent(string ImportKey, string Title, string Location, string Notes,
    DateOnly Date, TimeOnly? StartsAt, TimeOnly? EndsAt, int? DurationMinutes,
    string TimeZoneId, string SourceTimeZoneId, bool UsesTripTimeZone);
public sealed record CalendarIssue(string Title, string Reason);
public sealed record CalendarPreview(IReadOnlyList<CalendarEvent> Events, IReadOnlyList<CalendarIssue> Issues);

/// <summary>Bounded RFC 5545 event exchange, with no network access or recurrence expansion.</summary>
public static class CalendarExchange
{
    public const int MaximumBytes = 1024 * 1024;
    public const int MaximumEvents = 500;
    private static readonly string[] DateTimeFormats = ["yyyyMMdd'T'HHmmss", "yyyyMMdd'T'HHmm"];

    // Bookings and personal plans are the traveler's data, regardless of editor.
    // Editorial ideas use the same catalog access policy, never a stale paid grant.
    public static bool CanExportItem(ScheduleItemDto item, bool hasKnownValidAccess, SessionAccessMode accessMode,
        RecommendationDto? recommendation = null, UserEntitlementsDto? entitlements = null)
    {
        if (item.IsTravelerOwned || item.RecommendationId is null || item.IsConfirmedReservation
            || item.Type is ReservationType.Flight or ReservationType.Lodging) return true;
        if (recommendation is null || recommendation.Id != item.RecommendationId) return false;
        if (!hasKnownValidAccess || accessMode is SessionAccessMode.BuilderReadOnly or SessionAccessMode.FreeMapPreview)
            entitlements = null;
        if (entitlements is not null && (entitlements.Entitlements.Count == 0
            || entitlements.Entitlements.Any(grant => grant.ExpiresAt <= DateTimeOffset.UtcNow)))
        {
            var active = entitlements.Entitlements.Where(grant => grant.ExpiresAt is null || grant.ExpiresAt > DateTimeOffset.UtcNow).ToArray();
            entitlements = entitlements with
            {
                Entitlements = active,
                PackageIds = active.Where(grant => grant.PackageId.HasValue).Select(grant => grant.PackageId!.Value).Distinct().ToArray(),
                DestinationIds = active.Where(grant => grant.DestinationId.HasValue).Select(grant => grant.DestinationId!.Value).Distinct().ToArray(),
                AccessLevels = active.Select(grant => grant.AccessLevel).Distinct().ToArray()
            };
        }
        return ContentAccessPolicy.IsRecommendationUnlocked(entitlements, recommendation.AccessLevel,
            recommendation.DestinationId, recommendation.PackageIds);
    }

    public static CalendarPreview Parse(string content, string tripTimeZoneId, DateOnly startsOn, DateOnly endsOn)
    {
        if (Encoding.UTF8.GetByteCount(content) > MaximumBytes) throw new ArgumentException("CalendarFileTooLarge");
        var zone = FindZone(tripTimeZoneId);
        var events = new List<CalendarEvent>();
        var issues = new List<CalendarIssue>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        List<Property>? current = null;
        var depth = 0;
        var count = 0;
        var calendar = false;
        var closed = false;
        var cancelled = false;
        foreach (var line in Unfold(content.TrimStart('\uFEFF')))
        {
            if (line.Equals("BEGIN:VCALENDAR", StringComparison.OrdinalIgnoreCase)) { calendar = true; continue; }
            if (!calendar || closed) continue;
            if (line.Equals("END:VCALENDAR", StringComparison.OrdinalIgnoreCase)) { closed = true; continue; }
            if (line.Equals("METHOD:CANCEL", StringComparison.OrdinalIgnoreCase)) cancelled = true;
            if (line.Equals("BEGIN:VEVENT", StringComparison.OrdinalIgnoreCase))
            {
                if (current is not null) issues.Add(new(Title(current), "CalendarMalformedEvent"));
                if (++count > MaximumEvents) throw new ArgumentException("CalendarTooManyEvents");
                current = []; depth = 0; continue;
            }
            if (current is null) continue;
            if (line.Equals("END:VEVENT", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    if (depth != 0) throw new ArgumentException("CalendarMalformedEvent");
                    var item = ReadEvent(current, zone, startsOn, endsOn);
                    if (seen.Add(item.ImportKey)) events.Add(item);
                    else issues.Add(new(item.Title, "CalendarDuplicateEvent"));
                }
                catch (ArgumentException exception) { issues.Add(new(Title(current), exception.Message)); }
                current = null; continue;
            }
            if (line.StartsWith("BEGIN:", StringComparison.OrdinalIgnoreCase)) { depth++; continue; }
            if (line.StartsWith("END:", StringComparison.OrdinalIgnoreCase)) { depth--; continue; }
            if (depth == 0)
            {
                var property = ReadProperty(line);
                if (property is not null) current.Add(property);
            }
        }
        if (!calendar || !closed) throw new ArgumentException("CalendarInvalidFile");
        if (current is not null) issues.Add(new(Title(current), "CalendarMalformedEvent"));
        if (cancelled) return new([], [new("", "CalendarCancelledEvent")]);
        return new(events.OrderBy(item => item.Date).ThenBy(item => item.StartsAt).ToArray(), issues);
    }

    public static ItineraryItemMutationRequest Mutation(CalendarEvent item, int revision, bool confirmOverlap = false) =>
        new(null, null, item.Title, item.Date, Period(item.StartsAt), item.StartsAt.HasValue,
            item.StartsAt, item.EndsAt, null, item.Location, item.Location, item.Notes,
            null, null, revision, item.ImportKey, ConfirmOverlap: confirmOverlap,
            Flexibility: ItineraryFlexibility.FixedByTraveler, DurationMinutes: item.DurationMinutes,
            ReminderEnabled: false, TimeZoneId: item.TimeZoneId);

    public static string Export(TripScheduleDto schedule, IReadOnlyCollection<Guid> selection, DateTimeOffset createdAt)
    {
        var output = new StringBuilder();
        Line(output, "BEGIN:VCALENDAR"); Line(output, "VERSION:2.0");
        Line(output, "PRODID:-//Yuku//Travel calendar//ES"); Line(output, "CALSCALE:GREGORIAN");
        foreach (var item in schedule.Items.Where(item => selection.Contains(item.Id)).OrderBy(item => item.Date).ThenBy(item => item.StartsAt))
        {
            Line(output, "BEGIN:VEVENT");
            Line(output, $"UID:{item.Id:N}.{schedule.TripId:N}@yuku.app");
            Line(output, $"DTSTAMP:{createdAt.UtcDateTime:yyyyMMdd'T'HHmmss'Z'}");
            if (item.HasExactTime)
            {
                var zone = FindZone(item.TimeZoneId ?? schedule.TimeZoneId);
                var start = LocalInstant(item.Date.ToDateTime(item.StartsAt), zone);
                Line(output, $"DTSTART:{start.UtcDateTime:yyyyMMdd'T'HHmmss'Z'}");
                var end = End(item);
                if (end.HasValue) Line(output, $"DTEND:{LocalInstant(end.Value, zone).UtcDateTime:yyyyMMdd'T'HHmmss'Z'}");
                Line(output, "X-YUKU-TIMEZONE:" + Escape(zone.Id));
            }
            else
            {
                Line(output, $"DTSTART;VALUE=DATE:{item.Date:yyyyMMdd}");
                Line(output, $"DTEND;VALUE=DATE:{item.Date.AddDays(1):yyyyMMdd}");
            }
            Line(output, "SUMMARY:" + Escape(item.Title));
            if (!string.IsNullOrWhiteSpace(item.LocationName)) Line(output, "LOCATION:" + Escape(item.LocationName));
            if (!string.IsNullOrWhiteSpace(item.Notes)) Line(output, "DESCRIPTION:" + Escape(item.Notes));
            Line(output, "END:VEVENT");
        }
        Line(output, "END:VCALENDAR");
        return output.ToString();
    }

    private static CalendarEvent ReadEvent(List<Property> properties, TimeZoneInfo tripZone, DateOnly startsOn, DateOnly endsOn)
    {
        if (properties.Any(p => p.Name is "RRULE" or "RDATE" or "EXDATE" or "RECURRENCE-ID")) throw new ArgumentException("CalendarRecurringEvent");
        if (properties.Any(p => p.Name == "STATUS" && p.Value.Equals("CANCELLED", StringComparison.OrdinalIgnoreCase))) throw new ArgumentException("CalendarCancelledEvent");
        if (properties.Any(p => p.Name == "ATTACH")) throw new ArgumentException("CalendarAttachedEvent");
        foreach (var name in new[] { "DTSTART", "DTEND", "DURATION", "UID", "SUMMARY", "LOCATION", "DESCRIPTION" })
            if (properties.Count(p => p.Name == name) > 1) throw new ArgumentException("CalendarMalformedEvent");
        var title = Title(properties);
        var location = Text(properties, "LOCATION"); var notes = Text(properties, "DESCRIPTION");
        if (title.Length == 0 || title.Length > 160 || location.Length > 160 || notes.Length > 2000
            || title.Any(char.IsControl) || location.Any(c => char.IsControl(c) && c != '\n')
            || notes.Any(c => char.IsControl(c) && c is not '\n' and not '\t')) throw new ArgumentException("CalendarUnsupportedFields");
        var start = properties.FirstOrDefault(p => p.Name == "DTSTART") ?? throw new ArgumentException("CalendarMissingTime");
        var end = properties.FirstOrDefault(p => p.Name == "DTEND");
        var duration = properties.FirstOrDefault(p => p.Name == "DURATION");
        if (end is not null && duration is not null) throw new ArgumentException("CalendarMalformedEvent");
        var allDay = start.Parameters.GetValueOrDefault("VALUE")?.Equals("DATE", StringComparison.OrdinalIgnoreCase) == true;
        DateOnly date; TimeOnly? startsAt = null, endsAt = null; int? minutes = null;
        var sourceZone = tripZone.Id; var usesTripZone = false;
        if (allDay)
        {
            if (!DateOnly.TryParseExact(start.Value, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date)
                || start.Parameters.ContainsKey("TZID")) throw new ArgumentException("CalendarMissingTime");
            if (end is not null && (!string.Equals(end.Parameters.GetValueOrDefault("VALUE"), "DATE", StringComparison.OrdinalIgnoreCase)
                || !DateOnly.TryParseExact(end.Value, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var endDate)
                || endDate != date.AddDays(1))) throw new ArgumentException("CalendarLongEvent");
            if (duration is not null && ReadDuration(duration.Value) != TimeSpan.FromDays(1)) throw new ArgumentException("CalendarLongEvent");
        }
        else
        {
            var instant = Instant(start, tripZone, out sourceZone, out usesTripZone);
            var local = TimeZoneInfo.ConvertTime(instant, tripZone);
            if (tripZone.IsAmbiguousTime(local.DateTime)) throw new ArgumentException("CalendarAmbiguousTime");
            date = DateOnly.FromDateTime(local.DateTime); startsAt = TimeOnly.FromDateTime(local.DateTime);
            DateTimeOffset? finish = end is not null ? Instant(end, tripZone, out _, out _) : null;
            if (duration is not null) finish = instant.Add(ReadDuration(duration.Value));
            if (finish.HasValue)
            {
                var elapsed = finish.Value - instant;
                if (elapsed <= TimeSpan.Zero || elapsed.TotalMinutes > 1440 || elapsed.TotalMinutes % 1 != 0)
                    throw new ArgumentException("CalendarLongEvent");
                minutes = (int)elapsed.TotalMinutes;
                endsAt = TimeOnly.FromDateTime(TimeZoneInfo.ConvertTime(finish.Value, tripZone).DateTime);
            }
        }
        if (date < startsOn || date > endsOn) throw new ArgumentException("CalendarOutsideTrip");
        var uid = Text(properties, "UID");
        if (uid.Length > 1024 || uid.Any(char.IsControl)) throw new ArgumentException("CalendarMalformedEvent");
        var identity = uid.Length > 0 ? uid : date.ToString("yyyyMMdd", CultureInfo.InvariantCulture) + "|"
            + startsAt?.ToString("HHmmss", CultureInfo.InvariantCulture) + "|" + title + "|" + location;
        var key = "ics-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity))).ToLowerInvariant();
        return new(key, title, location, notes, date, startsAt, endsAt, minutes, tripZone.Id, sourceZone, usesTripZone);
    }

    private static DateTimeOffset Instant(Property property, TimeZoneInfo fallback, out string sourceZone, out bool usesFallback)
    {
        var utc = property.Value.EndsWith('Z');
        var value = utc ? property.Value[..^1] : property.Value;
        if (property.Parameters.GetValueOrDefault("VALUE") is { } type && !type.Equals("DATE-TIME", StringComparison.OrdinalIgnoreCase)
            || !DateTime.TryParseExact(value, DateTimeFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var local))
            throw new ArgumentException("CalendarMissingTime");
        var zoneId = property.Parameters.GetValueOrDefault("TZID");
        if (utc && zoneId is not null) throw new ArgumentException("CalendarUnknownZone");
        usesFallback = !utc && zoneId is null;
        var zone = utc ? TimeZoneInfo.Utc : zoneId is null ? fallback : FindZone(zoneId);
        sourceZone = zone.Id;
        return LocalInstant(local, zone);
    }

    private static DateTimeOffset LocalInstant(DateTime local, TimeZoneInfo zone)
    {
        local = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        if (zone.IsInvalidTime(local) || zone.IsAmbiguousTime(local)) throw new ArgumentException("CalendarAmbiguousTime");
        return new DateTimeOffset(local, zone.GetUtcOffset(local));
    }

    private static TimeZoneInfo FindZone(string id)
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException) { throw new ArgumentException("CalendarUnknownZone"); }
    }

    private static TimeSpan ReadDuration(string value)
    {
        try
        {
            var duration = XmlConvert.ToTimeSpan(value);
            if (duration <= TimeSpan.Zero || duration > TimeSpan.FromDays(1)) throw new ArgumentException("CalendarLongEvent");
            return duration;
        }
        catch (FormatException) { throw new ArgumentException("CalendarLongEvent"); }
        catch (OverflowException) { throw new ArgumentException("CalendarLongEvent"); }
    }

    private static string Period(TimeOnly? time) => time?.Hour switch { < 5 => "night", < 12 => "morning", < 15 => "midday", < 20 => "afternoon", >= 20 => "night", _ => "morning" };
    private static string Title(List<Property> properties) => Text(properties, "SUMMARY");
    private static string Text(List<Property> properties, string name) => Unescape(properties.FirstOrDefault(p => p.Name == name)?.Value ?? "").Trim();

    private static DateTime? End(ScheduleItemDto item)
    {
        if (item.EndsAt is { } time)
        {
            var date = item.EndsOn ?? (time < item.StartsAt ? item.Date.AddDays(1) : item.Date);
            return date.ToDateTime(time);
        }
        return item.DurationMinutes is > 0 ? item.Date.ToDateTime(item.StartsAt).AddMinutes(item.DurationMinutes.Value) : null;
    }

    private sealed record Property(string Name, string Value, Dictionary<string, string> Parameters);
    private static Property? ReadProperty(string line)
    {
        var quoted = false; var separator = -1;
        for (var i = 0; i < line.Length; i++) { if (line[i] == '"') quoted = !quoted; if (line[i] == ':' && !quoted) { separator = i; break; } }
        if (separator < 1) return null;
        var header = line[..separator].Split(';');
        var parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var part in header.Skip(1))
        {
            var equals = part.IndexOf('=');
            if (equals > 0) parameters[part[..equals].ToUpperInvariant()] = part[(equals + 1)..].Trim('"');
        }
        return new(header[0].ToUpperInvariant(), line[(separator + 1)..], parameters);
    }

    private static IEnumerable<string> Unfold(string content)
    {
        StringBuilder? pending = null;
        foreach (var line in content.Replace("\r\n", "\n").Split('\n'))
        {
            if ((line.StartsWith(' ') || line.StartsWith('\t')) && pending is not null) pending.Append(line.AsSpan(1));
            else { if (pending is not null) yield return pending.ToString(); pending = new StringBuilder(line.TrimEnd('\r')); }
        }
        if (pending is not null) yield return pending.ToString();
    }

    private static string Unescape(string value)
    {
        var text = new StringBuilder();
        for (var i = 0; i < value.Length; i++)
        {
            if (value[i] == '\\' && i + 1 < value.Length) { var next = value[++i]; text.Append(next is 'n' or 'N' ? '\n' : next); }
            else text.Append(value[i]);
        }
        return text.ToString();
    }

    private static string Escape(string value) => new string(value.Where(c => !char.IsControl(c) || c is '\n' or '\r' or '\t').ToArray())
        .Replace("\\", "\\\\").Replace("\r\n", "\n").Replace("\r", "\n").Replace("\n", "\\n").Replace(";", "\\;").Replace(",", "\\,");

    private static void Line(StringBuilder output, string value)
    {
        var bytes = 0;
        foreach (var rune in value.EnumerateRunes())
        {
            if (bytes + rune.Utf8SequenceLength > 75) { output.Append("\r\n "); bytes = 1; }
            output.Append(rune.ToString()); bytes += rune.Utf8SequenceLength;
        }
        output.Append("\r\n");
    }
}

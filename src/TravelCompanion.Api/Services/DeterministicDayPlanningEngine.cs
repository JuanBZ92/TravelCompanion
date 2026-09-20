using TravelCompanion.Api.Models;

namespace TravelCompanion.Api.Services;

public readonly record struct LocalPlanningInterval(DateTime Start, DateTime End)
{
    public int DurationMinutes => Math.Max(0, (int)(End - Start).TotalMinutes);
    public bool Overlaps(LocalPlanningInterval other) => Start < other.End && End > other.Start;
}

public sealed class DeterministicDayPlanningEngine
{
    public LocalPlanningInterval CreateWindow(DateOnly date, TimeOnly start, TimeOnly end)
    {
        var startsAt = DateTime.SpecifyKind(date.ToDateTime(start), DateTimeKind.Unspecified);
        var endDate = end <= start ? date.AddDays(1) : date;
        var endsAt = DateTime.SpecifyKind(endDate.ToDateTime(end), DateTimeKind.Unspecified);
        return new(startsAt, endsAt);
    }

    public LocalPlanningInterval GetInterval(Reservation item)
    {
        var start = DateTime.SpecifyKind(item.Date.ToDateTime(item.StartsAt), DateTimeKind.Unspecified);
        var endDate = item.EndsOn
            ?? (item.EndsAt.HasValue && item.EndsAt.Value <= item.StartsAt ? item.Date.AddDays(1) : item.Date);
        var endTime = item.EndsAt ?? item.StartsAt.AddMinutes(item.DurationMinutes is > 0 ? item.DurationMinutes.Value : 60);
        var end = DateTime.SpecifyKind(endDate.ToDateTime(endTime), DateTimeKind.Unspecified);
        if (end <= start) end = start.AddMinutes(item.DurationMinutes is > 0 ? item.DurationMinutes.Value : 60);
        return new(start, end);
    }

    public bool Covers(Reservation item, DateOnly date)
    {
        var interval = GetInterval(item);
        var day = new LocalPlanningInterval(date.ToDateTime(TimeOnly.MinValue), date.AddDays(1).ToDateTime(TimeOnly.MinValue));
        return interval.Overlaps(day);
    }

    public DateTime? FindNextAvailable(DateTime cursor, int durationMinutes,
        IEnumerable<Reservation> occupiedItems, DateTime windowEnd, int bufferMinutes = 20)
    {
        var duration = TimeSpan.FromMinutes(Math.Max(1, durationMinutes));
        foreach (var interval in occupiedItems.Select(GetInterval).OrderBy(item => item.Start))
        {
            if (interval.End <= cursor) continue;
            if (cursor + duration <= interval.Start) break;
            if (cursor < interval.End && cursor + duration > interval.Start)
                cursor = interval.End.AddMinutes(bufferMinutes);
        }
        return cursor + duration <= windowEnd ? cursor : null;
    }

    public IReadOnlyList<(Reservation First, Reservation Second)> FindOverlaps(IEnumerable<Reservation> items)
    {
        var ordered = items.Select(item => (Item: item, Interval: GetInterval(item)))
            .OrderBy(item => item.Interval.Start).ThenBy(item => item.Interval.End).ToList();
        var conflicts = new List<(Reservation, Reservation)>();
        for (var i = 0; i < ordered.Count; i++)
            for (var j = i + 1; j < ordered.Count && ordered[j].Interval.Start < ordered[i].Interval.End; j++)
                if (ordered[i].Interval.Overlaps(ordered[j].Interval))
                    conflicts.Add((ordered[i].Item, ordered[j].Item));
        return conflicts;
    }
}

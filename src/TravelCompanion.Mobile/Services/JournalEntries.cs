using System.Globalization;
using System.Collections.ObjectModel;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Mobile.Services;

public sealed record JournalEntry(ScheduleItemDto Item)
{
    public string Title => Item.Title;
    public string Notes => Item.Notes.Trim();
    public string Context => $"{Item.Date.ToString("d MMMM", CultureInfo.CurrentCulture)} · {Item.City}";
}

public static class JournalEntries
{
    public static bool SameContent(JournalMemory first, JournalMemory second) =>
        (first with { Photos = null }) == (second with { Photos = null }) && first.Images.SequenceEqual(second.Images);

    // Keep unchanged rows and their native views when a local snapshot is revalidated.
    public static void ReconcileRows<T>(ObservableCollection<T> rows, IReadOnlyList<T> desired, Func<T, string> key) where T : class
    {
        var keys = desired.Select(key).ToHashSet(StringComparer.Ordinal);
        for (var i = rows.Count - 1; i >= 0; i--) if (!keys.Contains(key(rows[i]))) rows.RemoveAt(i);
        for (var i = 0; i < desired.Count; i++)
        {
            var item = desired[i];
            if (i < rows.Count && ReferenceEquals(rows[i], item)) continue;
            var oldIndex = -1;
            for (var j = i; j < rows.Count; j++) if (ReferenceEquals(rows[j], item)) { oldIndex = j; break; }
            if (oldIndex >= 0) rows.Move(oldIndex, i);
            else if (i < rows.Count && key(rows[i]) == key(item)) rows[i] = item;
            else rows.Insert(i, item);
        }
        while (rows.Count > desired.Count) rows.RemoveAt(rows.Count - 1);
    }

    public static IEnumerable<int> VisibleRows(int count, int first, int last, int overscan = 1)
    {
        if (count <= 0 || first < 0 || last < first || first >= count) yield break;
        var start = Math.Max(0, first - Math.Max(0, overscan));
        var end = (int)Math.Min(count - 1L, (long)last + Math.Max(0, overscan));
        for (var i = start; i <= end; i++) yield return i;
    }

    // The first preview is the chosen cover; remaining previews keep their original viewer indices.
    public static IReadOnlyList<int> PhotoPreviewIndices(JournalMemory memory, int maximum = 3) =>
        Enumerable.Range(0, memory.Images.Length)
            .OrderByDescending(index => memory.Images[index].Id == memory.CoverId)
            .Take(Math.Max(0, maximum)).ToArray();

    public static IReadOnlyList<JournalEntry> Build(IEnumerable<ScheduleItemDto> items) => items
        .Where(item => item.IsTravelerOwned && !string.IsNullOrWhiteSpace(item.Notes)
            && !string.Equals(item.Notes.Trim(), "Guardado desde Travel Assistant.", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(item.Notes.Trim(), item.CuratedNotes?.Trim(), StringComparison.Ordinal))
        .OrderBy(item => item.Date).ThenBy(item => item.StartsAt)
        .Select(item => new JournalEntry(item)).ToArray();
}

// A bounded cache for already decoded thumbnail sources, never full photo payloads.
public sealed class JournalThumbnailCache<T>(int capacity = 48) where T : class
{
    private readonly Dictionary<Guid, (T Value, long Used)> entries = [];
    private long clock;
    public int Count => entries.Count;
    public bool TryGet(Guid id, out T? value)
    {
        if (!entries.TryGetValue(id, out var entry)) { value = null; return false; }
        entries[id] = (entry.Value, ++clock); value = entry.Value; return true;
    }
    public void Add(Guid id, T value) => entries[id] = (value, ++clock);
    public IReadOnlyList<Guid> Trim(IReadOnlySet<Guid> visible)
    {
        var removed = new List<Guid>();
        foreach (var id in entries.Where(x => !visible.Contains(x.Key)).OrderBy(x => x.Value.Used).Select(x => x.Key).ToArray())
        {
            if (entries.Count <= Math.Max(1, capacity)) break;
            entries.Remove(id); removed.Add(id);
        }
        return removed;
    }
    public void Clear() => entries.Clear();
}

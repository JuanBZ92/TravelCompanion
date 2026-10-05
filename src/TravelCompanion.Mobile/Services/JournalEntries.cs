using System.Globalization;
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

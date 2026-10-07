using System.Globalization;
using System.Text;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Mobile.Services;

public enum TripSearchKind { Reservation, Document, Memory }
public sealed record TripSearchResult(string Key, TripSearchKind Kind, string Title, string Context, DateOnly? Date, object Item)
{
    public string KindLabel => LocalizationResourceManager.Instance[Kind switch {
        TripSearchKind.Reservation => "TripSearchReservations", TripSearchKind.Document => "TripSearchDocuments", _ => "TripSearchMemories" }];
    public string Icon => Kind switch { TripSearchKind.Reservation => "tab_trip.svg", TripSearchKind.Document => "tab_docs.svg", _ => "tab_journal.svg" };
}

// Metadata and confirmed text only. No network calls or document/photo bytes are needed.
public sealed class TripSearchIndex(IEnumerable<TripSearchResult> results)
{
    private readonly (TripSearchResult Result, string Text)[] entries = results
        .DistinctBy(x => (x.Kind, x.Key)).Select(x => (x, Normalize(SearchText(x)))).ToArray();
    public IReadOnlyList<TripSearchResult> Find(string query, TripSearchKind? kind = null)
    {
        var terms = Normalize(query).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return entries.Where(x => (!kind.HasValue || x.Result.Kind == kind) && terms.All(term => x.Text.Contains(term, StringComparison.Ordinal)))
            .OrderBy(x => x.Result.Kind).ThenBy(x => x.Result.Date).ThenBy(x => x.Result.Title, StringComparer.CurrentCultureIgnoreCase)
            .Select(x => x.Result).ToArray();
    }
    private static string SearchText(TripSearchResult result) => string.Join(' ', result.Title, result.Context,
        result.Item is JournalMemory memory ? memory.Text : "", result.Date?.ToString("yyyy-MM-dd"), result.Date?.ToString("d/M"),
        result.Date?.ToString("dd/MM/yyyy"), result.Date?.ToString("d MMMM yyyy", CultureInfo.CurrentCulture));
    private static string Normalize(string text)
    {
        var builder = new StringBuilder(text.Length);
        foreach (var character in text.Normalize(NormalizationForm.FormD))
            if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark) builder.Append(char.ToLowerInvariant(character));
        return builder.ToString().Normalize(NormalizationForm.FormC);
    }
}

public sealed record TripSearchSource(string Name, Func<CancellationToken, Task<IReadOnlyList<TripSearchResult>>> Read);
public sealed record TripSearchLoadResult(TripSearchIndex Index, bool HasIncompleteSources);

// Each local source refreshes independently; a failed source retains its last usable snapshot.
public sealed class TripSearchSourceLoader
{
    private readonly Dictionary<string, IReadOnlyList<TripSearchResult>> snapshots = [];
    public async Task<TripSearchLoadResult> LoadAsync(IEnumerable<TripSearchSource> sources, Func<bool> isCurrent,
        CancellationToken ct, Action<TripSearchIndex>? publish = null, Action<string, Exception>? failed = null)
    {
        void Check() { ct.ThrowIfCancellationRequested(); if (!isCurrent()) throw new OperationCanceledException(); }
        var incomplete = false;
        foreach (var source in sources)
        {
            Check();
            try
            {
                var rows = await source.Read(ct).WaitAsync(ct);
                Check();
                snapshots[source.Name] = rows;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception exception)
            {
                Check();
                incomplete = true;
                failed?.Invoke(source.Name, exception);
            }
            Check();
            publish?.Invoke(new(snapshots.Values.SelectMany(rows => rows)));
        }
        Check();
        return new(new(snapshots.Values.SelectMany(rows => rows)), incomplete);
    }
}

using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Mobile.Services;

// Controlled I/O for tests that execute the production FreeMapStore.
public sealed class TravelCompanionApiClient
{
    public Task NotifyItineraryChangedAsync() => Task.CompletedTask;
    public Func<Task<ExpensesDto>> FetchExpenses = () => throw new HttpRequestException();
    public Func<Guid, SaveExpenseRequest, Task<SaveExpenseResult>> SaveExpense = (_, _) => throw new HttpRequestException();
    public Func<SaveExpenseSettingsRequest, Task<ExpenseSettingsDto?>> SaveExpenseSettings = _ => throw new HttpRequestException();
    public Task<ExpensesDto> GetExpensesAsync(string token, Guid trip, CancellationToken ct) => FetchExpenses();
    public Task<SaveExpenseResult> SaveExpenseAsync(string token, Guid trip, Guid id, SaveExpenseRequest value, CancellationToken ct) => SaveExpense(id, value);
    public Task<ExpenseSettingsDto?> SaveExpenseSettingsAsync(string token, Guid trip, SaveExpenseSettingsRequest value, CancellationToken ct) => SaveExpenseSettings(value);
    public Task<ExpenseRateDto?> GetExpenseRateAsync(string token, Guid trip, string currency, string target, DateOnly date, CancellationToken ct) => Task.FromResult<ExpenseRateDto?>(null);
    public Func<Task<List<JournalNoteDto>>> FetchJournal = () => Task.FromResult(new List<JournalNoteDto>());
    public Func<Task<List<JournalFreeEntryDto>>> FetchJournalFree = () => Task.FromResult(new List<JournalFreeEntryDto>());
    public Func<SaveJournalFreeEntryRequest, Task<JournalFreeSaveResult>> SaveJournalFree = _ => throw new HttpRequestException();
    public Func<DeleteJournalFreeEntryRequest, Task<JournalFreeSaveResult>> DeleteJournalFree = _ => throw new HttpRequestException();
    public Task<List<JournalFreeEntryDto>> GetJournalFreeAsync(string token, Guid trip, CancellationToken ct) => FetchJournalFree();
    public Task<JournalFreeSaveResult> SaveJournalFreeAsync(string token, Guid trip, Guid id, SaveJournalFreeEntryRequest request, CancellationToken ct) => SaveJournalFree(request);
    public Task<JournalFreeSaveResult> DeleteJournalFreeAsync(string token, Guid trip, Guid id, DeleteJournalFreeEntryRequest request, CancellationToken ct) => DeleteJournalFree(request);
    public Func<SaveJournalNoteRequest, Task<JournalSaveResult>> SaveJournal = _ => throw new HttpRequestException();
    public Task<List<JournalNoteDto>> GetJournalAsync(string token, Guid trip, CancellationToken ct) => FetchJournal();
    public Task<JournalSaveResult> SaveJournalAsync(string token, Guid trip, Guid activity, SaveJournalNoteRequest request, CancellationToken ct) => SaveJournal(request);
    public Uri BaseAddress { get; } = new("https://example.invalid/");
    public int CityRequests;
    public Func<Task<FreeMapPreviewDto?>> FetchCity = () => Task.FromResult<FreeMapPreviewDto?>(null);
    public Task<FreeMapPreviewDto?> GetFreeMapCityAsync(string token, string city, CancellationToken ct)
    {
        Interlocked.Increment(ref CityRequests);
        return FetchCity();
    }
    public Task<IReadOnlyList<FreeMapCityDto>?> GetFreeMapCitiesAsync(string token, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<FreeMapCityDto>?>([]);
}

public sealed class OfflineCacheService
{
    public readonly Dictionary<string, object> Entries = [];
    public readonly Dictionary<(string Language, string Key), object> LocalizedEntries = [];
    public int Reads;
    public int Writes;
    public Func<Task>? BeforeRead;
    public Func<Task>? BeforeSave;
    public Func<Task>? BeforeLocalizedRead;
    public async Task<OfflineCacheResult<T>?> GetAsync<T>(string key, TimeSpan? maxAge = null, CancellationToken cancellationToken = default)
    {
        Reads++;
        if (BeforeRead is not null) await BeforeRead();
        cancellationToken.ThrowIfCancellationRequested();
        return Entries.GetValueOrDefault(key) as OfflineCacheResult<T>;
    }
    public Task<OfflineCacheResult<T>?> GetAsync<T>(string key, CancellationToken ct) => GetAsync<T>(key, null, ct);
    public async Task<IReadOnlyList<OfflineCacheResult<T>>> GetLocalizedCopiesAsync<T>(string key, CancellationToken ct = default)
    {
        if (BeforeLocalizedRead is not null) await BeforeLocalizedRead();
        ct.ThrowIfCancellationRequested();
        return LocalizedEntries.Where(entry => entry.Key.Key == key)
            .OrderBy(entry => entry.Key.Language, StringComparer.Ordinal)
            .Select(entry => entry.Value).OfType<OfflineCacheResult<T>>().ToList();
    }
    public Task SaveAsync<T>(string key, T value, CancellationToken ct = default) => SaveAsync(key, value, new OfflineCacheMetadata(), ct);
    public async Task SaveAsync<T>(string key, T value, OfflineCacheMetadata metadata, CancellationToken ct = default)
    {
        Writes++;
        if (BeforeSave is not null) await BeforeSave();
        ct.ThrowIfCancellationRequested();
        Entries[key] = new OfflineCacheResult<T>(value, DateTimeOffset.UtcNow, metadata);
    }
    public Task DeleteAsync(string key) { Entries.Remove(key); return Task.CompletedTask; }
    public Task DeleteByPrefixAsync(params string[] prefixes)
    {
        foreach (var key in Entries.Keys.Where(key => prefixes.Any(prefix => key.StartsWith(prefix, StringComparison.Ordinal))).ToArray()) Entries.Remove(key);
        foreach (var key in LocalizedEntries.Keys.Where(key => prefixes.Any(prefix => key.Key.StartsWith(prefix, StringComparison.Ordinal))).ToArray()) LocalizedEntries.Remove(key);
        return Task.CompletedTask;
    }
}
public sealed record OfflineCacheResult<T>(T Value, DateTimeOffset SavedAt, OfflineCacheMetadata? Metadata = null);
public sealed record OfflineCacheMetadata;
public sealed class MobileSyncStateStore
{
    public Task<OfflineCacheMetadata> CreateCacheMetadataAsync(string scope, string version,
        Guid? destinationId = null, string? destinationSlug = null, CancellationToken cancellationToken = default) => Task.FromResult(new OfflineCacheMetadata());
}

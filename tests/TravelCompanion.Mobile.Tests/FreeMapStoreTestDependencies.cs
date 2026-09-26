using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Mobile.Services;

// Controlled I/O for tests that execute the production FreeMapStore.
public sealed class TravelCompanionApiClient
{
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
    public int Reads;
    public int Writes;
    public Task<OfflineCacheResult<T>?> GetAsync<T>(string key, TimeSpan? maxAge = null, CancellationToken cancellationToken = default)
    {
        Reads++;
        return Task.FromResult(Entries.GetValueOrDefault(key) as OfflineCacheResult<T>);
    }
    public Task<OfflineCacheResult<T>?> GetAsync<T>(string key, CancellationToken ct) => GetAsync<T>(key, null, ct);
    public Task SaveAsync<T>(string key, T value, CancellationToken ct = default) => SaveAsync(key, value, new OfflineCacheMetadata(), ct);
    public Task SaveAsync<T>(string key, T value, OfflineCacheMetadata metadata, CancellationToken ct = default)
    {
        Writes++;
        Entries[key] = new OfflineCacheResult<T>(value, DateTimeOffset.UtcNow, metadata);
        return Task.CompletedTask;
    }
    public Task DeleteAsync(string key) { Entries.Remove(key); return Task.CompletedTask; }
    public Task DeleteByPrefixAsync(params string[] prefixes)
    {
        foreach (var key in Entries.Keys.Where(key => prefixes.Any(prefix => key.StartsWith(prefix, StringComparison.Ordinal))).ToArray()) Entries.Remove(key);
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

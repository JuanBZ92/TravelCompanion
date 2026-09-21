using System.Globalization;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Mobile.Services;

public sealed class FreeMapStore(
    TravelCompanionApiClient apiClient,
    OfflineCacheService offlineCacheService,
    MobileSyncStateStore syncStateStore,
    AuthSessionService sessionService)
{
    private const string Prefix = "free-map-v2-";
    private readonly SemaphoreSlim _cacheGate = new(1, 1);
    private readonly object _requestLock = new();
    private readonly Dictionary<string, Task<object?>> _requests = [];
    private long _generation;
    private string? _memoryScope;
    private OfflineCacheResult<IReadOnlyList<FreeMapCityDto>>? _cities;
    private OfflineCacheResult<FreeMapPreviewDto>? _city;

    // Quotas and itinerary revisions do not change catalog identity.
    public string ContextKey => $"{sessionService.HasSession}-{sessionService.CurrentUserId:N}-{sessionService.CurrentTripId:N}-{sessionService.AccessMode}-{sessionService.ExperienceMode}-{CultureInfo.CurrentUICulture.TwoLetterISOLanguageName}";
    public long Generation => Interlocked.Read(ref _generation);

    public bool HasFreshSnapshot(string citySlug) => sessionService.HasSession && sessionService.HasKnownValidAccess
        && _memoryScope == ContextKey && _city?.Value.City.Slug == citySlug
        && DateTimeOffset.UtcNow - _city.SavedAt < TimeSpan.FromMinutes(5);

    public Task<OfflineCacheResult<IReadOnlyList<FreeMapCityDto>>?> GetCachedCitiesAsync(CancellationToken cancellationToken = default) =>
        ReadAsync<IReadOnlyList<FreeMapCityDto>>("cities", cancellationToken);

    public Task<OfflineCacheResult<FreeMapPreviewDto>?> GetCachedCityAsync(string citySlug, CancellationToken cancellationToken = default) =>
        ReadAsync<FreeMapPreviewDto>($"city-{citySlug}", cancellationToken);

    private async Task<OfflineCacheResult<T>?> ReadAsync<T>(string resource, CancellationToken cancellationToken)
    {
        var scope = ContextKey;
        var generation = Generation;
        var sessionVersion = sessionService.ContextVersion;
        await _cacheGate.WaitAsync(cancellationToken);
        try
        {
            if (!IsCurrent(scope, generation) || sessionVersion != sessionService.ContextVersion) return null;
            ResetMemoryScope(scope);
            var memory = resource == "cities" ? (object?)_cities : _city?.Value.City.Slug == resource[5..] ? _city : null;
            if (memory is OfflineCacheResult<T> result) return result;
            var cached = await offlineCacheService.GetAsync<T>($"{Prefix}{scope}-{resource}", cancellationToken);
            if (!IsCurrent(scope, generation) || sessionVersion != sessionService.ContextVersion) return null;
            Remember(cached);
            return cached;
        }
        finally { _cacheGate.Release(); }
    }

    public Task<IReadOnlyList<FreeMapCityDto>?> RefreshCitiesAsync(string token, CancellationToken cancellationToken = default) =>
        RefreshAsync("cities", ct => apiClient.GetFreeMapCitiesAsync(token, ct), cancellationToken);

    public Task<FreeMapPreviewDto?> RefreshCityAsync(string token, string citySlug, CancellationToken cancellationToken = default) =>
        RefreshAsync($"city-{citySlug}", ct => apiClient.GetFreeMapCityAsync(token, citySlug, ct), cancellationToken);

    private async Task<T?> RefreshAsync<T>(string resource, Func<CancellationToken, Task<T?>> fetch, CancellationToken cancellationToken) where T : class
    {
        var scope = ContextKey;
        var generation = Generation;
        var sessionVersion = sessionService.ContextVersion;
        var key = $"{generation}-{sessionVersion}-{scope}-{resource}";
        Task<object?> request;
        lock (_requestLock)
        {
            if (!_requests.TryGetValue(key, out request!))
            {
                request = FetchAsync();
                _requests[key] = request;
                _ = request.ContinueWith(completed =>
                {
                    _ = completed.Exception;
                    lock (_requestLock)
                    {
                        if (_requests.GetValueOrDefault(key) == completed) _requests.Remove(key);
                    }
                }, CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
            }
        }
        try { return (T?)await request.WaitAsync(cancellationToken); }
        finally
        {
            lock (_requestLock)
            {
                if (request.IsCompleted && _requests.GetValueOrDefault(key) == request) _requests.Remove(key);
            }
        }

        async Task<object?> FetchAsync()
        {
            // A disappearing tab cancels its wait, not a request shared by another consumer.
            var value = await fetch(CancellationToken.None);
            await _cacheGate.WaitAsync();
            try
            {
                if (value is null || !IsCurrent(scope, generation) || sessionVersion != sessionService.ContextVersion) return null;
                var metadata = await syncStateStore.CreateCacheMetadataAsync("free-catalog",
                    $"downloaded:{DateTimeOffset.UtcNow.UtcTicks}", destinationSlug: (value as FreeMapPreviewDto)?.City.Slug);
                if (!IsCurrent(scope, generation) || sessionVersion != sessionService.ContextVersion) return null;
                var cacheKey = $"{Prefix}{scope}-{resource}";
                await offlineCacheService.SaveAsync(cacheKey, value, metadata);
                if (!IsCurrent(scope, generation) || sessionVersion != sessionService.ContextVersion)
                {
                    await offlineCacheService.DeleteAsync(cacheKey);
                    return null;
                }
                ResetMemoryScope(scope);
                Remember(new OfflineCacheResult<T>(value, DateTimeOffset.UtcNow, metadata));
                return value;
            }
            finally { _cacheGate.Release(); }
        }
    }

    public async Task ClearAsync()
    {
        Interlocked.Increment(ref _generation);
        await _cacheGate.WaitAsync();
        try
        {
            _cities = null;
            _city = null;
            _memoryScope = null;
            await offlineCacheService.DeleteByPrefixAsync(Prefix, "free-map-city-", "free-map-cities");
        }
        finally { _cacheGate.Release(); }
    }

    private bool IsCurrent(string scope, long generation) => sessionService.HasSession
        && sessionService.HasKnownValidAccess && scope == ContextKey && generation == Generation;

    private void ResetMemoryScope(string scope)
    {
        if (_memoryScope == scope) return;
        _cities = null;
        _city = null;
        _memoryScope = scope;
    }

    private void Remember<T>(OfflineCacheResult<T>? result)
    {
        if (result is OfflineCacheResult<IReadOnlyList<FreeMapCityDto>> cities) _cities = cities;
        if (result is OfflineCacheResult<FreeMapPreviewDto> city) _city = city;
    }
}

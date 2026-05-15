using System.Diagnostics;
using Microsoft.Extensions.Logging;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Mobile.Services;

public sealed class MobileBootstrapStore(
    TravelCompanionApiClient apiClient,
    AuthSessionService sessionService,
    OfflineCacheService offlineCacheService,
    ILogger<MobileBootstrapStore> logger)
{
    private static readonly TimeSpan DefaultFreshnessWindow = TimeSpan.FromMinutes(2);
    private MobileBootstrapDto? _current;
    private DateTimeOffset? _currentSavedAt;
    private Guid? _currentUserId;
    private readonly object _refreshLock = new();
    private Task<MobileBootstrapDto?>? _refreshTask;

    public async Task<OfflineCacheResult<MobileBootstrapDto>?> GetCachedAsync(
        string? destinationSlug = null,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var currentUserId = sessionService.CurrentUserId;
        var cacheScope = NormalizeCacheScope(destinationSlug);
        if (_current is not null
            && _currentUserId == currentUserId
            && _currentSavedAt.HasValue
            && IsScopeMatch(cacheScope, _current.Destination.Slug))
        {
            stopwatch.Stop();
            logger.LogInformation(
                "Mobile bootstrap memory cache hit in {ElapsedMs}ms. Scope={CacheScope}.",
                stopwatch.Elapsed.TotalMilliseconds,
                cacheScope);
            return new OfflineCacheResult<MobileBootstrapDto>(_current, _currentSavedAt.Value);
        }

        var cached = await offlineCacheService.GetAsync<MobileBootstrapDto>(
            GetCacheKey(currentUserId, cacheScope),
            cancellationToken).ConfigureAwait(false);
        stopwatch.Stop();

        logger.LogInformation(
            "Mobile bootstrap disk cache {CacheResult} in {ElapsedMs}ms. Scope={CacheScope}.",
            cached is null ? "miss" : "hit",
            stopwatch.Elapsed.TotalMilliseconds,
            cacheScope);

        if (cached is not null)
        {
            _current = cached.Value;
            _currentSavedAt = cached.SavedAt;
            _currentUserId = currentUserId;
        }

        return cached;
    }

    public async Task<MobileBootstrapDto?> RefreshAsync(
        string token,
        string? destinationSlug = null,
        CancellationToken cancellationToken = default)
    {
        Task<MobileBootstrapDto?> refreshTask;
        lock (_refreshLock)
        {
            if (_refreshTask is null || _refreshTask.IsCompleted)
            {
                _refreshTask = RefreshCoreAsync(token, destinationSlug, cancellationToken);
            }
            else
            {
                logger.LogInformation("Mobile bootstrap refresh joined existing in-flight request.");
            }

            refreshTask = _refreshTask;
        }

        try
        {
            return await refreshTask.ConfigureAwait(false);
        }
        finally
        {
            if (refreshTask.IsCompleted)
            {
                lock (_refreshLock)
                {
                    if (ReferenceEquals(_refreshTask, refreshTask))
                    {
                        _refreshTask = null;
                    }
                }
            }
        }
    }

    private async Task<MobileBootstrapDto?> RefreshCoreAsync(
        string token,
        string? destinationSlug,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var bootstrap = await apiClient.GetMobileBootstrapAsync(token, destinationSlug, cancellationToken).ConfigureAwait(false);
        if (bootstrap is null)
        {
            stopwatch.Stop();
            logger.LogWarning(
                "Mobile bootstrap refresh returned no data after {ElapsedMs}ms.",
                stopwatch.Elapsed.TotalMilliseconds);
            return null;
        }

        var savedAt = DateTimeOffset.UtcNow;
        var currentUserId = sessionService.CurrentUserId;
        var requestedCacheScope = NormalizeCacheScope(destinationSlug);
        var destinationCacheScope = NormalizeCacheScope(bootstrap.Destination.Slug);
        await offlineCacheService.SaveAsync(
            GetCacheKey(currentUserId, requestedCacheScope),
            bootstrap,
            cancellationToken).ConfigureAwait(false);
        if (!string.Equals(requestedCacheScope, destinationCacheScope, StringComparison.Ordinal))
        {
            await offlineCacheService.SaveAsync(
                GetCacheKey(currentUserId, destinationCacheScope),
                bootstrap,
                cancellationToken).ConfigureAwait(false);
        }
        stopwatch.Stop();

        _current = bootstrap;
        _currentSavedAt = savedAt;
        _currentUserId = currentUserId;

        logger.LogInformation(
            "Mobile bootstrap refreshed and cached in {ElapsedMs}ms. Scope={CacheScope}; Recommendations={RecommendationCount}; Packages={PackageCount}; HasSchedule={HasSchedule}.",
            stopwatch.Elapsed.TotalMilliseconds,
            destinationCacheScope,
            bootstrap.Recommendations.Count,
            bootstrap.Packages.Count,
            bootstrap.Schedule is not null);

        return bootstrap;
    }

    public bool HasFreshSnapshot(string? destinationSlug = null, TimeSpan? maxAge = null)
    {
        var currentUserId = sessionService.CurrentUserId;
        var cacheScope = NormalizeCacheScope(destinationSlug);
        var ageLimit = maxAge ?? DefaultFreshnessWindow;

        return _current is not null
            && _currentUserId == currentUserId
            && _currentSavedAt.HasValue
            && DateTimeOffset.UtcNow - _currentSavedAt.Value <= ageLimit
            && IsScopeMatch(cacheScope, _current.Destination.Slug);
    }

    private static string GetCacheKey(Guid? userId, string cacheScope)
    {
        return $"mobile-bootstrap-{cacheScope}-{userId?.ToString() ?? "anonymous"}";
    }

    private static string NormalizeCacheScope(string? destinationSlug)
    {
        if (string.IsNullOrWhiteSpace(destinationSlug))
        {
            return "auto";
        }

        return destinationSlug.Trim().ToLowerInvariant();
    }

    private static bool IsScopeMatch(string requestedScope, string destinationSlug)
    {
        return requestedScope == "auto"
            || string.Equals(NormalizeCacheScope(destinationSlug), requestedScope, StringComparison.Ordinal);
    }
}

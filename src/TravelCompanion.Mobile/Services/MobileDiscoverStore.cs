using System.Diagnostics;
using Microsoft.Extensions.Logging;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Mobile.Services;

public sealed class MobileDiscoverStore(
    TravelCompanionApiClient apiClient,
    AuthSessionService sessionService,
    OfflineCacheService offlineCacheService,
    ILogger<MobileDiscoverStore> logger)
{
    private static readonly TimeSpan DefaultFreshnessWindow = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan DiskCacheMaxAge = TimeSpan.FromHours(6);
    private MobileDiscoverDto? _current;
    private DateTimeOffset? _currentSavedAt;
    private Guid? _currentUserId;

    public async Task<OfflineCacheResult<MobileDiscoverDto>?> GetCachedAsync(
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
                "Mobile discover memory cache hit in {ElapsedMs}ms. Scope={CacheScope}.",
                stopwatch.Elapsed.TotalMilliseconds,
                cacheScope);
            return new OfflineCacheResult<MobileDiscoverDto>(_current, _currentSavedAt.Value);
        }

        var cached = await offlineCacheService.GetAsync<MobileDiscoverDto>(
            GetCacheKey(currentUserId, cacheScope),
            DiskCacheMaxAge,
            cancellationToken).ConfigureAwait(false);
        stopwatch.Stop();

        logger.LogInformation(
            "Mobile discover disk cache {CacheResult} in {ElapsedMs}ms. Scope={CacheScope}.",
            cached is null ? "miss" : "hit",
            stopwatch.Elapsed.TotalMilliseconds,
            cacheScope);

        if (cached is not null)
        {
            var normalized = MobilePayloadNormalizer.Normalize(cached.Value);
            if (normalized is null)
            {
                logger.LogWarning("Mobile discover disk cache ignored because it is incomplete. Scope={CacheScope}.", cacheScope);
                return null;
            }

            _current = normalized;
            _currentSavedAt = cached.SavedAt;
            _currentUserId = currentUserId;
            return new OfflineCacheResult<MobileDiscoverDto>(normalized, cached.SavedAt);
        }

        return cached;
    }

    public async Task<MobileDiscoverDto?> RefreshAsync(
        string token,
        string? destinationSlug = null,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var discover = await apiClient.GetMobileDiscoverAsync(token, destinationSlug, cancellationToken).ConfigureAwait(false);
        if (discover is null)
        {
            stopwatch.Stop();
            logger.LogWarning(
                "Mobile discover refresh returned no data after {ElapsedMs}ms.",
                stopwatch.Elapsed.TotalMilliseconds);
            return null;
        }

        var savedAt = DateTimeOffset.UtcNow;
        var currentUserId = sessionService.CurrentUserId;
        var requestedCacheScope = NormalizeCacheScope(destinationSlug);
        var destinationCacheScope = NormalizeCacheScope(discover.Destination.Slug);
        await offlineCacheService.SaveAsync(
            GetCacheKey(currentUserId, requestedCacheScope),
            discover,
            cancellationToken).ConfigureAwait(false);
        if (!string.Equals(requestedCacheScope, destinationCacheScope, StringComparison.Ordinal))
        {
            await offlineCacheService.SaveAsync(
                GetCacheKey(currentUserId, destinationCacheScope),
                discover,
                cancellationToken).ConfigureAwait(false);
        }
        stopwatch.Stop();

        _current = discover;
        _currentSavedAt = savedAt;
        _currentUserId = currentUserId;

        logger.LogInformation(
            "Mobile discover refreshed and cached in {ElapsedMs}ms. Scope={CacheScope}; Recommendations={RecommendationCount}.",
            stopwatch.Elapsed.TotalMilliseconds,
            destinationCacheScope,
            discover.Recommendations.Count);

        return discover;
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

    public async Task ClearUserCacheAsync(Guid? userId, CancellationToken cancellationToken = default)
    {
        if (_currentUserId == userId)
        {
            _current = null;
            _currentSavedAt = null;
            _currentUserId = null;
        }

        await offlineCacheService.DeleteByPrefixAndSuffixAsync(
            "mobile-discover-",
            GetCacheKeySuffix(userId)).ConfigureAwait(false);
    }

    private static string GetCacheKey(Guid? userId, string cacheScope)
    {
        return $"mobile-discover-{cacheScope}{GetCacheKeySuffix(userId)}";
    }

    private static string GetCacheKeySuffix(Guid? userId) =>
        $"-{userId?.ToString() ?? "anonymous"}";

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

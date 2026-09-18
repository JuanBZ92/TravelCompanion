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
    private MobileDiscoverDto? _current;
    private DateTimeOffset? _currentSavedAt;
    private Guid? _currentUserId;
    private Guid? _currentTripId;
    private string? _currentLocale;
    private static string Locale => System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
    private readonly object _refreshLock = new();
    private Task<ApiCallResult<MobileDiscoverDto>>? _refreshTask;
    private string? _refreshKey;

    public async Task<OfflineCacheResult<MobileDiscoverDto>?> GetCachedAsync(
        string? destinationSlug = null,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var currentUserId = sessionService.CurrentUserId;
        var currentTripId = sessionService.CurrentTripId;
        var cacheScope = NormalizeCacheScope(destinationSlug);
        if (_current is not null
            && _currentLocale == Locale
            && _currentUserId == currentUserId
            && _currentTripId == currentTripId
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
            GetCacheKey(currentUserId, currentTripId, cacheScope),
            maxAge: null,
            cancellationToken: cancellationToken).ConfigureAwait(false);
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
            _currentLocale = Locale;
            _currentSavedAt = cached.SavedAt;
            _currentUserId = currentUserId;
            _currentTripId = currentTripId;
            return new OfflineCacheResult<MobileDiscoverDto>(normalized, cached.SavedAt);
        }

        return cached;
    }

    public async Task<MobileDiscoverDto?> RefreshAsync(
        string token,
        string? destinationSlug = null,
        CancellationToken cancellationToken = default)
    {
        var result = await RefreshResultAsync(token, destinationSlug, cancellationToken).ConfigureAwait(false);
        return result.Value;
    }

    public async Task<ApiCallResult<MobileDiscoverDto>> RefreshResultAsync(
        string token,
        string? destinationSlug = null,
        CancellationToken cancellationToken = default)
    {
        var context = CaptureContext(destinationSlug);
        Task<ApiCallResult<MobileDiscoverDto>> refreshTask;
        lock (_refreshLock)
        {
            if (_refreshTask is null || _refreshTask.IsCompleted || !string.Equals(_refreshKey, context.Key, StringComparison.Ordinal))
            {
                _refreshKey = context.Key;
                _refreshTask = RefreshCoreAsync(token, destinationSlug, context, cancellationToken);
            }
            else
            {
                logger.LogInformation("Mobile discover refresh joined existing in-flight request.");
            }
            refreshTask = _refreshTask;
        }

        try
        {
            return await refreshTask.WaitAsync(cancellationToken).ConfigureAwait(false);
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
                        _refreshKey = null;
                    }
                }
            }
        }
    }

    private async Task<ApiCallResult<MobileDiscoverDto>> RefreshCoreAsync(
        string token,
        string? destinationSlug,
        CacheContext context,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var result = await apiClient.GetMobileDiscoverResultAsync(token, destinationSlug, cancellationToken).ConfigureAwait(false);
        if (!result.IsSuccess || result.Value is not { } discover)
        {
            stopwatch.Stop();
            logger.LogWarning(
                "Mobile discover refresh returned no data after {ElapsedMs}ms.",
                stopwatch.Elapsed.TotalMilliseconds);
            return result;
        }

        var savedAt = DateTimeOffset.UtcNow;
        if (!IsCurrent(context))
        {
            logger.LogInformation("Discarded mobile discover response because the session context changed.");
            return ApiCallResult<MobileDiscoverDto>.TransientFailure();
        }

        var currentUserId = context.UserId;
        var requestedCacheScope = NormalizeCacheScope(destinationSlug);
        var destinationCacheScope = NormalizeCacheScope(discover.Destination.Slug);
        var requestedCacheKey = GetCacheKey(currentUserId, context.TripId, requestedCacheScope);
        var destinationCacheKey = GetCacheKey(currentUserId, context.TripId, destinationCacheScope);

        _current = discover;
        _currentSavedAt = savedAt;
        _currentUserId = currentUserId;
        _currentTripId = context.TripId;
        _currentLocale = context.Locale;

        await offlineCacheService.SaveAsync(
            requestedCacheKey,
            discover,
            cancellationToken).ConfigureAwait(false);
        if (!string.Equals(requestedCacheScope, destinationCacheScope, StringComparison.Ordinal))
        {
            await offlineCacheService.SaveAsync(
                destinationCacheKey,
                discover,
                cancellationToken).ConfigureAwait(false);
        }
        if (!IsCurrent(context))
        {
            await offlineCacheService.DeleteAsync(requestedCacheKey).ConfigureAwait(false);
            if (!string.Equals(requestedCacheKey, destinationCacheKey, StringComparison.Ordinal))
            {
                await offlineCacheService.DeleteAsync(destinationCacheKey).ConfigureAwait(false);
            }
            logger.LogInformation("Removed mobile discover cache written after the session context changed.");
            return ApiCallResult<MobileDiscoverDto>.TransientFailure();
        }
        stopwatch.Stop();

        logger.LogInformation(
            "Mobile discover refreshed and cached in {ElapsedMs}ms. Scope={CacheScope}; Recommendations={RecommendationCount}.",
            stopwatch.Elapsed.TotalMilliseconds,
            destinationCacheScope,
            discover.Recommendations.Count);

        return result;
    }

    public bool HasFreshSnapshot(string? destinationSlug = null, TimeSpan? maxAge = null)
    {
        var currentUserId = sessionService.CurrentUserId;
        var cacheScope = NormalizeCacheScope(destinationSlug);
        var ageLimit = maxAge ?? DefaultFreshnessWindow;

        return _current is not null
            && _currentLocale == Locale
            && _currentUserId == currentUserId
            && _currentTripId == sessionService.CurrentTripId
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
            _currentTripId = null;
            _currentLocale = null;
        }

        await offlineCacheService.DeleteByPrefixAndSuffixAsync(
            "mobile-discover-",
            GetCacheKeySuffix(userId)).ConfigureAwait(false);
    }

    private static string GetCacheKey(Guid? userId, Guid? tripId, string cacheScope)
    {
        return $"mobile-discover-{cacheScope}-trip-{tripId?.ToString() ?? "auto"}{GetCacheKeySuffix(userId)}";
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

    private CacheContext CaptureContext(string? destinationSlug)
    {
        var userId = sessionService.CurrentUserId;
        var tripId = sessionService.CurrentTripId;
        var locale = Locale;
        var scope = NormalizeCacheScope(destinationSlug);
        var contextVersion = sessionService.ContextVersion;
        return new CacheContext(userId, tripId, locale, contextVersion, $"{contextVersion}:{userId}:{tripId}:{locale}:{scope}");
    }

    private bool IsCurrent(CacheContext context) =>
        context.UserId == sessionService.CurrentUserId
        && context.TripId == sessionService.CurrentTripId
        && context.ContextVersion == sessionService.ContextVersion
        && string.Equals(context.Locale, Locale, StringComparison.Ordinal);

    private sealed record CacheContext(Guid? UserId, Guid? TripId, string Locale, long ContextVersion, string Key);
}

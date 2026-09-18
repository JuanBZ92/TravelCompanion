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
    private Guid? _currentTripId;
    private string? _currentLocale;
    private static string Locale => System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
    private readonly object _refreshLock = new();
    private Task<ApiCallResult<MobileBootstrapDto>>? _refreshTask;
    private string? _refreshKey;

    public event EventHandler<ScheduleCacheUpdatedEventArgs>? ScheduleUpdated;

    public async Task<OfflineCacheResult<MobileBootstrapDto>?> GetCachedAsync(
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
                "Mobile bootstrap memory cache hit in {ElapsedMs}ms. Scope={CacheScope}.",
                stopwatch.Elapsed.TotalMilliseconds,
                cacheScope);
            return new OfflineCacheResult<MobileBootstrapDto>(_current, _currentSavedAt.Value);
        }

        var cached = await offlineCacheService.GetAsync<MobileBootstrapDto>(
            GetCacheKey(currentUserId, currentTripId, cacheScope),
            maxAge: null,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        stopwatch.Stop();

        logger.LogInformation(
            "Mobile bootstrap disk cache {CacheResult} in {ElapsedMs}ms. Scope={CacheScope}.",
            cached is null ? "miss" : "hit",
            stopwatch.Elapsed.TotalMilliseconds,
            cacheScope);

        if (cached is not null)
        {
            var normalized = MobilePayloadNormalizer.Normalize(cached.Value);
            if (normalized is null)
            {
                logger.LogWarning("Mobile bootstrap disk cache ignored because it is incomplete. Scope={CacheScope}.", cacheScope);
                return null;
            }

            _current = normalized;
            _currentLocale = Locale;
            _currentSavedAt = cached.SavedAt;
            _currentUserId = currentUserId;
            _currentTripId = currentTripId;
            return new OfflineCacheResult<MobileBootstrapDto>(normalized, cached.SavedAt);
        }

        return cached;
    }

    public async Task<MobileBootstrapDto?> RefreshAsync(
        string token,
        string? destinationSlug = null,
        CancellationToken cancellationToken = default)
    {
        var result = await RefreshResultAsync(token, destinationSlug, cancellationToken).ConfigureAwait(false);
        return result.Value;
    }

    public async Task<ApiCallResult<MobileBootstrapDto>> RefreshResultAsync(
        string token,
        string? destinationSlug = null,
        CancellationToken cancellationToken = default)
    {
        var context = CaptureContext(destinationSlug);
        Task<ApiCallResult<MobileBootstrapDto>> refreshTask;
        lock (_refreshLock)
        {
            if (_refreshTask is null || _refreshTask.IsCompleted || !string.Equals(_refreshKey, context.Key, StringComparison.Ordinal))
            {
                _refreshKey = context.Key;
                _refreshTask = RefreshCoreAsync(token, destinationSlug, context, cancellationToken);
            }
            else
            {
                logger.LogInformation("Mobile bootstrap refresh joined existing in-flight request.");
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

    private async Task<ApiCallResult<MobileBootstrapDto>> RefreshCoreAsync(
        string token,
        string? destinationSlug,
        CacheContext context,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var result = await apiClient.GetMobileBootstrapResultAsync(token, destinationSlug, cancellationToken).ConfigureAwait(false);
        if (!result.IsSuccess || result.Value is not { } bootstrap)
        {
            stopwatch.Stop();
            logger.LogWarning(
                "Mobile bootstrap refresh returned no data after {ElapsedMs}ms.",
                stopwatch.Elapsed.TotalMilliseconds);
            return result;
        }

        var savedAt = DateTimeOffset.UtcNow;
        if (!IsCurrent(context))
        {
            logger.LogInformation("Discarded mobile bootstrap response because the session context changed.");
            return ApiCallResult<MobileBootstrapDto>.TransientFailure();
        }

        var currentUserId = context.UserId;
        var requestedCacheScope = NormalizeCacheScope(destinationSlug);
        var destinationCacheScope = NormalizeCacheScope(bootstrap.Destination.Slug);
        var requestedCacheKey = GetCacheKey(currentUserId, context.TripId, requestedCacheScope);
        var destinationCacheKey = GetCacheKey(currentUserId, context.TripId, destinationCacheScope);

        _current = bootstrap;
        _currentLocale = context.Locale;
        _currentSavedAt = savedAt;
        _currentUserId = currentUserId;
        _currentTripId = context.TripId;

        await offlineCacheService.SaveAsync(
            requestedCacheKey,
            bootstrap,
            cancellationToken).ConfigureAwait(false);
        if (!string.Equals(requestedCacheScope, destinationCacheScope, StringComparison.Ordinal))
        {
            await offlineCacheService.SaveAsync(
                destinationCacheKey,
                bootstrap,
                cancellationToken).ConfigureAwait(false);
        }
        if (!IsCurrent(context))
        {
            await offlineCacheService.DeleteAsync(requestedCacheKey).ConfigureAwait(false);
            if (!string.Equals(requestedCacheKey, destinationCacheKey, StringComparison.Ordinal))
            {
                await offlineCacheService.DeleteAsync(destinationCacheKey).ConfigureAwait(false);
            }
            logger.LogInformation("Removed mobile bootstrap cache written after the session context changed.");
            return ApiCallResult<MobileBootstrapDto>.TransientFailure();
        }
        stopwatch.Stop();

        logger.LogInformation(
            "Mobile bootstrap refreshed and cached in {ElapsedMs}ms. Scope={CacheScope}; Recommendations={RecommendationCount}; Packages={PackageCount}; HasSchedule={HasSchedule}.",
            stopwatch.Elapsed.TotalMilliseconds,
            destinationCacheScope,
            bootstrap.Recommendations.Count,
            bootstrap.Packages.Count,
            bootstrap.Schedule is not null);

        return result;
    }

    public bool HasFreshSnapshot(string? destinationSlug = null, TimeSpan? maxAge = null)
    {
        var currentUserId = sessionService.CurrentUserId;
        var currentTripId = sessionService.CurrentTripId;
        var cacheScope = NormalizeCacheScope(destinationSlug);
        var ageLimit = maxAge ?? DefaultFreshnessWindow;

        return _current is not null
            && _currentLocale == Locale
            && _currentUserId == currentUserId
            && _currentTripId == currentTripId
            && _currentSavedAt.HasValue
            && DateTimeOffset.UtcNow - _currentSavedAt.Value <= ageLimit
            && IsScopeMatch(cacheScope, _current.Destination.Slug);
    }

    public async Task<bool> UpsertScheduleItemAsync(
        ScheduleItemDto item,
        CancellationToken cancellationToken = default)
    {
        var currentUserId = sessionService.CurrentUserId;
        var currentTripId = sessionService.CurrentTripId;
        if (_current is null
            || _currentUserId != currentUserId
            || _currentTripId != currentTripId
            || _current.Schedule is null)
        {
            logger.LogInformation(
                "Skipped schedule cache update because bootstrap cache is not ready. ItemId={ScheduleItemId}.",
                item.Id);
            return false;
        }

        var schedule = _current.Schedule;
        var items = schedule.Items
            .Where(existing => existing.Id != item.Id)
            .Append(item)
            .OrderBy(existing => existing.Date)
            .ThenBy(existing => existing.StartsAt)
            .ToList();
        var updatedSchedule = schedule with
        {
            Items = items
        };
        var updatedBootstrap = _current with
        {
            Schedule = updatedSchedule
        };
        var savedAt = DateTimeOffset.UtcNow;
        var destinationCacheScope = NormalizeCacheScope(updatedBootstrap.Destination.Slug);

        _current = updatedBootstrap;
        _currentSavedAt = savedAt;

        await offlineCacheService.SaveAsync(
            GetCacheKey(currentUserId, currentTripId, "auto"),
            updatedBootstrap,
            cancellationToken).ConfigureAwait(false);
        await offlineCacheService.SaveAsync(
            GetCacheKey(currentUserId, currentTripId, destinationCacheScope),
            updatedBootstrap,
            cancellationToken).ConfigureAwait(false);

        MainThread.BeginInvokeOnMainThread(() =>
            ScheduleUpdated?.Invoke(this, new ScheduleCacheUpdatedEventArgs(updatedSchedule, savedAt)));

        logger.LogInformation(
            "Schedule cache updated after assistant save. ItemId={ScheduleItemId}; TotalItems={ScheduleItemCount}.",
            item.Id,
            items.Count);
        return true;
    }

    public async Task ClearUserCacheAsync(Guid? userId, CancellationToken cancellationToken = default)
    {
        if (_currentUserId == userId)
        {
            _current = null;
            _currentSavedAt = null;
            _currentUserId = null;
            _currentTripId = null;
        }

        await offlineCacheService.DeleteByPrefixAndSuffixAsync(
            "mobile-bootstrap-",
            GetCacheKeySuffix(userId)).ConfigureAwait(false);
    }

    private static string GetCacheKey(Guid? userId, Guid? tripId, string cacheScope)
    {
        return $"mobile-bootstrap-{cacheScope}-trip-{tripId?.ToString() ?? "auto"}{GetCacheKeySuffix(userId)}";
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

public sealed record ScheduleCacheUpdatedEventArgs(
    TripScheduleDto Schedule,
    DateTimeOffset SavedAt);

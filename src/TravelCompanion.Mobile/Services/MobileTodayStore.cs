using System.Diagnostics;
using Microsoft.Extensions.Logging;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Mobile.Services;

public sealed class MobileTodayStore(
    TravelCompanionApiClient apiClient,
    AuthSessionService sessionService,
    OfflineCacheService offlineCacheService,
    ILogger<MobileTodayStore> logger)
{
    private static readonly TimeSpan DefaultFreshnessWindow = TimeSpan.FromMinutes(2);
    private TodayDto? _current;
    private DateTimeOffset? _currentSavedAt;
    private Guid? _currentUserId;
    private Guid? _currentTripId;
    private DateOnly? _currentDate;
    private string? _currentLocale;
    private static string Locale => System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
    private readonly object _refreshLock = new();
    private Task<ApiCallResult<TodayDto>>? _refreshTask;
    private string? _refreshKey;

    public async Task<OfflineCacheResult<TodayDto>?> GetCachedAsync(
        DateOnly date,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var currentUserId = sessionService.CurrentUserId;
        var currentTripId = sessionService.CurrentTripId;
        if (_current is not null
            && _currentLocale == Locale
            && _currentUserId == currentUserId
            && _currentTripId == currentTripId
            && _currentDate == date
            && _currentSavedAt.HasValue)
        {
            stopwatch.Stop();
            logger.LogInformation(
                "Mobile today memory cache hit in {ElapsedMs}ms. Date={Date}.",
                stopwatch.Elapsed.TotalMilliseconds,
                date);
            return new OfflineCacheResult<TodayDto>(_current, _currentSavedAt.Value);
        }

        var cached = await offlineCacheService.GetAsync<TodayDto>(
            GetCacheKey(currentUserId, currentTripId, date),
            maxAge: null,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        stopwatch.Stop();

        logger.LogInformation(
            "Mobile today disk cache {CacheResult} in {ElapsedMs}ms. Date={Date}.",
            cached is null ? "miss" : "hit",
            stopwatch.Elapsed.TotalMilliseconds,
            date);

        if (cached is not null)
        {
            var normalized = MobilePayloadNormalizer.Normalize(cached.Value);
            if (normalized is null)
            {
                return null;
            }

            _current = normalized;
            _currentLocale = Locale;
            _currentSavedAt = cached.SavedAt;
            _currentUserId = currentUserId;
            _currentTripId = currentTripId;
            _currentDate = date;
            return new OfflineCacheResult<TodayDto>(normalized, cached.SavedAt);
        }

        return null;
    }

    public async Task<TodayDto?> RefreshAsync(
        string token,
        DateOnly date,
        GeoPointDto? currentLocation,
        CancellationToken cancellationToken = default)
    {
        var result = await RefreshResultAsync(token, date, currentLocation, cancellationToken).ConfigureAwait(false);
        return result.Value;
    }

    public async Task<ApiCallResult<TodayDto>> RefreshResultAsync(
        string token,
        DateOnly date,
        GeoPointDto? currentLocation,
        CancellationToken cancellationToken = default)
    {
        var context = CaptureContext(date, currentLocation);
        Task<ApiCallResult<TodayDto>> refreshTask;
        lock (_refreshLock)
        {
            if (_refreshTask is null || _refreshTask.IsCompleted || !string.Equals(_refreshKey, context.Key, StringComparison.Ordinal))
            {
                _refreshKey = context.Key;
                _refreshTask = RefreshCoreAsync(token, date, currentLocation, context, cancellationToken);
            }
            else
            {
                logger.LogInformation("Mobile today refresh joined existing in-flight request. Date={Date}.", date);
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

    private async Task<ApiCallResult<TodayDto>> RefreshCoreAsync(
        string token,
        DateOnly date,
        GeoPointDto? currentLocation,
        CacheContext context,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var result = await apiClient.GetMobileTodayResultAsync(token, date, currentLocation, cancellationToken).ConfigureAwait(false);
        if (!result.IsSuccess || result.Value is not { } today)
        {
            stopwatch.Stop();
            logger.LogWarning(
                "Mobile today refresh returned no data after {ElapsedMs}ms. Date={Date}.",
                stopwatch.Elapsed.TotalMilliseconds,
                date);
            return result;
        }

        var savedAt = DateTimeOffset.UtcNow;
        if (!IsCurrent(context))
        {
            logger.LogInformation("Discarded mobile today response because the session context changed. Date={Date}.", date);
            return ApiCallResult<TodayDto>.TransientFailure();
        }

        var currentUserId = context.UserId;
        var cachedToday = today.HotelBase?.Attribution is null ? today : today with
        {
            HotelBase = today.HotelBase with { Name = "Hotel", Address = string.Empty, Latitude = null, Longitude = null }
        };
        var cacheKey = GetCacheKey(currentUserId, context.TripId, today.Date);
        _current = today;
        _currentLocale = context.Locale;
        _currentSavedAt = savedAt;
        _currentUserId = currentUserId;
        _currentTripId = context.TripId;
        _currentDate = today.Date;

        await offlineCacheService.SaveAsync(
            cacheKey,
            cachedToday,
            cancellationToken).ConfigureAwait(false);
        if (!IsCurrent(context))
        {
            await offlineCacheService.DeleteAsync(cacheKey).ConfigureAwait(false);
            logger.LogInformation("Removed mobile today cache written after the session context changed. Date={Date}.", date);
            return ApiCallResult<TodayDto>.TransientFailure();
        }
        stopwatch.Stop();

        logger.LogInformation(
            "Mobile today refreshed and cached in {ElapsedMs}ms. Date={Date}; Sections={SectionCount}; Suggestions={SuggestionCount}.",
            stopwatch.Elapsed.TotalMilliseconds,
            today.Date,
            today.Sections.Count,
            today.Sections.Sum(section => section.Recommendations.Count));

        return result;
    }

    public bool HasFreshSnapshot(DateOnly date, TimeSpan? maxAge = null)
    {
        var ageLimit = maxAge ?? DefaultFreshnessWindow;
        return _current is not null
            && _currentLocale == Locale
            && _currentUserId == sessionService.CurrentUserId
            && _currentTripId == sessionService.CurrentTripId
            && _currentDate == date
            && _currentSavedAt.HasValue
            && DateTimeOffset.UtcNow - _currentSavedAt.Value <= ageLimit;
    }

    public async Task ClearUserCacheAsync(Guid? userId, CancellationToken cancellationToken = default)
    {
        if (_currentUserId == userId)
        {
            _current = null;
            _currentSavedAt = null;
            _currentUserId = null;
            _currentTripId = null;
            _currentDate = null;
        }

        await offlineCacheService.DeleteByPrefixAndSuffixAsync(
            "mobile-today-",
            GetCacheKeySuffix(userId)).ConfigureAwait(false);
    }

    private static string GetCacheKey(Guid? userId, Guid? tripId, DateOnly date) =>
        $"mobile-today-{date:yyyyMMdd}-trip-{tripId?.ToString() ?? "auto"}{GetCacheKeySuffix(userId)}";

    private static string GetCacheKeySuffix(Guid? userId) =>
        $"-{userId?.ToString() ?? "anonymous"}";

    private CacheContext CaptureContext(DateOnly date, GeoPointDto? location)
    {
        var userId = sessionService.CurrentUserId;
        var tripId = sessionService.CurrentTripId;
        var locale = Locale;
        var locationKey = location is null ? "none" : $"{location.Latitude:0.####}:{location.Longitude:0.####}";
        var contextVersion = sessionService.ContextVersion;
        return new CacheContext(userId, tripId, locale, contextVersion, $"{contextVersion}:{userId}:{tripId}:{locale}:{date:yyyyMMdd}:{locationKey}");
    }

    private bool IsCurrent(CacheContext context) =>
        context.UserId == sessionService.CurrentUserId
        && context.TripId == sessionService.CurrentTripId
        && context.ContextVersion == sessionService.ContextVersion
        && string.Equals(context.Locale, Locale, StringComparison.Ordinal);

    private sealed record CacheContext(Guid? UserId, Guid? TripId, string Locale, long ContextVersion, string Key);
}

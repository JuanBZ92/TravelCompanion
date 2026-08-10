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
    private static readonly TimeSpan DiskCacheMaxAge = TimeSpan.FromHours(6);
    private TodayDto? _current;
    private DateTimeOffset? _currentSavedAt;
    private Guid? _currentUserId;
    private DateOnly? _currentDate;

    public async Task<OfflineCacheResult<TodayDto>?> GetCachedAsync(
        DateOnly date,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var currentUserId = sessionService.CurrentUserId;
        if (_current is not null
            && _currentUserId == currentUserId
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
            GetCacheKey(currentUserId, date),
            DiskCacheMaxAge,
            cancellationToken).ConfigureAwait(false);
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
            _currentSavedAt = cached.SavedAt;
            _currentUserId = currentUserId;
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
        var stopwatch = Stopwatch.StartNew();
        var today = await apiClient.GetMobileTodayAsync(token, date, currentLocation, cancellationToken).ConfigureAwait(false);
        if (today is null)
        {
            stopwatch.Stop();
            logger.LogWarning(
                "Mobile today refresh returned no data after {ElapsedMs}ms. Date={Date}.",
                stopwatch.Elapsed.TotalMilliseconds,
                date);
            return null;
        }

        var savedAt = DateTimeOffset.UtcNow;
        var currentUserId = sessionService.CurrentUserId;
        await offlineCacheService.SaveAsync(
            GetCacheKey(currentUserId, today.Date),
            today,
            cancellationToken).ConfigureAwait(false);
        stopwatch.Stop();

        _current = today;
        _currentSavedAt = savedAt;
        _currentUserId = currentUserId;
        _currentDate = today.Date;

        logger.LogInformation(
            "Mobile today refreshed and cached in {ElapsedMs}ms. Date={Date}; Sections={SectionCount}; Suggestions={SuggestionCount}.",
            stopwatch.Elapsed.TotalMilliseconds,
            today.Date,
            today.Sections.Count,
            today.Sections.Sum(section => section.Recommendations.Count));

        return today;
    }

    public bool HasFreshSnapshot(DateOnly date, TimeSpan? maxAge = null)
    {
        var ageLimit = maxAge ?? DefaultFreshnessWindow;
        return _current is not null
            && _currentUserId == sessionService.CurrentUserId
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
            _currentDate = null;
        }

        await offlineCacheService.DeleteByPrefixAndSuffixAsync(
            "mobile-today-",
            GetCacheKeySuffix(userId)).ConfigureAwait(false);
    }

    private static string GetCacheKey(Guid? userId, DateOnly date) =>
        $"mobile-today-{date:yyyyMMdd}{GetCacheKeySuffix(userId)}";

    private static string GetCacheKeySuffix(Guid? userId) =>
        $"-{userId?.ToString() ?? "anonymous"}";
}

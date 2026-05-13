using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Mobile.Services;

public sealed class MobileBootstrapStore(
    TravelCompanionApiClient apiClient,
    AuthSessionService sessionService,
    OfflineCacheService offlineCacheService)
{
    private MobileBootstrapDto? _current;
    private DateTimeOffset? _currentSavedAt;
    private Guid? _currentUserId;

    public async Task<OfflineCacheResult<MobileBootstrapDto>?> GetCachedAsync(
        CancellationToken cancellationToken = default)
    {
        var currentUserId = sessionService.CurrentUserId;
        if (_current is not null && _currentUserId == currentUserId && _currentSavedAt.HasValue)
        {
            return new OfflineCacheResult<MobileBootstrapDto>(_current, _currentSavedAt.Value);
        }

        var cached = await offlineCacheService.GetAsync<MobileBootstrapDto>(
            GetCacheKey(currentUserId),
            cancellationToken);

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
        CancellationToken cancellationToken = default)
    {
        var bootstrap = await apiClient.GetMobileBootstrapAsync(token, cancellationToken);
        if (bootstrap is null)
        {
            return null;
        }

        var savedAt = DateTimeOffset.UtcNow;
        var currentUserId = sessionService.CurrentUserId;
        await offlineCacheService.SaveAsync(
            GetCacheKey(currentUserId),
            bootstrap,
            cancellationToken);

        _current = bootstrap;
        _currentSavedAt = savedAt;
        _currentUserId = currentUserId;

        return bootstrap;
    }

    private static string GetCacheKey(Guid? userId)
    {
        return $"mobile-bootstrap-japon-{userId?.ToString() ?? "anonymous"}";
    }
}

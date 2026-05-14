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
        string? destinationSlug = null,
        CancellationToken cancellationToken = default)
    {
        var currentUserId = sessionService.CurrentUserId;
        var cacheScope = NormalizeCacheScope(destinationSlug);
        if (_current is not null
            && _currentUserId == currentUserId
            && _currentSavedAt.HasValue
            && string.Equals(NormalizeCacheScope(_current.Destination.Slug), cacheScope, StringComparison.Ordinal))
        {
            return new OfflineCacheResult<MobileBootstrapDto>(_current, _currentSavedAt.Value);
        }

        var cached = await offlineCacheService.GetAsync<MobileBootstrapDto>(
            GetCacheKey(currentUserId, cacheScope),
            cancellationToken).ConfigureAwait(false);

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
        var bootstrap = await apiClient.GetMobileBootstrapAsync(token, destinationSlug, cancellationToken).ConfigureAwait(false);
        if (bootstrap is null)
        {
            return null;
        }

        var savedAt = DateTimeOffset.UtcNow;
        var currentUserId = sessionService.CurrentUserId;
        var cacheScope = NormalizeCacheScope(bootstrap.Destination.Slug);
        await offlineCacheService.SaveAsync(
            GetCacheKey(currentUserId, cacheScope),
            bootstrap,
            cancellationToken).ConfigureAwait(false);

        _current = bootstrap;
        _currentSavedAt = savedAt;
        _currentUserId = currentUserId;

        return bootstrap;
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
}

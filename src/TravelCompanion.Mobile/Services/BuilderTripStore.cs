using Microsoft.Extensions.Logging;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Mobile.Services;

public sealed class BuilderTripStore(
    TravelCompanionApiClient apiClient,
    AuthSessionService sessionService,
    OfflineCacheService offlineCacheService,
    MobileSyncStateStore syncStateStore,
    ILogger<BuilderTripStore> logger)
{
    private readonly SemaphoreSlim _loadLock = new(1, 1);
    private BuilderTripSetupDto? _current;
    private long _contextVersion = -1;

    public async Task<BuilderTripSetupDto?> GetAsync(
        string token,
        bool forceRefresh = false,
        CancellationToken cancellationToken = default)
    {
        if (!sessionService.HasKnownValidAccess) return null;
        var contextVersion = sessionService.ContextVersion;
        if (!forceRefresh && _current is not null && _contextVersion == contextVersion) return _current;

        await _loadLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!forceRefresh && _current is not null && _contextVersion == contextVersion) return _current;
            var key = GetCacheKey();
            if (!forceRefresh)
            {
                var cached = await offlineCacheService.GetAsync<BuilderTripSetupDto>(key, cancellationToken).ConfigureAwait(false);
                if (cached is not null)
                {
                    _current = cached.Value;
                    _contextVersion = contextVersion;
                    return _current;
                }
            }

            var setup = await apiClient.GetBuilderTripSetupAsync(token, cancellationToken).ConfigureAwait(false);
            if (setup is null || contextVersion != sessionService.ContextVersion) return null;
            await SaveAsync(setup, cancellationToken).ConfigureAwait(false);
            return setup;
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or IOException)
        {
            logger.LogInformation("Builder setup cache kept after transient failure ({ErrorType}).", exception.GetType().Name);
            return _current;
        }
        finally
        {
            _loadLock.Release();
        }
    }

    public async Task SaveAsync(BuilderTripSetupDto setup, CancellationToken cancellationToken = default)
    {
        _current = setup;
        _contextVersion = sessionService.ContextVersion;
        var metadata = await syncStateStore.CreateCacheMetadataAsync(
            "itinerary",
            setup.Revision.ToString(System.Globalization.CultureInfo.InvariantCulture),
            destinationSlug: setup.Destination,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        await offlineCacheService.SaveAsync(GetCacheKey(), setup, metadata, cancellationToken).ConfigureAwait(false);
    }

    public async Task UpdateRevisionAsync(int revision, CancellationToken cancellationToken = default)
    {
        if (_current is null) return;
        await SaveAsync(_current with { Revision = revision }, cancellationToken).ConfigureAwait(false);
    }

    public async Task ClearAsync()
    {
        _current = null;
        _contextVersion = -1;
        await offlineCacheService.DeleteByPrefixAndSuffixAsync(
            "builder-trip-v1-",
            $"-{sessionService.CurrentUserId?.ToString() ?? "anonymous"}").ConfigureAwait(false);
    }

    private string GetCacheKey() =>
        $"builder-trip-v1-{System.Globalization.CultureInfo.CurrentUICulture.Name}-trip-{sessionService.CurrentTripId?.ToString() ?? "auto"}-{sessionService.CurrentUserId?.ToString() ?? "anonymous"}";
}

using Microsoft.Extensions.Logging;

namespace TravelCompanion.Mobile.Services;

public sealed class OfflineSyncCoordinator(
    OfflineMutationQueueService mutationQueue,
    AuthSessionService sessionService,
    TravelCompanionApiClient apiClient,
    MobileSyncStateStore syncStateStore,
    MobileBootstrapStore bootstrapStore,
    MobileDiscoverStore discoverStore,
    MobileTodayStore todayStore,
    FreeMapStore freeMapStore,
    OfflineCacheService offlineCacheService,
    ILogger<OfflineSyncCoordinator> logger)
{
    private readonly SemaphoreSlim _syncLock = new(1, 1);
    private bool _started;

    public event EventHandler<int>? PendingCountChanged;

    public void Start()
    {
        if (_started)
        {
            return;
        }

        _started = true;
        Connectivity.Current.ConnectivityChanged += OnConnectivityChanged;
        TriggerSynchronize();
    }

    public void TriggerSynchronize() => _ = SynchronizeSafelyAsync();

    public async Task<OfflineMutationReplayResult> SynchronizeAsync(
        CancellationToken cancellationToken = default)
    {
        if (!await _syncLock.WaitAsync(0, cancellationToken).ConfigureAwait(false))
        {
            return new OfflineMutationReplayResult(0, 0, 0);
        }

        try
        {
            if (Connectivity.Current.NetworkAccess != NetworkAccess.Internet)
            {
                await PublishPendingCountAsync(cancellationToken).ConfigureAwait(false);
                return new OfflineMutationReplayResult(0, 0, 0);
            }

            var token = await sessionService.GetTokenAsync().ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(token))
            {
                await PublishPendingCountAsync(cancellationToken).ConfigureAwait(false);
                return new OfflineMutationReplayResult(0, 0, 0);
            }

            var result = await mutationQueue
                .ReplayPendingAsync(token, cancellationToken)
                .ConfigureAwait(false);
            await SynchronizeVersionsAsync(token, force: false, cancellationToken).ConfigureAwait(false);
            await PublishPendingCountAsync(cancellationToken).ConfigureAwait(false);
            return result;
        }
        finally
        {
            _syncLock.Release();
        }
    }

    public async Task SynchronizeVersionsAsync(
        string token,
        bool force,
        CancellationToken cancellationToken = default)
    {
        var comparison = await syncStateStore.CheckAsync(force, cancellationToken).ConfigureAwait(false);
        if (comparison is null) return;

        if (comparison.RequiresScopeRecovery)
        {
            // A missing sync-state entry does not imply that the domain caches are new.
            // Recover each persisted scope once so an older cache cannot become permanently
            // accepted after the server answers 304 on subsequent checks.
            var hasBootstrapCache = !sessionService.IsFreeMapPreview
                && await bootstrapStore
                    .GetCachedAsync(comparison.Current.DestinationSlug, cancellationToken)
                    .ConfigureAwait(false) is not null;
            var hasFreeCatalogCache = sessionService.IsFreeMapPreview
                && await freeMapStore.GetCachedCitiesAsync(cancellationToken).ConfigureAwait(false) is not null;

            if (!hasBootstrapCache && !hasFreeCatalogCache)
            {
                // First installation: the owning screen performs the single bootstrap/catalog load.
                return;
            }

            comparison = comparison with
            {
                CatalogChanged = hasBootstrapCache,
                ItineraryChanged = hasBootstrapCache && comparison.Current.TripId.HasValue,
                DocumentsChanged = hasBootstrapCache && comparison.Current.TripId.HasValue,
                TodayChanged = hasBootstrapCache,
                FreeCatalogChanged = hasFreeCatalogCache,
                RequiresScopeRecovery = false
            };
        }

        if (comparison.CatalogChanged || comparison.ItineraryChanged || comparison.TodayChanged)
        {
            // Invalidate derived views before publishing a new schedule so the visible day reloads once.
            await todayStore.InvalidateAllAsync().ConfigureAwait(false);
        }

        if (comparison.CatalogChanged)
        {
            discoverStore.Invalidate();
            var discover = await discoverStore.RefreshAsync(token, comparison.Current.DestinationSlug, cancellationToken).ConfigureAwait(false);
            if (discover is not null)
            {
                var packages = await apiClient.GetPackagesAsync(comparison.Current.DestinationSlug, token, cancellationToken).ConfigureAwait(false);
                await bootstrapStore.ApplyCatalogAsync(discover, packages, cancellationToken).ConfigureAwait(false);
            }
        }

        if (comparison.ItineraryChanged)
        {
            var schedule = await apiClient.GetScheduleAsync(token, cancellationToken).ConfigureAwait(false);
            if (schedule is not null)
            {
                await bootstrapStore.ReplaceScheduleAsync(schedule, cancellationToken).ConfigureAwait(false);
            }
        }

        if (comparison.DocumentsChanged || comparison.ItineraryChanged)
        {
            await offlineCacheService.DeleteByPrefixAndSuffixAsync(
                "mobile-docs-",
                $"-{sessionService.CurrentUserId?.ToString() ?? "anonymous"}").ConfigureAwait(false);
        }

        if (comparison.FreeCatalogChanged)
        {
            await freeMapStore.ClearAsync().ConfigureAwait(false);
        }
    }

    public async Task PublishPendingCountAsync(CancellationToken cancellationToken = default)
    {
        var count = await mutationQueue.GetPendingCountAsync(cancellationToken).ConfigureAwait(false);
        PendingCountChanged?.Invoke(this, count);
    }

    private void OnConnectivityChanged(object? sender, ConnectivityChangedEventArgs args)
    {
        if (args.NetworkAccess == NetworkAccess.Internet)
        {
            TriggerSynchronize();
        }
    }

    private async Task SynchronizeSafelyAsync()
    {
        try
        {
            await SynchronizeAsync().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Offline mutation synchronization failed.");
        }
    }
}

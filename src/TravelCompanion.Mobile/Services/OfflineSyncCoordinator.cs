using Microsoft.Extensions.Logging;

namespace TravelCompanion.Mobile.Services;

public sealed class OfflineSyncCoordinator(
    OfflineMutationQueueService mutationQueue,
    AuthSessionService sessionService,
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
            await PublishPendingCountAsync(cancellationToken).ConfigureAwait(false);
            return result;
        }
        finally
        {
            _syncLock.Release();
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

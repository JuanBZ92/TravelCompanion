using Microsoft.Extensions.DependencyInjection;
using TravelCompanion.Mobile.ViewModels;

namespace TravelCompanion.Mobile.Services;

public sealed class SessionLogoutService(
    AuthSessionService sessionService,
    TravelCompanionApiClient apiClient,
    MobileBootstrapStore bootstrapStore,
    MobileDiscoverStore discoverStore,
    MobileTodayStore todayStore,
    FreeMapStore freeMapStore,
    OfflineMutationQueueService mutationQueue,
    PendingItineraryActionStore pendingItineraryActionStore,
    IServiceProvider serviceProvider)
{
    private readonly SemaphoreSlim _logoutLock = new(1, 1);

    public async Task LogoutAsync()
    {
        await _logoutLock.WaitAsync();
        try
        {
            var userId = sessionService.CurrentUserId;
            var token = await sessionService.GetTokenAsync();
            if (!string.IsNullOrWhiteSpace(token))
            {
                try
                {
                    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(1));
                    await apiClient.LogoutAsync(token, timeout.Token);
                }
                catch
                {
                    // Local logout must remain available while the API is offline.
                }
            }

            await ResetContentCoreAsync(userId, preservePendingItineraryAction: false);
            sessionService.Clear();
        }
        finally
        {
            _logoutLock.Release();
        }
    }

    public async Task ResetContentAsync(
        Guid? userId,
        bool preservePendingItineraryAction = false)
    {
        await _logoutLock.WaitAsync();
        try
        {
            await ResetContentCoreAsync(userId, preservePendingItineraryAction);
        }
        finally
        {
            _logoutLock.Release();
        }
    }

    private async Task ResetContentCoreAsync(
        Guid? userId,
        bool preservePendingItineraryAction)
    {
        await TryClearAsync(() => bootstrapStore.ClearUserCacheAsync(userId));
        await TryClearAsync(() => discoverStore.ClearUserCacheAsync(userId));
        await TryClearAsync(() => todayStore.ClearUserCacheAsync(userId));
        await TryClearAsync(freeMapStore.ClearAsync);
        await TryClearAsync(mutationQueue.ClearAsync);

        if (!preservePendingItineraryAction)
        {
            pendingItineraryActionStore.Clear();
        }

        foreach (var resettable in serviceProvider.GetServices<ISessionStateResettable>())
        {
            resettable.ResetForNewSession();
        }
    }

    private static async Task TryClearAsync(Func<Task> clear)
    {
        try
        {
            await clear();
        }
        catch
        {
            // A damaged cache must not prevent the user from ending the session.
        }
    }
}

using Microsoft.Extensions.DependencyInjection;
using TravelCompanion.Mobile.ViewModels;

namespace TravelCompanion.Mobile.Services;

public sealed class SessionLogoutService(
    AuthSessionService sessionService,
    TravelCompanionApiClient apiClient,
    MobileBootstrapStore bootstrapStore,
    MobileDiscoverStore discoverStore,
    MobileTodayStore todayStore,
    MobileSyncStateStore syncStateStore,
    BuilderTripStore builderTripStore,
    FreeMapStore freeMapStore,
    OfflineCacheService offlineCacheService,
    OfflineSyncCoordinator syncCoordinator,
    PendingItineraryActionStore pendingItineraryActionStore,
    IServiceProvider serviceProvider)
{
    private readonly SemaphoreSlim _logoutLock = new(1, 1);

    public async Task LogoutAsync(Func<Task>? leaveAuthenticatedScreen = null)
    {
        await _logoutLock.WaitAsync();
        try
        {
            var userId = sessionService.CurrentUserId;
            var tokenTask = sessionService.GetTokenAsync();
            sessionService.BeginLogout();
            try
            {
                if (leaveAuthenticatedScreen is not null) await leaveAuthenticatedScreen();
                await ResetContentCoreAsync(userId, preservePendingItineraryAction: false);
            }
            finally
            {
                sessionService.Clear();
            }
            var token = await tokenTask;
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

            await TryClearAsync(() => syncCoordinator.PublishPendingCountAsync());
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
        await TryClearAsync(TripDocumentStore.ClearPreviewsAsync);
        await TryClearAsync(() => serviceProvider.GetRequiredService<ReservationReminderService>().ClearAsync());
        await TryClearAsync(() => bootstrapStore.ClearUserCacheAsync(userId));
        await TryClearAsync(() => discoverStore.ClearUserCacheAsync(userId));
        await TryClearAsync(() => todayStore.ClearUserCacheAsync(userId));
        await TryClearAsync(syncStateStore.ClearAsync);
        await TryClearAsync(builderTripStore.ClearAsync);
        await TryClearAsync(freeMapStore.ClearAsync);
        await TryClearAsync(() => offlineCacheService.DeleteByPrefixAndSuffixAsync(
            "mobile-docs-",
            $"-{userId?.ToString() ?? "anonymous"}"));
        await TryClearAsync(() => syncCoordinator.PublishPendingCountAsync());

        if (!preservePendingItineraryAction)
        {
            pendingItineraryActionStore.Clear();
        }

        foreach (var resettable in serviceProvider.GetServices<ISessionStateResettable>())
        {
            await TryClearAsync(() =>
            {
                resettable.ResetForNewSession();
                return Task.CompletedTask;
            });
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

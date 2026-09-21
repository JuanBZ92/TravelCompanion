using System.Globalization;
using TravelCompanion.Mobile.Services;
using TravelCompanion.Shared;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Mobile.Tests;

[CollectionDefinition("Free map session", DisableParallelization = true)]
public sealed class FreeMapSessionCollection;

[Collection("Free map session")]
public sealed class FreeMapStoreTests
{
    private static readonly FreeMapPreviewDto Preview = new(DateTimeOffset.UtcNow,
        new("tokyo", "Tokyo", 35, 139, 3, 0), null, 0, 0, []);

    private static async Task<(FreeMapStore Store, TravelCompanionApiClient Api, OfflineCacheService Disk, AuthSessionService Session)> Setup()
    {
        var session = new AuthSessionService();
        await session.SaveAsync(new(Guid.NewGuid(), "test@example.com", "Test", false, "token", Guid.NewGuid()));
        var api = new TravelCompanionApiClient { FetchCity = () => Task.FromResult<FreeMapPreviewDto?>(Preview) };
        var disk = new OfflineCacheService();
        return (new(api, disk, new(), session), api, disk, session);
    }

    [Fact]
    public async Task Warm_reads_reuse_memory_without_network_or_disk()
    {
        var (store, api, disk, _) = await Setup();
        await store.RefreshCityAsync("token", "tokyo");
        for (var i = 0; i < 5; i++) Assert.Same(Preview, (await store.GetCachedCityAsync("tokyo"))!.Value);
        Assert.True(store.HasFreshSnapshot("tokyo"));
        Assert.Equal(1, api.CityRequests);
        Assert.Equal(0, disk.Reads);
        Assert.Equal(1, disk.Writes);
    }

    [Fact]
    public async Task Concurrent_reads_share_fetch_and_cancelling_one_wait_keeps_other_alive()
    {
        var (store, api, disk, _) = await Setup();
        var pending = new TaskCompletionSource<FreeMapPreviewDto?>(TaskCreationOptions.RunContinuationsAsynchronously);
        api.FetchCity = () => pending.Task;
        using var cancelled = new CancellationTokenSource();
        var first = store.RefreshCityAsync("token", "tokyo", cancelled.Token);
        var second = store.RefreshCityAsync("token", "tokyo");
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
        pending.SetResult(Preview);
        Assert.Same(Preview, await second);
        Assert.Equal(1, api.CityRequests);
        Assert.Equal(1, disk.Writes);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Late_response_cannot_repopulate_cache_after_invalidation_or_logout(bool logout)
    {
        var (store, api, disk, session) = await Setup();
        var pending = new TaskCompletionSource<FreeMapPreviewDto?>(TaskCreationOptions.RunContinuationsAsynchronously);
        api.FetchCity = () => pending.Task;
        var request = store.RefreshCityAsync("token", "tokyo");
        if (logout) session.BeginLogout(); else await store.ClearAsync();
        pending.SetResult(Preview);
        Assert.Null(await request);
        Assert.Empty(disk.Entries);
        Assert.False(store.HasFreshSnapshot("tokyo"));
    }

    [Fact]
    public async Task Disk_survives_store_restart_but_not_account_or_language_change()
    {
        var (store, api, disk, session) = await Setup();
        await store.RefreshCityAsync("token", "tokyo");
        var restarted = new FreeMapStore(api, disk, new(), session);
        Assert.Same(Preview, (await restarted.GetCachedCityAsync("tokyo"))!.Value);
        var previous = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = new(previous.TwoLetterISOLanguageName == "es" ? "en" : "es");
            Assert.Null(await restarted.GetCachedCityAsync("tokyo"));
        }
        finally { CultureInfo.CurrentUICulture = previous; }
        await session.SaveAsync(new(Guid.NewGuid(), "other@example.com", "Other", false, "token", Guid.NewGuid()));
        Assert.Null(await restarted.GetCachedCityAsync("tokyo"));
    }

    [Fact]
    public async Task Legacy_unscoped_cache_is_never_loaded()
    {
        var (store, _, disk, _) = await Setup();
        disk.Entries["free-map-city-tokyo"] = new OfflineCacheResult<FreeMapPreviewDto>(Preview, DateTimeOffset.UtcNow);
        Assert.Null(await store.GetCachedCityAsync("tokyo"));
    }

    [Fact]
    public async Task Offline_failure_keeps_snapshot_and_next_explicit_refresh_retries()
    {
        var (store, api, disk, _) = await Setup();
        await store.RefreshCityAsync("token", "tokyo");
        api.FetchCity = () => throw new HttpRequestException("offline");
        await Assert.ThrowsAsync<HttpRequestException>(() => store.RefreshCityAsync("token", "tokyo"));
        Assert.Same(Preview, (await store.GetCachedCityAsync("tokyo"))!.Value);
        var changed = Preview with { ContactUrl = "https://example.com/pass" };
        api.FetchCity = () => Task.FromResult<FreeMapPreviewDto?>(changed);
        Assert.Same(changed, await store.RefreshCityAsync("token", "tokyo"));
        Assert.Equal(3, api.CityRequests);
        Assert.Equal(2, disk.Writes);
    }

    [Fact]
    public async Task Same_account_relogin_rejects_response_from_previous_session()
    {
        var (store, api, disk, session) = await Setup();
        var login = new AuthSessionDto(session.CurrentUserId!.Value, "test@example.com", "Test", false, "new-token", session.CurrentTripId);
        var pending = new TaskCompletionSource<FreeMapPreviewDto?>(TaskCreationOptions.RunContinuationsAsynchronously);
        api.FetchCity = () => pending.Task;
        var request = store.RefreshCityAsync("token", "tokyo");
        session.BeginLogout();
        await session.SaveAsync(login);
        pending.SetResult(Preview);
        Assert.Null(await request);
        Assert.Empty(disk.Entries);
    }

    [Fact]
    public async Task Trip_and_access_changes_do_not_reuse_cached_preview()
    {
        var (store, _, _, session) = await Setup();
        await store.RefreshCityAsync("token", "tokyo");
        Preferences.Default.Set("auth_trip_id", Guid.NewGuid().ToString());
        Assert.Null(await store.GetCachedCityAsync("tokyo"));
        await store.RefreshCityAsync("token", "tokyo");
        Preferences.Default.Set("auth_access_mode", SessionAccessMode.FreeMapPreview.ToString());
        Assert.False(store.HasFreshSnapshot("tokyo"));
        Assert.Null(await store.GetCachedCityAsync("tokyo"));
        session.Clear();
    }
}

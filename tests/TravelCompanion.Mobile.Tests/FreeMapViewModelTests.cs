using TravelCompanion.Mobile.Services;
using TravelCompanion.Mobile.ViewModels;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Mobile.Tests;

[Collection("Free map session")]
public sealed class FreeMapViewModelTests
{
    private static readonly FreeMapCityDto City = new("tokyo", "Tokyo", 35, 139, 3, 0);
    private static readonly FreeMapPreviewDto Preview = new(DateTimeOffset.UtcNow, City, null, 0, 0, []);

    [Theory]
    [InlineData("account")]
    [InlineData("trip")]
    [InlineData("cancel")]
    public async Task Delayed_token_cannot_clear_a_new_session_or_start_refresh_after_cancel(string change)
    {
        using var fixture = await Fixture.CreateAsync(warm: false);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        SecureStorage.Default.BeforeGet = key =>
        {
            if (!key.StartsWith("auth_token", StringComparison.Ordinal)) return Task.CompletedTask;
            entered.TrySetResult(); return release.Task;
        };
        try
        {
            var refresh = fixture.ViewModel.RefreshCommand.ExecuteAsync(null);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var expected = fixture.Session;
            if (change == "cancel") fixture.ViewModel.CancelLoading();
            else
            {
                expected = fixture.Session with { UserId = change == "account" ? Guid.NewGuid() : fixture.Session.UserId,
                    TripId = Guid.NewGuid(), Token = "current-token" };
                await fixture.Sessions.SaveAsync(expected);
            }
            release.TrySetResult();
            await refresh.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(fixture.Sessions.HasSession);
            Assert.Equal(expected.UserId, fixture.Sessions.CurrentUserId);
            Assert.Equal(expected.TripId, fixture.Sessions.CurrentTripId);
            Assert.Empty(Shell.Current.Navigations);
            Assert.Equal(0, fixture.Sync.VersionRequests);
            Assert.Equal(0, fixture.Api.CitiesRequests);
            Assert.Equal(0, fixture.Api.CityRequests);
            Assert.Equal(0, fixture.Cache.Writes);
            if (change == "cancel") Assert.False(fixture.ViewModel.HasError);
        }
        finally { SecureStorage.Default.BeforeGet = null; release.TrySetResult(); }
    }

    [Theory]
    [InlineData("account")]
    [InlineData("trip")]
    [InlineData("cancel")]
    public async Task Delayed_version_sync_cannot_refresh_a_city_for_a_changed_context_or_after_cancel(string change)
    {
        using var fixture = await Fixture.CreateAsync(warm: true);
        var cityRequests = fixture.Api.CityRequests;
        var writes = fixture.Cache.Writes;
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Sync.Synchronize = _ => { entered.TrySetResult(); return release.Task; };
        try
        {
            var refresh = fixture.ViewModel.RefreshCommand.ExecuteAsync(null);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            if (change == "cancel") fixture.ViewModel.CancelLoading();
            else await fixture.Sessions.SaveAsync(fixture.Session with
            {
                UserId = change == "account" ? Guid.NewGuid() : fixture.Session.UserId,
                TripId = Guid.NewGuid(), Token = "current-token"
            });
            release.TrySetResult(); await refresh.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(1, fixture.Sync.VersionRequests);
            Assert.Equal(cityRequests, fixture.Api.CityRequests);
            Assert.Equal(writes, fixture.Cache.Writes);
            Assert.Empty(Shell.Current.Navigations);
            Assert.True(fixture.Sessions.HasSession);
            if (change == "cancel") Assert.False(fixture.ViewModel.HasError);
        }
        finally { release.TrySetResult(); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Background_revalidation_cannot_send_the_old_token_for_the_new_account_or_trip(bool changeAccount)
    {
        using var fixture = await Fixture.CreateAsync(warm: true);
        foreach (var key in fixture.Cache.Entries.Where(entry => entry.Value is OfflineCacheResult<FreeMapPreviewDto>)
                     .Select(entry => entry.Key).ToArray())
            fixture.Cache.Entries[key] = new OfflineCacheResult<FreeMapPreviewDto>(Preview, DateTimeOffset.UtcNow.AddMinutes(-6));
        fixture.RestartMap();
        var cityRequests = fixture.Api.CityRequests;
        var writes = fixture.Cache.Writes;
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Sync.Synchronize = _ => { entered.TrySetResult(); return release.Task; };
        try
        {
            var load = fixture.ViewModel.LoadCommand.ExecuteAsync(null);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await fixture.Sessions.SaveAsync(fixture.Session with
            {
                UserId = changeAccount ? Guid.NewGuid() : fixture.Session.UserId,
                TripId = Guid.NewGuid(), Token = "current-token"
            });
            release.TrySetResult(); await load.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(1, fixture.Sync.VersionRequests);
            Assert.Equal(cityRequests, fixture.Api.CityRequests);
            Assert.Equal(writes, fixture.Cache.Writes);
            Assert.Empty(Shell.Current.Navigations);
            Assert.True(fixture.Sessions.HasSession);
        }
        finally { release.TrySetResult(); }
    }

    private sealed class Fixture : IDisposable
    {
        public AuthSessionService Sessions { get; } = new();
        public AuthSessionDto Session { get; private init; } = null!;
        public TravelCompanionApiClient Api { get; } = new();
        public OfflineCacheService Cache { get; } = new();
        public OfflineSyncCoordinator Sync { get; } = new();
        public FreeMapStore Store { get; private set; } = null!;
        public FreeMapViewModel ViewModel { get; private set; } = null!;
        private Shell PreviousShell { get; } = Shell.Current;

        public static async Task<Fixture> CreateAsync(bool warm)
        {
            var fixture = new Fixture { Session = new(Guid.NewGuid(), "free-map@example.test", "Traveler", false,
                "original-token", Guid.NewGuid()) };
            await fixture.Sessions.SaveAsync(fixture.Session);
            fixture.Api.FetchCities = () => Task.FromResult<IReadOnlyList<FreeMapCityDto>?>([City]);
            fixture.Api.FetchCity = () => Task.FromResult<FreeMapPreviewDto?>(Preview);
            fixture.Store = new(fixture.Api, fixture.Cache, new(), fixture.Sessions);
            fixture.ViewModel = new(fixture.Sessions, fixture.Store, fixture.Sync);
            Shell.Current = new();
            if (warm)
            {
                await fixture.Store.RefreshCitiesAsync("original-token");
                await fixture.Store.RefreshCityAsync("original-token", City.Slug);
                await fixture.ViewModel.LoadCommand.ExecuteAsync(null);
                Assert.Same(Preview, fixture.ViewModel.Preview);
            }
            return fixture;
        }
        public void RestartMap()
        {
            ViewModel.ResetForNewSession();
            Store = new(Api, Cache, new(), Sessions);
            ViewModel = new(Sessions, Store, Sync);
        }
        public void Dispose()
        {
            SecureStorage.Default.BeforeGet = null;
            ViewModel.ResetForNewSession(); Sessions.Clear(); Shell.Current = PreviousShell;
        }
    }
}

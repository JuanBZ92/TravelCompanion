using TravelCompanion.Mobile.Services;
using TravelCompanion.Mobile.ViewModels;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Mobile.Tests;

[Collection("Free map session")]
public sealed class AssistantFreeTimeViewModelTests
{
    private static readonly DateOnly Day = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(10);
    private static AuthSessionDto Session() => new(Guid.NewGuid(), "review@example.test", "Traveler", false,
        "token", Guid.NewGuid());
    private static OfflineCacheResult<MobileBootstrapDto> Snapshot(AuthSessionDto session) => new(new(
        DateTimeOffset.UtcNow, new(Guid.NewGuid(), "Japan", "japan", "JP", "", ""),
        new(session.UserId, session.Email, session.DisplayName, [], [], [], []), [], [],
        new(session.TripId!.Value, "Traveler", "Japan", Day, Day.AddDays(5), []) { TimeZoneId = "Asia/Tokyo" }),
        DateTimeOffset.UtcNow);

    [Theory]
    [InlineData("date")]
    [InlineData("account")]
    [InlineData("trip")]
    [InlineData("surface")]
    public async Task Delayed_cache_cannot_open_free_time_after_the_original_selection_changes(string change)
    {
        var sessions = new AuthSessionService();
        try
        {
            var session = Session(); await sessions.SaveAsync(session);
            var delayed = new TaskCompletionSource<OfflineCacheResult<MobileBootstrapDto>?>(TaskCreationOptions.RunContinuationsAsynchronously);
            var bootstrap = new MobileBootstrapStore { ReadCached = _ => delayed.Task };
            var vm = new TravelChatViewModel(sessions, bootstrap) { PlanningDate = Day.ToDateTime(TimeOnly.MinValue) };
            var setup = vm.OpenFreeTimeForDateAsync(null);
            if (change == "date") vm.PlanningDate = Day.AddDays(1).ToDateTime(TimeOnly.MinValue);
            else if (change == "surface") vm.ChangeSurface();
            else await sessions.SaveAsync(change == "trip" ? session with { TripId = Guid.NewGuid() } : Session());
            delayed.SetResult(Snapshot(session)); await setup;
            Assert.Equal(0, vm.SearchOpened);
            Assert.False(vm.IsFreeTimeSearch);
            Assert.Null(vm.ErrorMessage);
        }
        finally { sessions.Clear(); }
    }

    [Theory]
    [InlineData("date")]
    [InlineData("request")]
    [InlineData("surface")]
    public async Task Delayed_city_resolution_does_not_replace_a_new_day_surface_or_a_request_already_in_progress(string change)
    {
        var sessions = new AuthSessionService();
        try
        {
            var session = Session(); await sessions.SaveAsync(session);
            var delayed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var bootstrap = new MobileBootstrapStore { Value = Snapshot(session).Value };
            var vm = new TravelChatViewModel(sessions, bootstrap)
            { PlanningDate = Day.ToDateTime(TimeOnly.MinValue), ResolveCity = _ => delayed.Task };
            var setup = vm.OpenFreeTimeForDateAsync(null);
            if (change == "request") vm.IsBusy = true;
            else if (change == "surface") vm.ChangeSurface();
            else vm.PlanningDate = Day.AddDays(1).ToDateTime(TimeOnly.MinValue);
            delayed.SetResult(); await setup;
            Assert.Equal(0, vm.SearchOpened);
            Assert.False(vm.IsFreeTimeSearch);
            Assert.Equal(change == "request", vm.IsBusy);
            Assert.Null(vm.ErrorMessage);
        }
        finally { sessions.Clear(); }
    }

    [Fact]
    public async Task Current_context_opens_requested_day_and_preserves_free_time_controls()
    {
        var sessions = new AuthSessionService();
        try
        {
            var session = Session(); await sessions.SaveAsync(session);
            var bootstrap = new MobileBootstrapStore { Value = Snapshot(session).Value };
            var vm = new TravelChatViewModel(sessions, bootstrap) { PlanningDate = Day.ToDateTime(TimeOnly.MinValue) };
            await vm.OpenFreeTimeForDateAsync(Day.AddDays(1));
            Assert.Equal(1, vm.SearchOpened);
            Assert.True(vm.IsFreeTimeSearch);
            Assert.True(vm.DateSelectedByTraveler);
            Assert.Equal(Day.AddDays(1), DateOnly.FromDateTime(vm.PlanningDate));
            Assert.Equal(TimeSpan.FromHours(9), vm.FreeTimeStart);
            Assert.Equal(4, vm.FreeTimeDurations.Count);
            Assert.True(vm.CanSubmitQuickSearch);
            Assert.Null(vm.PendingRetryRequest);
        }
        finally { sessions.Clear(); }
    }

    [Fact]
    public async Task Double_tap_during_cache_read_opens_free_time_only_once()
    {
        var sessions = new AuthSessionService();
        try
        {
            var session = Session(); await sessions.SaveAsync(session);
            var delayed = new TaskCompletionSource<OfflineCacheResult<MobileBootstrapDto>?>(TaskCreationOptions.RunContinuationsAsynchronously);
            var reads = 0;
            var bootstrap = new MobileBootstrapStore { ReadCached = _ => { reads++; return delayed.Task; } };
            var vm = new TravelChatViewModel(sessions, bootstrap) { PlanningDate = Day.ToDateTime(TimeOnly.MinValue) };
            var first = vm.OpenFreeTimeForDateAsync(null);
            await vm.OpenFreeTimeForDateAsync(null);
            Assert.Equal(1, reads);
            delayed.SetResult(Snapshot(session)); await first;
            Assert.Equal(1, vm.SearchOpened);
        }
        finally { sessions.Clear(); }
    }
}

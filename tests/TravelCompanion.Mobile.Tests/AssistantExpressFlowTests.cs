using TravelCompanion.Mobile.Services;
using TravelCompanion.Mobile.ViewModels;
using TravelCompanion.Shared;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Mobile.Tests;

[Collection("Free map session")]
public sealed class AssistantExpressFlowTests
{
    private static readonly DateOnly Day = new(2026, 10, 8);
    private static readonly DateTimeOffset Now = new(2026, 10, 8, 1, 0, 0, TimeSpan.Zero);
    private static ScheduleItemDto Next() => new(Guid.NewGuid(), null, ReservationType.Event, Day,
        new(11, 0), null, new(12, 0), "Museum", "Tokyo", "Museum", "Address", "", "",
        null, null, null, null, null, null, Latitude: 35.7m, Longitude: 139.7m);
    private static TravelCardDto Card(Guid id, string title = "Garden", string? provider = null) =>
        new("recommendation", title, "Tokyo", "Description", "10:10", "10:25", "", null, null,
            ["Fits your available time"], [], id.ToString(), null) { ProviderPlaceId = provider };
    private static TravelChatResponse Response(params TravelCardDto[] cards) => new("express-conversation",
        "Options", "recommendation", cards, [], null);

    private sealed class Context(AuthSessionService sessions, AuthSessionDto session, MobileBootstrapStore bootstrap,
        TravelChatViewModel vm) : IDisposable
    {
        public AuthSessionService Sessions => sessions;
        public AuthSessionDto Session => session;
        public MobileBootstrapStore Bootstrap => bootstrap;
        public TravelChatViewModel Vm => vm;
        public void Dispose() { sessions.Clear(); Connectivity.Current.NetworkAccess = NetworkAccess.Internet; }
    }

    private static async Task<Context> OpenAsync(params ScheduleItemDto[] items)
    {
        var sessions = new AuthSessionService();
        var session = new AuthSessionDto(Guid.NewGuid(), "synthetic@example.test", "Traveler", false,
            "token", Guid.NewGuid(), AccessMode: SessionAccessMode.Builder,
            ExperienceMode: ExperienceMode.SelfServiceBuilder,
            Capabilities: new(true, true, true, false, false, true));
        await sessions.SaveAsync(session);
        var bootstrap = new MobileBootstrapStore { Value = new(DateTimeOffset.UtcNow,
            new(Guid.NewGuid(), "Japan", "japan", "JP", "", ""),
            new(session.UserId, session.Email, session.DisplayName, [], [], [], []), [], [],
            new(session.TripId!.Value, "Traveler", "Japan", Day, Day.AddDays(3), items) { TimeZoneId = "Asia/Tokyo" }) };
        var vm = new TravelChatViewModel(sessions, bootstrap)
        { PlanningDate = Day.ToDateTime(TimeOnly.MinValue), FreeTimeClock = () => Now };
        await vm.OpenFreeTimeForDateAsync(null);
        return new(sessions, session, bootstrap, vm);
    }

    private static async Task ChooseNextAsync(TravelChatViewModel vm)
    {
        vm.SelectInterest(GuidedTravelCategories.Culture);
        await vm.SubmitExpressAsync();
        vm.ChooseFreeTimeNextAreaCommand.Execute(null);
    }

    [Fact]
    public async Task Interest_precedes_area_and_no_location_is_requested_without_an_explicit_choice()
    {
        using var context = await OpenAsync(Next());
        var vm = context.Vm;
        Assert.True(vm.ShowFreeTimeInterests);
        Assert.False(vm.CanSubmitQuickSearch);
        vm.SelectInterest(GuidedTravelCategories.Culture);
        Assert.True(vm.CanSubmitQuickSearch);
        await vm.SubmitExpressAsync();
        Assert.True(vm.ShowFreeTimeArea);
        Assert.False(vm.ShowFreeTimeInterests);
        Assert.Equal(0, vm.Location.Requests);
        Assert.True(vm.BackToInterests());
        Assert.True(vm.ShowFreeTimeInterests);
    }

    [Fact]
    public async Task Nearby_next_plan_uses_its_id_and_never_impersonates_the_travelers_location()
    {
        var next = Next();
        using var context = await OpenAsync(next);
        var vm = context.Vm;
        TravelChatRequest? request = null;
        vm.Api.SendTravelChat = (value, _) => { request = value; return Task.FromResult<TravelChatResponse?>(Response(Card(Guid.NewGuid()))); };
        await ChooseNextAsync(vm);
        await vm.SubmitExpressAsync();
        Assert.Equal(next.Id, request!.Criteria!.NearReservationId);
        Assert.Null(request.CurrentLocation);
        Assert.Equal(30, request.Criteria.MaxWalkingMinutes);
        Assert.Equal(Day, request.Date);
        Assert.Equal(0, vm.Location.Requests);
        Assert.Single(vm.Options);
    }

    [Fact]
    public async Task Current_area_requests_location_only_after_choice_and_keeps_the_real_origin()
    {
        using var context = await OpenAsync();
        var vm = context.Vm;
        TravelChatRequest? request = null;
        vm.Api.SendTravelChat = (value, _) => { request = value; return Task.FromResult<TravelChatResponse?>(Response(Card(Guid.NewGuid()))); };
        vm.SelectInterest(GuidedTravelCategories.Walk);
        await vm.SubmitExpressAsync();
        Assert.Equal(0, vm.Location.Requests);
        await vm.ChooseFreeTimeCurrentAreaCommand.ExecuteAsync(null);
        await vm.SubmitExpressAsync();
        Assert.Equal(new GeoPointDto(35.68m, 139.76m), request!.CurrentLocation);
        Assert.Null(request.Criteria!.NearReservationId);
        Assert.Equal(1, vm.Location.Requests);
    }

    [Fact]
    public async Task Missing_location_does_not_claim_nearby_and_requires_explicit_city_fallback()
    {
        using var context = await OpenAsync();
        var vm = context.Vm;
        vm.Location.Resolve = _ => Task.FromResult<GeoPointDto?>(null);
        vm.SelectInterest(GuidedTravelCategories.Food);
        await vm.SubmitExpressAsync();
        await vm.ChooseFreeTimeCurrentAreaCommand.ExecuteAsync(null);
        Assert.False(vm.CanSubmitQuickSearch);
        Assert.True(vm.CanUseFreeTimeCityFallback);
        Assert.NotNull(vm.ErrorMessage);
        vm.ChooseFreeTimeCityAreaCommand.Execute(null);
        Assert.True(vm.CanSubmitQuickSearch);
        TravelChatRequest? request = null;
        vm.Api.SendTravelChat = (value, _) => { request = value; return Task.FromResult<TravelChatResponse?>(Response(Card(Guid.NewGuid()))); };
        await vm.SubmitExpressAsync();
        Assert.Null(request!.CurrentLocation);
        Assert.Null(request.Criteria!.NearReservationId);
        Assert.Null(request.Criteria.MaxWalkingMinutes);
    }

    [Theory]
    [InlineData("page")]
    [InlineData("account")]
    [InlineData("trip")]
    [InlineData("date")]
    [InlineData("surface")]
    public async Task Delayed_location_is_discarded_after_context_change(string change)
    {
        using var context = await OpenAsync(Next());
        var vm = context.Vm;
        var location = new TaskCompletionSource<GeoPointDto?>(TaskCreationOptions.RunContinuationsAsynchronously);
        vm.Location.Resolve = _ => location.Task;
        vm.SelectInterest(GuidedTravelCategories.Walk);
        await vm.SubmitExpressAsync();
        var task = vm.ChooseFreeTimeCurrentAreaCommand.ExecuteAsync(null);
        if (change == "page") vm.LeavePage();
        else if (change == "surface") vm.ChangeSurface();
        else if (change == "date") vm.PlanningDate = Day.AddDays(1).ToDateTime(TimeOnly.MinValue);
        else await context.Sessions.SaveAsync(change == "trip" ? context.Session with { TripId = Guid.NewGuid() }
            : context.Session with { UserId = Guid.NewGuid() });
        location.SetResult(new(35.68m, 139.76m));
        await task;
        Assert.False(vm.FreeTimeCurrentAreaSelected);
        Assert.Null(vm.ErrorMessage);
    }

    [Theory]
    [InlineData("account")]
    [InlineData("trip")]
    [InlineData("date")]
    [InlineData("page")]
    public async Task Delayed_network_response_cannot_publish_into_another_context(string change)
    {
        using var context = await OpenAsync(Next());
        var vm = context.Vm;
        await ChooseNextAsync(vm);
        var delayed = new TaskCompletionSource<TravelChatResponse?>(TaskCreationOptions.RunContinuationsAsynchronously);
        vm.Api.SendTravelChat = (_, _) => delayed.Task;
        var request = vm.SubmitExpressAsync();
        if (change == "page") vm.LeavePage();
        else if (change == "date") vm.PlanningDate = Day.AddDays(1).ToDateTime(TimeOnly.MinValue);
        else await context.Sessions.SaveAsync(change == "trip" ? context.Session with { TripId = Guid.NewGuid() }
            : context.Session with { UserId = Guid.NewGuid() });
        delayed.SetResult(Response(Card(Guid.NewGuid())));
        await request;
        Assert.Empty(vm.Options);
        Assert.Null(vm.ErrorMessage);
    }

    [Fact]
    public async Task Replacement_changes_one_card_and_excludes_every_previously_proposed_id()
    {
        using var context = await OpenAsync(Next());
        var vm = context.Vm;
        await ChooseNextAsync(vm);
        var first = Card(Guid.NewGuid(), "Garden");
        var second = Card(Guid.NewGuid(), "Gallery");
        vm.Api.SendTravelChat = (_, _) => Task.FromResult<TravelChatResponse?>(Response(first, second));
        await vm.SubmitExpressAsync();
        var retained = vm.Options[1];
        TravelChatRequest? replacement = null;
        var third = Card(Guid.NewGuid(), "Temple");
        vm.Api.SendTravelChat = (value, _) => { replacement = value; return Task.FromResult<TravelChatResponse?>(Response(first, third)); };
        await vm.ReplaceExpressAsync(vm.Options[0]);
        Assert.Equal(third.RecommendationId, vm.Options[0].RecommendationId.ToString());
        Assert.Same(retained, vm.Options[1]);
        Assert.Equal(GuidedTravelActions.Alternative, replacement!.GuidedAction!.Action);
        Assert.Contains(Guid.Parse(first.RecommendationId!), replacement.Criteria!.ExcludedRecommendationIds!);
        Assert.Contains(Guid.Parse(second.RecommendationId!), replacement.Criteria.ExcludedRecommendationIds!);
        vm.Api.SendTravelChat = (value, _) => { replacement = value; return Task.FromResult<TravelChatResponse?>(Response(Card(Guid.NewGuid(), "Park"))); };
        await vm.ReplaceExpressAsync(vm.Options[0]);
        Assert.Contains(Guid.Parse(third.RecommendationId!), replacement!.Criteria!.ExcludedRecommendationIds!);
        Assert.Same(retained, vm.Options[1]);
    }

    [Fact]
    public async Task Duplicate_place_aliases_and_duplicate_ids_are_removed_from_initial_options()
    {
        using var context = await OpenAsync(Next());
        var vm = context.Vm;
        await ChooseNextAsync(vm);
        var id = Guid.NewGuid();
        vm.Api.SendTravelChat = (_, _) => Task.FromResult<TravelChatResponse?>(Response(
            Card(id, "Café Garden", "place-a"), Card(id, "Other title"),
            Card(Guid.NewGuid(), "Cafe Garden"), Card(Guid.NewGuid(), "Another title", "place-a"),
            Card(Guid.NewGuid(), "Gallery", "place-b"), Card(Guid.NewGuid(), "Temple", "place-c")));
        await vm.SubmitExpressAsync();
        Assert.Equal(3, vm.Options.Count);
        Assert.Equal(new[] { "Café Garden", "Gallery", "Temple" }, vm.Options.Select(option => option.Title));
    }

    [Fact]
    public async Task No_replacement_or_network_failure_keeps_all_visible_cards()
    {
        using var context = await OpenAsync(Next());
        var vm = context.Vm;
        await ChooseNextAsync(vm);
        vm.Api.SendTravelChat = (_, _) => Task.FromResult<TravelChatResponse?>(Response(Card(Guid.NewGuid(), "Garden"), Card(Guid.NewGuid(), "Gallery")));
        await vm.SubmitExpressAsync();
        var previous = vm.Options.ToArray();
        vm.Api.SendTravelChat = (_, _) => Task.FromResult<TravelChatResponse?>(Response());
        await vm.ReplaceExpressAsync(previous[0]);
        Assert.Equal(previous, vm.Options);
        Assert.NotNull(vm.ErrorMessage);
        vm.Api.SendTravelChat = (_, _) => throw new HttpRequestException();
        await vm.ReplaceExpressAsync(previous[0]);
        Assert.Equal(previous, vm.Options);
        Assert.False(previous[0].IsSearchingAlternative);
    }

    [Fact]
    public async Task Lost_response_retries_the_same_operation_and_window()
    {
        using var context = await OpenAsync(Next());
        var vm = context.Vm;
        await ChooseNextAsync(vm);
        TravelChatRequest? first = null;
        vm.Api.SendTravelChat = (value, _) => { first = value; throw new HttpRequestException(); };
        await vm.SubmitExpressAsync();
        Assert.NotNull(vm.PendingRetryRequest);
        TravelChatRequest? retry = null;
        vm.Api.SendTravelChat = (value, _) => { retry = value; return Task.FromResult<TravelChatResponse?>(Response(Card(Guid.NewGuid()))); };
        await vm.SubmitExpressAsync();
        Assert.Same(first, retry);
        Assert.Null(vm.PendingRetryRequest);
    }

    [Fact]
    public async Task Offline_keeps_choices_and_does_not_call_backend()
    {
        using var context = await OpenAsync(Next());
        var vm = context.Vm;
        await ChooseNextAsync(vm);
        var requests = 0;
        vm.Api.SendTravelChat = (_, _) => { requests++; return Task.FromResult<TravelChatResponse?>(null); };
        Connectivity.Current.NetworkAccess = NetworkAccess.None;
        await vm.SubmitExpressAsync();
        Assert.Equal(0, requests);
        Assert.True(vm.FreeTimeNextAreaSelected);
        Assert.NotNull(vm.ErrorMessage);
        Assert.True(vm.CanSubmitQuickSearch);
    }

    [Fact]
    public async Task A_changed_next_plan_requires_a_fresh_area_choice()
    {
        using var context = await OpenAsync(Next());
        var vm = context.Vm;
        await ChooseNextAsync(vm);
        context.Bootstrap.Value = context.Bootstrap.Value! with
        { Schedule = context.Bootstrap.Value.Schedule! with { Items = [Next() with { Title = "New museum" }] } };
        var requests = 0;
        vm.Api.SendTravelChat = (_, _) => { requests++; return Task.FromResult<TravelChatResponse?>(null); };
        await vm.SubmitExpressAsync();
        Assert.Equal(0, requests);
        Assert.False(vm.CanSubmitQuickSearch);
        Assert.NotNull(vm.ErrorMessage);
    }

    [Fact]
    public async Task A_valid_free_access_quota_response_is_applied_without_discarding_its_options()
    {
        using var context = await OpenAsync(Next());
        var vm = context.Vm;
        await ChooseNextAsync(vm);
        vm.Api.SendTravelChat = (_, _) => Task.FromResult<TravelChatResponse?>(Response(Card(Guid.NewGuid())) with
        { TrialAccess = new(true, TrialAccessState.Editing, DateTimeOffset.UtcNow.AddDays(1), DateTimeOffset.UtcNow.AddDays(3), 0, 24.99m, "EUR", null) });
        await vm.SubmitExpressAsync();
        Assert.Single(vm.Options);
        Assert.Equal(0, context.Sessions.TrialAssistantRequestsRemaining);
    }

    [Fact]
    public async Task Known_revoked_access_is_not_overridden_by_cached_schedule()
    {
        using var context = await OpenAsync(Next());
        var vm = context.Vm;
        await ChooseNextAsync(vm);
        context.Sessions.ApplyTrialAccess(new(true, TrialAccessState.Revoked, null, null, 0, 24.99m, "EUR", null));
        var requests = 0;
        vm.Api.SendTravelChat = (_, _) => { requests++; return Task.FromResult<TravelChatResponse?>(Response(Card(Guid.NewGuid()))); };
        await vm.SubmitExpressAsync();
        Assert.Equal(0, requests);
        Assert.Empty(vm.Options);
    }

    [Fact]
    public async Task Midnight_discards_old_response_and_offers_restart_for_today()
    {
        using var context = await OpenAsync(Next());
        var vm = context.Vm;
        await ChooseNextAsync(vm);
        var delayed = new TaskCompletionSource<TravelChatResponse?>(TaskCreationOptions.RunContinuationsAsynchronously);
        vm.Api.SendTravelChat = (_, _) => delayed.Task;
        var task = vm.SubmitExpressAsync();
        vm.FreeTimeClock = () => Now.AddDays(1);
        delayed.SetResult(Response(Card(Guid.NewGuid())));
        await task;
        Assert.Empty(vm.Options);
        Assert.True(vm.FreeTimeNeedsRestart);
        Assert.False(vm.CanSubmitQuickSearch);
        vm.ChangeSurface();
        Assert.False(vm.FreeTimeNeedsRestart);
        await vm.OpenFreeTimeForDateAsync(null);
        Assert.Equal(Day.AddDays(1), DateOnly.FromDateTime(vm.PlanningDate));
        Assert.False(vm.FreeTimeNeedsRestart);
        Assert.True(vm.ShowFreeTimeInterests);
    }

    [Fact]
    public async Task Add_validation_rejects_a_stale_interval_and_new_commitment()
    {
        using var context = await OpenAsync(Next());
        var vm = context.Vm;
        await ChooseNextAsync(vm);
        vm.Api.SendTravelChat = (_, _) => Task.FromResult<TravelChatResponse?>(Response(Card(Guid.NewGuid())));
        await vm.SubmitExpressAsync();
        Assert.True(await vm.ValidateExpressSaveAsync(vm.Options[0]));
        context.Bootstrap.Value = context.Bootstrap.Value! with
        { Schedule = context.Bootstrap.Value.Schedule! with { Items = [Next() with { StartsAt = new(10, 15), EndsAt = new(10, 30) }] } };
        Assert.False(await vm.ValidateExpressSaveAsync(vm.Options[0]));
        context.Bootstrap.Value = context.Bootstrap.Value with { Schedule = context.Bootstrap.Value.Schedule! with { Items = [] } };
        vm.FreeTimeClock = () => Now.AddMinutes(11);
        Assert.False(await vm.ValidateExpressSaveAsync(vm.Options[0]));
    }

    [Fact]
    public async Task Changing_the_search_keeps_history_and_returns_to_interests_without_starting_a_request()
    {
        using var context = await OpenAsync(Next());
        var vm = context.Vm;
        await ChooseNextAsync(vm);
        var first = Card(Guid.NewGuid());
        vm.Api.SendTravelChat = (_, _) => Task.FromResult<TravelChatResponse?>(Response(first));
        await vm.SubmitExpressAsync();
        vm.EditFreeTimeSearchCommand.Execute(null);
        Assert.True(vm.ShowFreeTimeInterests);
        Assert.False(vm.ShowAssistantProposal);
        await vm.SubmitExpressAsync();
        TravelChatRequest? request = null;
        vm.Api.SendTravelChat = (value, _) => { request = value; return Task.FromResult<TravelChatResponse?>(Response(Card(Guid.NewGuid(), "Temple"))); };
        await vm.SubmitExpressAsync();
        Assert.Contains(Guid.Parse(first.RecommendationId!), request!.Criteria!.ExcludedRecommendationIds!);
    }

    [Fact]
    public async Task Express_timing_explanation_does_not_leak_into_a_normal_assistant_surface()
    {
        using var context = await OpenAsync(Next() with { StartsAt = new(9, 30), EndsAt = new(10, 30) });
        var vm = context.Vm;
        Assert.True(vm.HasFreeTimeSoonNote);
        vm.ChangeSurface();
        Assert.False(vm.HasFreeTimeSoonNote);
        Assert.True(vm.IsStandardQuickSearch);
    }
}

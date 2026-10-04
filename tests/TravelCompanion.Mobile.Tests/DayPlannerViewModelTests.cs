using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging.Abstractions;
using TravelCompanion.Mobile.Services;
using TravelCompanion.Mobile.ViewModels;
using TravelCompanion.Shared;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Mobile.Tests;

[Collection("Free map session")]
public sealed class DayPlannerViewModelTests
{
    private static readonly DateOnly Start = new(2026, 10, 20);

    [Fact]
    public async Task Lost_generation_response_replays_same_identity_after_options_revision_changes()
    {
        using var fixture = await Fixture.CreateAsync();
        fixture.Generate = (_, _, _) => throw new OperationCanceledException("Response lost");
        await fixture.ViewModel.InitializeAsync(Start);
        await fixture.ViewModel.GenerateCommand.ExecuteAsync(null);
        var original = Assert.Single(fixture.GenerationRequests);
        Assert.Equal(DayPlannerViewModel.Text("PlannerConnectionError"), fixture.ViewModel.ErrorMessage);

        fixture.Revision = 8;
        await fixture.ViewModel.RefreshAsync();
        fixture.Generate = (request, _, _) => Task.FromResult(Proposal(request));
        await fixture.ViewModel.GenerateCommand.ExecuteAsync(null);

        Assert.Equal(2, fixture.GenerationRequests.Count);
        Assert.Equal(original.OperationId, fixture.GenerationRequests[1].OperationId);
        Assert.Equal(original.ExpectedRevision, fixture.GenerationRequests[1].ExpectedRevision);
        Assert.True(fixture.ViewModel.HasProposal);
        Assert.Single(Shell.Current.Navigations);
    }

    [Fact]
    public async Task Restart_restores_settings_without_a_pending_request_and_keeps_them_after_profile_refresh()
    {
        using var fixture = await Fixture.CreateAsync();
        var settings = new DayPlanPreferencesDto("efficient", "high", ["culture", "nature"]);
        await fixture.Store.SaveAsync(fixture.Session.UserId, fixture.Trip,
            new(fixture.Options(), null, null, [], SelectedDate: Start.AddDays(1), DayCount: 3, Preferences: settings));

        await fixture.ViewModel.InitializeAsync(null);

        Assert.Equal(Start.AddDays(1), DateOnly.FromDateTime(fixture.ViewModel.SelectedDate));
        Assert.Equal(2, fixture.ViewModel.PaceIndex);
        Assert.Equal(2, fixture.ViewModel.BudgetIndex);
        Assert.Equal(new[] { "culture", "nature" }, fixture.ViewModel.Interests.Where(item => item.IsSelected).Select(item => item.Key));
        Assert.True(fixture.ViewModel.Durations.Single(item => item.Count == 3).IsSelected);
        await fixture.ViewModel.GenerateCommand.ExecuteAsync(null);
        var request = Assert.Single(fixture.GenerationRequests);
        Assert.Equal(3, request.DayCount);
        Assert.Equal(settings.TravelPace, request.Preferences!.TravelPace);
        Assert.Equal(settings.Budget, request.Preferences.Budget);
        Assert.Equal(settings.Interests, request.Preferences.Interests);
    }

    [Fact]
    public async Task Offline_restart_displays_restored_duration_and_range_even_without_options_refresh()
    {
        using var fixture = await Fixture.CreateAsync();
        await fixture.Store.SaveAsync(fixture.Session.UserId, fixture.Trip,
            new(fixture.Options(), null, null, [], SelectedDate: Start, DayCount: 3, Preferences: new("balanced", "medium", [])));
        Connectivity.Current.NetworkAccess = NetworkAccess.None;

        await fixture.ViewModel.InitializeAsync(null);

        Assert.True(fixture.ViewModel.Durations.Single(item => item.Count == 3).IsSelected);
        Assert.Equal($"{Start.ToDateTime(TimeOnly.MinValue):d MMM} — {Start.AddDays(2).ToDateTime(TimeOnly.MinValue):d MMM}", fixture.ViewModel.RangeSummary);
        Assert.Equal(0, fixture.HttpRequests);
    }

    [Fact]
    public async Task Explicit_different_date_starts_a_new_intent_instead_of_replaying_previous_day()
    {
        using var fixture = await Fixture.CreateAsync();
        fixture.Generate = (_, _, _) => throw new OperationCanceledException("Response lost");
        await fixture.ViewModel.InitializeAsync(Start);
        await fixture.ViewModel.GenerateCommand.ExecuteAsync(null);
        var original = Assert.Single(fixture.GenerationRequests);
        fixture.Generate = (request, _, _) => Task.FromResult(Proposal(request));
        var restarted = fixture.NewViewModel();

        await restarted.InitializeAsync(Start.AddDays(1));
        await restarted.GenerateCommand.ExecuteAsync(null);

        Assert.Equal(Start.AddDays(1), fixture.GenerationRequests[1].StartDate);
        Assert.NotEqual(original.OperationId, fixture.GenerationRequests[1].OperationId);
    }

    [Fact]
    public async Task Partial_application_advances_revision_for_remaining_selection_and_next_generation()
    {
        using var fixture = await Fixture.CreateAsync();
        fixture.Apply = (request, _, _) => Task.FromResult(new DayPlanApplyResponse(true, "Saved", request.TripId,
            request.ExpectedRevision + 1, []));
        await fixture.ViewModel.InitializeAsync(Start);
        await fixture.ViewModel.GenerateCommand.ExecuteAsync(null);
        var rows = fixture.ViewModel.Days.SelectMany(day => day).ToArray();
        rows[1].IsSelected = false;
        await fixture.ViewModel.ApplyCommand.ExecuteAsync(null);

        Assert.Equal(5, Assert.Single(fixture.ApplicationRequests).ExpectedRevision);
        Assert.True(rows[0].IsSaved);
        Assert.False(rows[0].CanSelectRow);
        Assert.False(rows[1].IsSaved);
        rows[1].IsSelected = true;
        await fixture.ViewModel.ApplyCommand.ExecuteAsync(null);
        Assert.Equal(6, fixture.ApplicationRequests[1].ExpectedRevision);
        await fixture.ViewModel.GenerateCommand.ExecuteAsync(null);
        Assert.Equal(7, fixture.GenerationRequests[1].ExpectedRevision);
        Assert.NotEqual(fixture.GenerationRequests[0].OperationId, fixture.GenerationRequests[1].OperationId);
        Assert.Equal(2, fixture.Bootstrap.InvalidationCount);
    }

    [Fact]
    public async Task Applying_all_selected_ideas_offers_open_trip_on_the_proposal_first_day()
    {
        using var fixture = await Fixture.CreateAsync();
        await fixture.ViewModel.InitializeAsync(Start.AddDays(1));
        await fixture.ViewModel.GenerateCommand.ExecuteAsync(null);
        Assert.False(fixture.ViewModel.ShowOpenTrip);
        Assert.True(fixture.ViewModel.ShowApply);

        await fixture.ViewModel.ApplyCommand.ExecuteAsync(null);

        Assert.Equal(0, fixture.ViewModel.SelectedCount);
        Assert.All(fixture.ViewModel.Days.SelectMany(day => day), row => Assert.True(row.IsSaved));
        Assert.True(fixture.ViewModel.ShowOpenTrip);
        Assert.False(fixture.ViewModel.ShowApply);
        await fixture.ViewModel.ViewTripCommand.ExecuteAsync(null);
        var navigation = Shell.Current.Navigations.Last();
        Assert.Equal("//main/schedule", navigation.Route);
        Assert.Equal(Start.AddDays(1), navigation.Parameters["InitialDate"]);
    }

    [Fact]
    public async Task Lost_application_response_survives_refresh_and_restart_then_replays_same_mutation()
    {
        using var fixture = await Fixture.CreateAsync();
        await fixture.ViewModel.InitializeAsync(Start);
        await fixture.ViewModel.GenerateCommand.ExecuteAsync(null);
        fixture.Apply = (_, _, _) => throw new OperationCanceledException("Response lost");
        await fixture.ViewModel.ApplyCommand.ExecuteAsync(null);
        var original = Assert.Single(fixture.ApplicationRequests);
        fixture.Revision = 6;
        await fixture.ViewModel.RefreshAsync();
        Assert.False(fixture.ViewModel.Stale);
        Assert.False(fixture.ViewModel.CanGenerate);
        Assert.All(fixture.ViewModel.Days.SelectMany(day => day), row => Assert.False(row.CanSelectRow));

        var restarted = fixture.NewViewModel();
        await restarted.InitializeAsync(null);
        fixture.Apply = (request, _, _) => Task.FromResult(new DayPlanApplyResponse(true, "Saved", request.TripId, 6, []));
        await restarted.ApplyCommand.ExecuteAsync(null);

        Assert.Equal(original.MutationId, fixture.ApplicationRequests[1].MutationId);
        Assert.Equal(original.ExpectedRevision, fixture.ApplicationRequests[1].ExpectedRevision);
        Assert.Equal(original.SelectedStopIds, fixture.ApplicationRequests[1].SelectedStopIds);
        Assert.All(restarted.Days.SelectMany(day => day), row => Assert.True(row.IsSaved));
        var draft = await fixture.Store.ReadAsync(fixture.Session.UserId, fixture.Trip);
        Assert.Null(draft!.PendingApplication);
        Assert.Equal(6, draft.AppliedRevision);
    }

    [Fact]
    public async Task Explicit_cancellation_preserves_request_and_reports_cancelled_instead_of_connection_failure()
    {
        using var fixture = await Fixture.CreateAsync();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Generate = async (_, _, ct) =>
        {
            started.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            throw new InvalidOperationException("Unreachable");
        };
        await fixture.ViewModel.InitializeAsync(Start);
        var pending = fixture.ViewModel.GenerateCommand.ExecuteAsync(null);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(fixture.ViewModel.IsBusy);
        fixture.ViewModel.CancelCommand.Execute(null);
        await pending;

        Assert.False(fixture.ViewModel.IsBusy);
        Assert.Null(fixture.ViewModel.ErrorMessage);
        Assert.Equal(DayPlannerViewModel.Text("PlannerCancelled"), fixture.ViewModel.StatusMessage);
        Assert.NotNull((await fixture.Store.ReadAsync(fixture.Session.UserId, fixture.Trip))!.Request);
    }

    [Fact]
    public async Task Saved_rows_remain_disabled_after_restart_and_remaining_rows_can_be_selected()
    {
        using var fixture = await Fixture.CreateAsync();
        await fixture.ViewModel.InitializeAsync(Start);
        await fixture.ViewModel.GenerateCommand.ExecuteAsync(null);
        fixture.ViewModel.Days[0][1].IsSelected = false;
        await fixture.ViewModel.ApplyCommand.ExecuteAsync(null);
        fixture.Revision = 6;
        var restarted = fixture.NewViewModel();
        await restarted.InitializeAsync(null);
        Assert.False(restarted.Stale);
        Assert.False(restarted.Days[0][0].CanSelectRow);
        Assert.True(restarted.Days[0][1].CanSelectRow);
        restarted.SelectDayCommand.Execute(restarted.Days[0]);
        Assert.Equal(1, restarted.SelectedCount);
        Assert.False(restarted.Days[0][0].IsSelected);
        await restarted.SaveDraftAsync();
    }

    [Fact]
    public async Task Returning_response_is_discarded_when_context_changes_even_if_user_and_trip_are_the_same()
    {
        using var fixture = await Fixture.CreateAsync();
        fixture.Generate = async (request, _, _) =>
        {
            await fixture.Sessions.SaveAsync(fixture.Session);
            return Proposal(request);
        };
        await fixture.ViewModel.InitializeAsync(Start);
        await fixture.ViewModel.GenerateCommand.ExecuteAsync(null);
        Assert.False(fixture.ViewModel.HasProposal);
        Assert.Empty(Shell.Current.Navigations);
        Assert.False(fixture.ViewModel.IsBusy);
    }

    [Fact]
    public async Task Empty_and_partial_days_remain_in_the_proposal_and_review_uses_selected_date_range()
    {
        using var fixture = await Fixture.CreateAsync();
        fixture.Generate = (request, _, _) => Task.FromResult(Proposal(request) with
        {
            Days = [new(request.StartDate, ["Tokyo"], [], ["morning"]),
                new(request.StartDate.AddDays(1), ["Tokyo"], Proposal(request).Days[0].Stops, ["night"])]
        });
        await fixture.ViewModel.InitializeAsync(Start.AddDays(1));
        await fixture.ViewModel.SelectDurationCommand.ExecuteAsync(fixture.ViewModel.Durations.Single(item => item.Count == 3));
        await fixture.ViewModel.GenerateCommand.ExecuteAsync(null);
        Assert.Equal(2, fixture.ViewModel.Days.Count);
        Assert.True(fixture.ViewModel.Days[0].IsEmpty);
        Assert.Equal(DayPlannerViewModel.Text("PlannerEmptyDay"), fixture.ViewModel.Days[0].EmptyMessage);
        Assert.Equal(DayPlannerViewModel.Text("PlannerPartialDay"), fixture.ViewModel.Days[1].EmptyMessage);
        await fixture.ViewModel.ReviewCommand.ExecuteAsync(null);
        var query = Shell.Current.Navigations.Last().Parameters;
        Assert.Equal(Start.AddDays(1), query["ReviewStartDate"]);
        Assert.Equal(Start.AddDays(3), query["ReviewEndDate"]);
    }

    [Fact]
    public async Task Offline_restart_retains_cached_proposal_without_network_calls_or_enabling_writes()
    {
        using var fixture = await Fixture.CreateAsync();
        await fixture.ViewModel.InitializeAsync(Start);
        await fixture.ViewModel.GenerateCommand.ExecuteAsync(null);
        var requestCount = fixture.HttpRequests;
        Connectivity.Current.NetworkAccess = NetworkAccess.None;
        var restarted = fixture.NewViewModel();
        await restarted.InitializeAsync(null);
        Assert.True(restarted.HasProposal);
        Assert.False(restarted.CanGenerate);
        Assert.False(restarted.CanApply);
        Assert.Equal(requestCount, fixture.HttpRequests);
        await restarted.ContinueProposalCommand.ExecuteAsync(null);
        Assert.Equal(2, Shell.Current.Navigations.Count);
    }

    [Fact]
    public async Task Free_preview_limits_date_window_and_shows_paid_duration_without_sending_generation()
    {
        using var fixture = await Fixture.CreateAsync();
        fixture.TrialAccess = new(true, TrialAccessState.Editing, DateTimeOffset.UtcNow.AddDays(1), null,
            3, 10, "USD", null) { DayImprovementsRemaining = 3 };
        await fixture.ViewModel.InitializeAsync(Start);
        Assert.Equal(Start.AddDays(2), DateOnly.FromDateTime(fixture.ViewModel.MaximumDate));
        Assert.True(fixture.ViewModel.Durations.Single(item => item.Count == 3).Available);
        Assert.False(fixture.ViewModel.Durations.Single(item => item.Count == 5).Available);
        await fixture.ViewModel.SelectDurationCommand.ExecuteAsync(fixture.ViewModel.Durations.Single(item => item.Count == 5));
        Assert.Equal("pass", Assert.Single(Shell.Current.Navigations).Route);
        Assert.Empty(fixture.GenerationRequests);

        fixture.ViewModel.SelectedDate = Start.AddDays(1).ToDateTime(TimeOnly.MinValue);
        Assert.False(fixture.ViewModel.Durations.Single(item => item.Count == 3).Available);
        Assert.True(fixture.ViewModel.Durations.Single(item => item.Count == 1).IsSelected);
    }

    [Theory]
    [InlineData("io")]
    [InlineData("permission")]
    [InlineData("encryption")]
    public async Task Local_save_failure_preserves_preview_and_selection_and_does_not_escape_page_exit(string failure)
    {
        using var fixture = await Fixture.CreateAsync();
        await fixture.ViewModel.InitializeAsync(Start);
        await fixture.ViewModel.GenerateCommand.ExecuteAsync(null);
        var selected = fixture.ViewModel.Days.SelectMany(day => day).Where(row => row.IsSelected).Select(row => row.Value.Id).ToArray();
        fixture.Cache.BeforeSave = () => throw failure switch
        {
            "io" => new IOException("Synthetic storage failure"),
            "permission" => new UnauthorizedAccessException("Synthetic storage failure"),
            _ => new System.Security.Cryptography.CryptographicException("Synthetic storage failure")
        };

        await fixture.ViewModel.SaveDraftAsync();

        Assert.Equal(DayPlannerViewModel.Text("PlannerLocalSaveFailed"), fixture.ViewModel.ErrorMessage);
        Assert.True(fixture.ViewModel.HasProposal);
        Assert.Equal(selected, fixture.ViewModel.Days.SelectMany(day => day).Where(row => row.IsSelected).Select(row => row.Value.Id));
        fixture.Cache.BeforeSave = null;
        await fixture.ViewModel.SaveDraftAsync();
        Assert.Equal(selected, (await fixture.Store.ReadAsync(fixture.Session.UserId, fixture.Trip))!.SelectedStopIds);
    }

    private static DayPlanResponse Proposal(DayPlanRequest request)
    {
        DayPlanStopDto Stop(string period, string title) => new(Guid.NewGuid(), Guid.NewGuid(), period,
            new TravelCardDto("recommendation", title, "Tokyo", null, null, null, null, null, null,
                [], [], null, null) { IsPeriodOnly = true, PeriodKey = period });
        return new(request.OperationId, request.TripId, request.ExpectedRevision,
            [new(request.StartDate, ["Tokyo"], [Stop("morning", "Garden"), Stop("afternoon", "Museum")], [])], "Ideas");
    }

    private sealed class Fixture : IDisposable
    {
        private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
        { Converters = { new JsonStringEnumConverter() } };
        public AuthSessionService Sessions { get; } = new();
        public AuthSessionDto Session { get; } = new(Guid.NewGuid(), "planner@example.test", "Planner", false,
            "token", Guid.NewGuid(), AccessMode: SessionAccessMode.FreeMapPreview, ExperienceMode: ExperienceMode.FreePreview);
        public Guid Trip => Session.TripId!.Value;
        public int Revision { get; set; } = 5;
        public TrialAccessStatusDto? TrialAccess { get; set; }
        public int HttpRequests { get; private set; }
        public List<DayPlanRequest> GenerationRequests { get; } = [];
        public List<DayPlanApplyRequest> ApplicationRequests { get; } = [];
        public Func<DayPlanRequest, int, CancellationToken, Task<DayPlanResponse>> Generate { get; set; } = (request, _, _) => Task.FromResult(Proposal(request));
        public Func<DayPlanApplyRequest, int, CancellationToken, Task<DayPlanApplyResponse>> Apply { get; set; } = (request, _, _) =>
            Task.FromResult(new DayPlanApplyResponse(true, "Saved", request.TripId, request.ExpectedRevision + 1, []));
        public MobileBootstrapStore Bootstrap { get; } = new();
        public OfflineCacheService Cache { get; } = new();
        public DayPlannerStore Store { get; }
        public DayPlannerViewModel ViewModel { get; private set; } = null!;
        private readonly DayPlanClient client;
        private readonly TravelCompanionApiClient api = new();
        private Fixture()
        {
            Store = new(Cache, Sessions);
            client = new(new HttpClient(new Handler(SendAsync)) { BaseAddress = new("https://example.invalid/") });
        }
        public static async Task<Fixture> CreateAsync()
        {
            Connectivity.Current.NetworkAccess = NetworkAccess.Internet;
            Shell.Current = new();
            var fixture = new Fixture();
            await fixture.Sessions.SaveAsync(fixture.Session);
            fixture.ViewModel = fixture.NewViewModel();
            return fixture;
        }
        public DayPlannerViewModel NewViewModel() => new(Sessions, client, Store, Bootstrap, api, NullLogger<DayPlannerViewModel>.Instance);
        public DayPlanOptionsDto Options() => new(true, Trip, Revision, Start, Start.AddDays(9), [1, 3, 5, 7],
            new(Session.UserId, [], [], "medium", "balanced", ["food"], [], false, 30, true, [], null), TrialAccess);
        private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage message, CancellationToken ct)
        {
            HttpRequests++;
            object result;
            if (message.Method == HttpMethod.Get) result = Options();
            else if (message.RequestUri!.AbsolutePath.EndsWith("/apply", StringComparison.Ordinal))
            {
                var request = (await message.Content!.ReadFromJsonAsync<DayPlanApplyRequest>(Json, ct))!;
                ApplicationRequests.Add(request);
                result = await Apply(request, ApplicationRequests.Count, ct);
            }
            else
            {
                var request = (await message.Content!.ReadFromJsonAsync<DayPlanRequest>(Json, ct))!;
                GenerationRequests.Add(request);
                result = await Generate(request, GenerationRequests.Count, ct);
            }
            return new(HttpStatusCode.OK) { Content = JsonContent.Create(result, result.GetType(), options: Json) };
        }
        public void Dispose()
        {
            client.Dispose(); Sessions.Clear(); Connectivity.Current.NetworkAccess = NetworkAccess.Internet; Shell.Current = new();
        }
        private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => send(request, ct);
        }
    }
}

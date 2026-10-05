using System.Net;
using TravelCompanion.Mobile.Services;
using TravelCompanion.Mobile.ViewModels;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Mobile.Tests;

public sealed partial class DayPlannerViewModelTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Replacement_preserves_selection_other_rows_and_the_original_preferences(bool selected)
    {
        using var fixture = await Fixture.CreateAsync();
        var initial = await GenerateForReplacementAsync(fixture);
        var old = fixture.ViewModel.Days[0][0];
        var other = fixture.ViewModel.Days[0][1];
        old.IsSelected = selected;
        fixture.ViewModel.PaceIndex = 2; // Form changes must not change the preferences used for this proposal.
        var answer = Replacement(initial, old.Value.Id);
        fixture.Replace = (_, _, _) => Task.FromResult(answer);

        await fixture.ViewModel.ReplaceStopCommand.ExecuteAsync(old);

        Assert.Same(other, fixture.ViewModel.Days[0][1]);
        Assert.Equal(selected, fixture.ViewModel.Days[0][0].IsSelected);
        Assert.Equal(answer.Proposal.Days[0].Stops[0].Id, fixture.ViewModel.Days[0][0].Value.Id);
        var sent = Assert.Single(fixture.ReplacementRequests);
        Assert.Equal(initial.OperationId, sent.OperationId);
        Assert.Equal("balanced", sent.OriginalRequest!.Preferences!.TravelPace);
        var saved = (await fixture.Store.ReadAsync(fixture.Session.UserId, fixture.Trip))!;
        Assert.Null(saved.PendingReplacement);
        Assert.Equal(1, saved.Proposal!.ProposalRevision);
        Assert.Contains(old.Value.RecommendationId, saved.SeenRecommendationIds!);
        Assert.Contains(answer.Proposal.Days[0].Stops[0].RecommendationId, saved.SeenRecommendationIds!);
    }

    [Fact]
    public async Task Exhaustion_keeps_the_idea_and_displays_a_notice_on_its_row()
    {
        using var fixture = await Fixture.CreateAsync();
        var initial = await GenerateForReplacementAsync(fixture);
        var row = fixture.ViewModel.Days[0][0];
        fixture.Replace = (_, _, _) => Task.FromResult(new DayPlanReplaceResponse(false, "no_alternative", "No ideas", initial));
        await fixture.ViewModel.ReplaceStopCommand.ExecuteAsync(row);
        Assert.Same(row, fixture.ViewModel.Days[0][0]);
        Assert.True(row.HasReplacementNotice);
        Assert.Equal(DayPlannerViewModel.Text("PlannerNoAlternative"), row.ReplacementNotice);
        Assert.True(fixture.ViewModel.CanApply);
        Assert.Null((await fixture.Store.ReadAsync(fixture.Session.UserId, fixture.Trip))!.PendingReplacement);
    }

    [Fact]
    public async Task Lost_response_survives_restart_and_replays_the_same_mutation_before_saving()
    {
        using var fixture = await Fixture.CreateAsync();
        var initial = await GenerateForReplacementAsync(fixture);
        var old = fixture.ViewModel.Days[0][0];
        var answer = Replacement(initial, old.Value.Id);
        fixture.Replace = (_, _, _) => throw new OperationCanceledException("Lost response");
        await fixture.ViewModel.ReplaceStopCommand.ExecuteAsync(old);
        Assert.False(fixture.ViewModel.CanApply);
        Assert.False(fixture.ViewModel.CanGenerate);
        Assert.True(old.CanReplaceRow);
        Assert.False(fixture.ViewModel.Days[0][1].CanReplaceRow);
        Assert.Equal(DayPlannerViewModel.Text("PlannerReplacementPending"), old.ReplacementNotice);

        var restarted = fixture.NewViewModel();
        await restarted.InitializeAsync(null);
        Assert.True(restarted.Days[0][0].CanReplaceRow);
        fixture.Replace = (_, _, _) => Task.FromResult(answer);
        await restarted.ReplaceStopCommand.ExecuteAsync(restarted.Days[0][0]);
        Assert.Equal(System.Text.Json.JsonSerializer.Serialize(fixture.ReplacementRequests[0]),
            System.Text.Json.JsonSerializer.Serialize(fixture.ReplacementRequests[1]));
        await restarted.ApplyCommand.ExecuteAsync(null);
        Assert.Contains(answer.Proposal.Days[0].Stops[0].Id, Assert.Single(fixture.ApplicationRequests).SelectedStopIds);
        Assert.DoesNotContain(old.Value.Id, fixture.ApplicationRequests[0].SelectedStopIds);
        Assert.All(restarted.Days[0], stop => Assert.True(stop.IsSaved));
    }

    [Fact]
    public async Task Pending_replacement_blocks_other_commands_and_cancellation_preserves_its_identity()
    {
        using var fixture = await Fixture.CreateAsync();
        var initial = await GenerateForReplacementAsync(fixture);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Replace = async (_, _, ct) =>
        {
            started.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            throw new InvalidOperationException("Unreachable");
        };
        var row = fixture.ViewModel.Days[0][0];
        var pending = fixture.ViewModel.ReplaceStopCommand.ExecuteAsync(row);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(row.IsReplacing);
        Assert.False(fixture.ViewModel.CanSelect);
        Assert.All(fixture.ViewModel.Days[0], stop => Assert.False(stop.CanReplaceRow));
        await fixture.ViewModel.ReplaceStopCommand.ExecuteAsync(fixture.ViewModel.Days[0][1]);
        await fixture.ViewModel.ApplyCommand.ExecuteAsync(null);
        await fixture.ViewModel.GenerateCommand.ExecuteAsync(null);
        Assert.Single(fixture.ReplacementRequests);
        Assert.Empty(fixture.ApplicationRequests);
        Assert.Single(fixture.GenerationRequests);
        fixture.ViewModel.CancelCommand.Execute(null);
        await pending;
        Assert.False(row.IsReplacing);
        var mutation = fixture.ReplacementRequests[0].MutationId;
        fixture.Replace = (_, _, _) => Task.FromResult(Replacement(initial, row.Value.Id));
        await fixture.ViewModel.ReplaceStopCommand.ExecuteAsync(row);
        Assert.Equal(mutation, fixture.ReplacementRequests[1].MutationId);
    }

    [Fact]
    public async Task A_pending_receipt_remains_retryable_after_restart_when_another_device_advanced_the_itinerary()
    {
        using var fixture = await Fixture.CreateAsync();
        var initial = await GenerateForReplacementAsync(fixture);
        var old = fixture.ViewModel.Days[0][0];
        var answer = Replacement(initial, old.Value.Id);
        fixture.Replace = (_, _, _) => throw new OperationCanceledException("Receipt lost");
        await fixture.ViewModel.ReplaceStopCommand.ExecuteAsync(old);
        fixture.Revision = 8;
        var restarted = fixture.NewViewModel();
        await restarted.InitializeAsync(null);
        Assert.True(restarted.Stale);
        Assert.True(restarted.Days[0][0].CanReplaceRow);
        Assert.False(restarted.Days[0][1].CanReplaceRow);
        fixture.Replace = (_, _, _) => Task.FromResult(answer);
        await restarted.ReplaceStopCommand.ExecuteAsync(restarted.Days[0][0]);
        Assert.Equal(fixture.ReplacementRequests[0].MutationId, fixture.ReplacementRequests[1].MutationId);
        Assert.False(restarted.CanApply);
        Assert.True(restarted.CanGenerate);
        Assert.Null((await fixture.Store.ReadAsync(fixture.Session.UserId, fixture.Trip))!.PendingReplacement);
    }

    [Fact]
    public async Task Local_receipt_failure_keeps_the_old_row_and_same_mutation_retryable()
    {
        using var fixture = await Fixture.CreateAsync();
        var initial = await GenerateForReplacementAsync(fixture);
        var old = fixture.ViewModel.Days[0][0];
        var answer = Replacement(initial, old.Value.Id);
        fixture.Replace = (_, _, _) =>
        {
            fixture.Cache.BeforeSave = () => throw new IOException("Synthetic receipt write failure");
            return Task.FromResult(answer);
        };
        await fixture.ViewModel.ReplaceStopCommand.ExecuteAsync(old);
        Assert.Same(old, fixture.ViewModel.Days[0][0]);
        Assert.Equal(DayPlannerViewModel.Text("PlannerLocalSaveFailed"), fixture.ViewModel.ErrorMessage);
        Assert.True(old.CanReplaceRow);
        Assert.NotNull((await fixture.Store.ReadAsync(fixture.Session.UserId, fixture.Trip))!.PendingReplacement);
        fixture.Cache.BeforeSave = null;
        fixture.Replace = (_, _, _) => Task.FromResult(answer);
        await fixture.ViewModel.ReplaceStopCommand.ExecuteAsync(old);
        Assert.Equal(fixture.ReplacementRequests[0].MutationId, fixture.ReplacementRequests[1].MutationId);
        Assert.Equal(answer.Proposal.Days[0].Stops[0].Id, fixture.ViewModel.Days[0][0].Value.Id);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Account_or_trip_switch_during_response_discards_the_result_even_if_original_context_returns(bool sameAccount)
    {
        using var fixture = await Fixture.CreateAsync();
        var initial = await GenerateForReplacementAsync(fixture);
        var old = fixture.ViewModel.Days[0][0];
        fixture.Replace = async (_, _, _) =>
        {
            await fixture.Sessions.SaveAsync(fixture.Session with
            { UserId = sameAccount ? fixture.Session.UserId : Guid.NewGuid(), TripId = Guid.NewGuid() });
            await fixture.Sessions.SaveAsync(fixture.Session);
            return Replacement(initial, old.Value.Id);
        };
        await fixture.ViewModel.ReplaceStopCommand.ExecuteAsync(old);
        Assert.Same(old, fixture.ViewModel.Days[0][0]);
        Assert.Null(fixture.ViewModel.ErrorMessage);
        Assert.NotNull((await fixture.Store.ReadAsync(fixture.Session.UserId, fixture.Trip))!.PendingReplacement);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Repeated_historical_or_visible_deselected_recommendations_are_rejected(bool historical)
    {
        using var fixture = await Fixture.CreateAsync();
        var initial = await GenerateForReplacementAsync(fixture);
        var old = fixture.ViewModel.Days[0][0];
        fixture.ViewModel.Days[0][1].IsSelected = false;
        var answer = Replacement(initial, old.Value.Id);
        fixture.Replace = (_, _, _) => Task.FromResult(answer);
        if (historical) await fixture.ViewModel.ReplaceStopCommand.ExecuteAsync(old);
        var current = fixture.ViewModel.Days[0][0];
        var previous = historical ? answer.Proposal : initial;
        var invalid = Replacement(previous, current.Value.Id, historical ? old.Value.RecommendationId : initial.Days[0].Stops[1].RecommendationId);
        fixture.Replace = (_, _, _) => Task.FromResult(invalid);
        await fixture.ViewModel.ReplaceStopCommand.ExecuteAsync(current);
        Assert.Same(current, fixture.ViewModel.Days[0][0]);
        Assert.NotNull(fixture.ViewModel.ErrorMessage);
        Assert.True(current.CanReplaceRow);
        Assert.False(fixture.ViewModel.CanApply);
    }

    [Fact]
    public async Task A_saved_row_cannot_be_replaced_and_the_remaining_row_uses_the_advanced_itinerary_revision()
    {
        using var fixture = await Fixture.CreateAsync();
        var initial = await GenerateForReplacementAsync(fixture);
        fixture.ViewModel.Days[0][1].IsSelected = false;
        await fixture.ViewModel.ApplyCommand.ExecuteAsync(null);
        var saved = fixture.ViewModel.Days[0][0];
        Assert.False(saved.CanReplaceRow);
        await fixture.ViewModel.ReplaceStopCommand.ExecuteAsync(saved);
        Assert.Empty(fixture.ReplacementRequests);
        var remaining = fixture.ViewModel.Days[0][1];
        fixture.Replace = (_, _, _) => Task.FromResult(Replacement(initial, remaining.Value.Id));
        await fixture.ViewModel.ReplaceStopCommand.ExecuteAsync(remaining);
        Assert.Same(saved, fixture.ViewModel.Days[0][0]);
        Assert.True(saved.IsSaved);
        Assert.Equal(6, Assert.Single(fixture.ReplacementRequests).ExpectedRevision);
        fixture.ViewModel.Days[0][1].IsSelected = true;
        await fixture.ViewModel.ApplyCommand.ExecuteAsync(null);
        Assert.Equal(6, fixture.ApplicationRequests[1].ExpectedRevision);
    }

    [Fact]
    public async Task Offline_replacement_keeps_the_proposal_and_does_not_make_network_calls()
    {
        using var fixture = await Fixture.CreateAsync();
        await GenerateForReplacementAsync(fixture);
        var old = fixture.ViewModel.Days[0][0];
        Connectivity.Current.NetworkAccess = NetworkAccess.None;
        fixture.ViewModel.NotifyNetworkState();
        await fixture.ViewModel.ReplaceStopCommand.ExecuteAsync(old);
        Assert.False(old.CanReplaceRow);
        Assert.Same(old, fixture.ViewModel.Days[0][0]);
        Assert.Empty(fixture.ReplacementRequests);
    }

    [Fact]
    public async Task Stale_replacement_recovers_canonical_cards_without_selecting_another_devices_new_idea()
    {
        using var fixture = await Fixture.CreateAsync();
        var initial = await GenerateForReplacementAsync(fixture);
        var old = fixture.ViewModel.Days[0][0];
        var unchanged = fixture.ViewModel.Days[0][1];
        unchanged.IsSelected = false;
        var canonical = Replacement(initial, old.Value.Id).Proposal;
        fixture.Replace = (_, _, _) => throw new DayPlanApiException(HttpStatusCode.Conflict, "stale", "Other device changed this proposal");
        fixture.Generate = (_, _, _) => Task.FromResult(canonical);
        fixture.ViewModel.SelectedDate = Start.AddDays(1).ToDateTime(TimeOnly.MinValue);
        fixture.ViewModel.PaceIndex = 2;
        await fixture.ViewModel.ReplaceStopCommand.ExecuteAsync(old);
        Assert.Equal(initial.OperationId, fixture.GenerationRequests[1].OperationId);
        Assert.Equal(Start, fixture.GenerationRequests[1].StartDate);
        Assert.Equal("balanced", fixture.GenerationRequests[1].Preferences!.TravelPace);
        Assert.All(fixture.ViewModel.Days[0], stop => Assert.False(stop.IsSelected));
        Assert.False(fixture.ViewModel.Stale);
        Assert.Equal(DayPlannerViewModel.Text("PlannerReplacementChanged"), fixture.ViewModel.StatusMessage);
        Assert.True(fixture.ViewModel.Days[0][0].HasReplacementNotice);
        Assert.Null((await fixture.Store.ReadAsync(fixture.Session.UserId, fixture.Trip))!.PendingReplacement);
    }

    [Fact]
    public void Duration_options_map_to_four_equal_columns_and_announce_selection()
    {
        var options = new[] { 1, 3, 5, 7 }.Select(count => new PlannerDurationOption(count, $"{count} days", true, true, false)).ToArray();
        Assert.Equal(new[] { 0, 1, 2, 3 }, options.Select(option => option.ColumnIndex));
        options[1].IsSelected = true;
        Assert.Equal(string.Format(DayPlannerViewModel.Text("PlannerDurationSelected"), "3 days"), options[1].AccessibilityLabel);
    }

    [Theory]
    [InlineData("operation")]
    [InlineData("selection")]
    [InlineData("stale")]
    public async Task Deterministic_legacy_rejection_releases_pending_state_without_erasing_the_idea(string code)
    {
        using var fixture = await Fixture.CreateAsync();
        await GenerateForReplacementAsync(fixture);
        var saved = (await fixture.Store.ReadAsync(fixture.Session.UserId, fixture.Trip))!;
        await fixture.Store.SaveAsync(fixture.Session.UserId, fixture.Trip, saved with { Request = null, ProposalRequest = null });
        var legacy = fixture.NewViewModel();
        await legacy.InitializeAsync(null);
        var row = legacy.Days[0][0];
        fixture.Replace = (_, _, _) => throw new DayPlanApiException(HttpStatusCode.Conflict, code, "Start another proposal");
        await legacy.ReplaceStopCommand.ExecuteAsync(row);
        Assert.Same(row, legacy.Days[0][0]);
        Assert.True(legacy.CanGenerate);
        Assert.Equal("Start another proposal", row.ReplacementNotice);
        Assert.Equal("Start another proposal", legacy.ErrorMessage);
        Assert.Null((await fixture.Store.ReadAsync(fixture.Session.UserId, fixture.Trip))!.PendingReplacement);
        if (code == "stale") Assert.True(legacy.Stale);
    }

    [Fact]
    public async Task A_failed_local_write_during_deterministic_rejection_keeps_the_exact_pending_mutation()
    {
        using var fixture = await Fixture.CreateAsync();
        await GenerateForReplacementAsync(fixture);
        var row = fixture.ViewModel.Days[0][0];
        fixture.Replace = (_, _, _) =>
        {
            fixture.Cache.BeforeSave = () => throw new IOException("Receipt failure");
            throw new DayPlanApiException(HttpStatusCode.Conflict, "operation", "Start another proposal");
        };
        await fixture.ViewModel.ReplaceStopCommand.ExecuteAsync(row);
        Assert.True(row.CanReplaceRow);
        Assert.False(fixture.ViewModel.CanGenerate);
        Assert.NotNull((await fixture.Store.ReadAsync(fixture.Session.UserId, fixture.Trip))!.PendingReplacement);
        fixture.Cache.BeforeSave = null;
        fixture.Replace = (_, _, _) => throw new DayPlanApiException(HttpStatusCode.Conflict, "operation", "Start another proposal");
        await fixture.ViewModel.ReplaceStopCommand.ExecuteAsync(row);
        Assert.Equal(fixture.ReplacementRequests[0].MutationId, fixture.ReplacementRequests[1].MutationId);
        Assert.True(fixture.ViewModel.CanGenerate);
    }

    private static async Task<DayPlanResponse> GenerateForReplacementAsync(Fixture fixture)
    {
        await fixture.ViewModel.InitializeAsync(Start);
        await fixture.ViewModel.GenerateCommand.ExecuteAsync(null);
        return (await fixture.Store.ReadAsync(fixture.Session.UserId, fixture.Trip))!.Proposal!;
    }

    private static DayPlanReplaceResponse Replacement(DayPlanResponse initial, Guid stopId, Guid? recommendation = null)
    {
        var days = initial.Days.Select(day => day with { Stops = day.Stops.Select(stop => stop.Id == stopId
            ? stop with { Id = Guid.NewGuid(), RecommendationId = recommendation ?? Guid.NewGuid(), Card = stop.Card with { Title = "A different idea" } }
            : stop).ToArray() }).ToArray();
        return new(true, "replaced", "A different idea", initial with { Days = days, ProposalRevision = initial.ProposalRevision + 1 });
    }
}

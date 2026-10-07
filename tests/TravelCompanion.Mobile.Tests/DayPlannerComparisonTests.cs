using TravelCompanion.Shared.Dtos;
using TravelCompanion.Mobile.ViewModels;
namespace TravelCompanion.Mobile.Tests;

public sealed partial class DayPlannerViewModelTests
{
    [Fact]
    public async Task Two_alternatives_survive_restart_and_switch_as_complete_proposals()
    {
        using var fixture = await Fixture.CreateAsync();
        await fixture.ViewModel.InitializeAsync(Start);
        await fixture.ViewModel.GenerateCommand.ExecuteAsync(null);
        var first = (await fixture.Store.ReadAsync(fixture.Session.UserId, fixture.Trip))!.Proposal!;
        await fixture.ViewModel.GenerateCommand.ExecuteAsync(null);
        var second = (await fixture.Store.ReadAsync(fixture.Session.UserId, fixture.Trip))!.Proposal!;
        Assert.NotEqual(first, second);
        Assert.True(fixture.ViewModel.CanCompare);
        var restarted = fixture.NewViewModel(); await restarted.InitializeAsync(null);
        Assert.True(restarted.CanCompare);
        await restarted.ChoosePreviousCommand.ExecuteAsync(null);
        Assert.Equal(first.OperationId, (await fixture.Store.ReadAsync(fixture.Session.UserId, fixture.Trip))!.Proposal!.OperationId);
        Assert.DoesNotContain(restarted.Days.SelectMany(x => x), x => second.Days.SelectMany(d => d.Stops).Any(s => s.Id == x.Value.Id));
        await restarted.ChoosePreviousCommand.ExecuteAsync(null);
        Assert.Equal(second.OperationId, (await fixture.Store.ReadAsync(fixture.Session.UserId, fixture.Trip))!.Proposal!.OperationId);
    }
    [Fact]
    public async Task Different_date_range_does_not_offer_comparison()
    {
        using var fixture = await Fixture.CreateAsync(); await fixture.ViewModel.InitializeAsync(Start);
        await fixture.ViewModel.GenerateCommand.ExecuteAsync(null);
        fixture.ViewModel.SelectedDate = Start.AddDays(1).ToDateTime(TimeOnly.MinValue);
        await fixture.ViewModel.GenerateCommand.ExecuteAsync(null);
        Assert.False(fixture.ViewModel.CanCompare);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Comparison_requires_a_complete_choice_before_editing_or_saving(bool choosePrevious)
    {
        using var fixture = await Fixture.CreateAsync();
        await fixture.ViewModel.InitializeAsync(Start);
        await fixture.ViewModel.GenerateCommand.ExecuteAsync(null);
        fixture.ViewModel.Days[0][0].IsSelected = false;
        var first = (await fixture.Store.ReadAsync(fixture.Session.UserId, fixture.Trip))!;
        await fixture.ViewModel.GenerateCommand.ExecuteAsync(null);
        var second = (await fixture.Store.ReadAsync(fixture.Session.UserId, fixture.Trip))!;
        var selected = fixture.ViewModel.SelectedCount;

        fixture.ViewModel.ToggleComparisonCommand.Execute(null);

        Assert.True(fixture.ViewModel.ShowComparison);
        Assert.False(fixture.ViewModel.CanApply);
        Assert.False(fixture.ViewModel.CanSelect);
        Assert.False(fixture.ViewModel.ShowApply);
        Assert.False(fixture.ViewModel.ShowOpenTrip);
        Assert.Equal(DayPlannerViewModel.Text("PlannerChooseForComparison"), fixture.ViewModel.ApplyBlockedReason);
        Assert.All(fixture.ViewModel.Days.SelectMany(day => day), stop =>
        {
            Assert.False(stop.CanSelectRow);
            Assert.False(stop.CanReplaceRow);
        });
        fixture.ViewModel.SelectDayCommand.Execute(fixture.ViewModel.Days[0]);
        await fixture.ViewModel.ReplaceStopCommand.ExecuteAsync(fixture.ViewModel.Days[0][0]);
        await fixture.ViewModel.ApplyCommand.ExecuteAsync(null);
        Assert.Equal(selected, fixture.ViewModel.SelectedCount);
        Assert.Empty(fixture.ReplacementRequests);
        Assert.Empty(fixture.ApplicationRequests);

        if (choosePrevious) await fixture.ViewModel.ChoosePreviousCommand.ExecuteAsync(null);
        else fixture.ViewModel.KeepCurrentCommand.Execute(null);

        var chosen = choosePrevious ? first : second;
        Assert.False(fixture.ViewModel.ShowComparison);
        Assert.True(fixture.ViewModel.CanApply);
        Assert.True(fixture.ViewModel.CanSelect);
        Assert.True(fixture.ViewModel.ShowApply);
        Assert.Equal(chosen.Proposal!.Days.SelectMany(day => day.Stops).Select(stop => stop.Id),
            fixture.ViewModel.Days.SelectMany(day => day).Select(stop => stop.Value.Id));
        Assert.Equal(chosen.SelectedStopIds,
            fixture.ViewModel.Days.SelectMany(day => day).Where(stop => stop.IsSelected).Select(stop => stop.Value.Id));
        await fixture.ViewModel.ApplyCommand.ExecuteAsync(null);
        Assert.Equal(chosen.SelectedStopIds, Assert.Single(fixture.ApplicationRequests).SelectedStopIds);
        Assert.True(fixture.ViewModel.ShowOpenTrip);
        fixture.ViewModel.ToggleComparisonCommand.Execute(null);
        Assert.False(fixture.ViewModel.ShowOpenTrip);
        Assert.False(fixture.ViewModel.ShowApply);
        fixture.ViewModel.KeepCurrentCommand.Execute(null);
        Assert.True(fixture.ViewModel.ShowOpenTrip);
    }

    [Fact]
    public async Task Choosing_a_compared_proposal_never_enables_saving_offline_or_without_access_options()
    {
        using var fixture = await Fixture.CreateAsync();
        await fixture.ViewModel.InitializeAsync(Start);
        await fixture.ViewModel.GenerateCommand.ExecuteAsync(null);
        await fixture.ViewModel.GenerateCommand.ExecuteAsync(null);
        Connectivity.Current.NetworkAccess = NetworkAccess.None;
        await fixture.ViewModel.RefreshAsync();
        fixture.ViewModel.ToggleComparisonCommand.Execute(null);
        await fixture.ViewModel.ChoosePreviousCommand.ExecuteAsync(null);
        Assert.False(fixture.ViewModel.CanApply);
        Assert.Equal(DayPlannerViewModel.Text("PlannerOffline"), fixture.ViewModel.ApplyBlockedReason);
        Assert.NotEqual(0, fixture.ViewModel.SelectedCount);

        Connectivity.Current.NetworkAccess = NetworkAccess.Internet;
        fixture.ViewModel.HasOptions = false;
        fixture.ViewModel.ToggleComparisonCommand.Execute(null);
        fixture.ViewModel.KeepCurrentCommand.Execute(null);
        Assert.False(fixture.ViewModel.CanApply);
        Assert.Equal(DayPlannerViewModel.Text("PlannerAccessRequired"), fixture.ViewModel.ApplyBlockedReason);
        Assert.Empty(fixture.ApplicationRequests);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Replacement_or_canonical_recovery_preserves_both_alternatives_after_restart(bool recoverCanonical)
    {
        using var fixture = await Fixture.CreateAsync();
        await fixture.ViewModel.InitializeAsync(Start);
        await fixture.ViewModel.GenerateCommand.ExecuteAsync(null);
        var first = (await fixture.Store.ReadAsync(fixture.Session.UserId, fixture.Trip))!.Proposal!;
        await fixture.ViewModel.GenerateCommand.ExecuteAsync(null);
        var second = (await fixture.Store.ReadAsync(fixture.Session.UserId, fixture.Trip))!.Proposal!;
        var row = fixture.ViewModel.Days[0][0];
        var replacement = Replacement(second, row.Value.Id);
        if (recoverCanonical)
        {
            fixture.Replace = (_, _, _) => throw new TravelCompanion.Mobile.Services.DayPlanApiException(
                System.Net.HttpStatusCode.Conflict, "stale", "Another device changed this proposal");
            fixture.Generate = (_, _, _) => Task.FromResult(replacement.Proposal);
        }
        else fixture.Replace = (_, _, _) => Task.FromResult(replacement);

        await fixture.ViewModel.ReplaceStopCommand.ExecuteAsync(row);

        var saved = (await fixture.Store.ReadAsync(fixture.Session.UserId, fixture.Trip))!;
        Assert.Null(saved.PendingReplacement);
        Assert.Equal(first.OperationId, saved.PreviousAlternative!.Proposal.OperationId);
        Assert.Equal(replacement.Proposal.Days[0].Stops[0].Id, saved.Proposal!.Days[0].Stops[0].Id);
        var restarted = fixture.NewViewModel();
        await restarted.InitializeAsync(null);
        Assert.True(restarted.CanCompare);
        await restarted.ChoosePreviousCommand.ExecuteAsync(null);
        Assert.Equal(first.Days.SelectMany(day => day.Stops).Select(stop => stop.Id),
            restarted.Days.SelectMany(day => day).Select(stop => stop.Value.Id));
        await restarted.ChoosePreviousCommand.ExecuteAsync(null);
        Assert.Equal(replacement.Proposal.Days.SelectMany(day => day.Stops).Select(stop => stop.Id),
            restarted.Days.SelectMany(day => day).Select(stop => stop.Value.Id));
    }
    [Fact]
    public async Task Offline_proposal_explains_block_without_losing_selection()
    {
        using var fixture = await Fixture.CreateAsync(); await fixture.ViewModel.InitializeAsync(Start);
        await fixture.ViewModel.GenerateCommand.ExecuteAsync(null);
        var count = fixture.ViewModel.SelectedCount;
        Connectivity.Current.NetworkAccess = NetworkAccess.None;
        await fixture.ViewModel.RefreshAsync();
        Assert.False(fixture.ViewModel.CanApply);
        Assert.False(string.IsNullOrWhiteSpace(fixture.ViewModel.ApplyBlockedReason));
        Assert.Equal(count, fixture.ViewModel.SelectedCount);
    }
}

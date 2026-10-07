using System.Globalization;
using System.Text.Json;
using CommunityToolkit.Mvvm.Input;
using TravelCompanion.Mobile.Services;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Mobile.ViewModels;

public sealed partial class DayPlannerViewModel
{
    private DayPlanReplaceRequest? pendingReplacement;
    private DayPlanRequest? proposalRequest;
    private readonly HashSet<Guid> seenRecommendations = [];

    private void RememberRecommendations(DayPlanResponse value) => seenRecommendations.UnionWith(
        value.Days.SelectMany(day => day.Stops).Select(stop => stop.RecommendationId).Where(id => id != Guid.Empty));

    private bool CanReplace(PlannerStopRow row) => !ShowComparison && current && IsNotBusy && HasOptions && options?.Enabled == true
        && Online && (!Stale || pendingReplacement?.StopId == row.Value.Id) && pendingApplication is null && !row.IsSaved && !row.IsReplacing
        && Days.Any(day => day.Contains(row))
        && (pendingReplacement is null || pendingReplacement.StopId == row.Value.Id);

    [RelayCommand]
    private Task ReplaceStopAsync(PlannerStopRow? row)
    {
        if (row is null || !CanReplace(row)) return Task.CompletedTask;
        return RunAsync(async ct =>
        {
            if (proposal is null || trip is null || !current) return;
            row.IsReplacing = true;
            row.ReplacementNotice = "";
            try
            {
                pendingReplacement ??= new(proposal.OperationId, trip.Value, proposalRevision, row.Value.Id,
                    Guid.NewGuid(), CultureInfo.CurrentUICulture.Name)
                { ExpectedProposalRevision = proposal.ProposalRevision, OriginalRequest = proposalRequest };
                NotifyActions();
                await PersistAsync(ct); // Persist the exact mutation before sending; a lost response can be replayed.
                if (!current) return;
                var token = await sessions.GetTokenAsync();
                if (token is null || !current) return;
                DayPlanReplaceResponse result;
                try { result = await client.ReplaceAsync(token, pendingReplacement, ct); }
                catch (DayPlanApiException error) when (error.Code == "stale" && proposalRequest is not null)
                {
                    if (current) await RecoverCanonicalProposalAsync(token, row, ct);
                    return;
                }
                catch (DayPlanApiException error) when (error.Code is "operation" or "selection" or "stale")
                {
                    if (!current) return;
                    // These structured rejections mean the mutation did not run. Keep the idea, release the pending state durably.
                    var unchangedSelection = Days.SelectMany(day => day).Where(stop => stop.IsSelected && !stop.IsSaved)
                        .Select(stop => stop.Value.Id).ToArray();
                    await PersistReplacementResultAsync(proposal, unchangedSelection, ct);
                    if (!current) return;
                    pendingReplacement = null;
                    request = null;
                    if (error.Code == "stale") Stale = true;
                    row.ReplacementNotice = error.Message;
                    ErrorMessage = error.Message;
                    NotifySelection();
                    return;
                }
                if (!current) return;
                ValidateReplacement(result, row);
                var selected = Days.SelectMany(day => day).Where(stop => stop.IsSelected && !stop.IsSaved)
                    .Select(stop => stop.Value.Id).ToHashSet();
                var newStop = result.Proposal.Days.SelectMany(day => day.Stops).FirstOrDefault(stop => stop.Id != row.Value.Id
                    && !proposal.Days.SelectMany(day => day.Stops).Any(old => old.Id == stop.Id));
                if (result.Replaced && newStop is not null && selected.Remove(row.Value.Id)) selected.Add(newStop.Id);
                await PersistReplacementResultAsync(result.Proposal, selected.ToArray(), ct);
                if (!current) return;
                proposal = result.Proposal;
                RememberRecommendations(proposal);
                pendingReplacement = null;
                if (result.Replaced && newStop is not null)
                {
                    var group = Days.First(day => day.Contains(row));
                    group[group.IndexOf(row)] = new(newStop, row.IsSelected, SelectionChanged, group.Cities);
                }
                else row.ReplacementNotice = Text("PlannerNoAlternative");
                StatusMessage = result.Replaced ? result.Message : Text("PlannerNoAlternative");
                OnPropertyChanged(nameof(ResultIntro));
                NotifySelection();
            }
            finally
            {
                row.IsReplacing = false;
                if (current && pendingReplacement?.StopId == row.Value.Id)
                    row.ReplacementNotice = Text("PlannerReplacementPending");
            }
        }, "PlannerReplacingIdea");
    }

    private void ValidateReplacement(DayPlanReplaceResponse result, PlannerStopRow row)
    {
        if (proposal is null || result.Proposal.OperationId != proposal.OperationId || result.Proposal.TripId != trip
            || result.Proposal.BasedOnRevision != proposal.BasedOnRevision
            || result.Proposal.ProposalRevision != proposal.ProposalRevision + (result.Replaced ? 1 : 0)
            || result.Proposal.Days.Count != proposal.Days.Count)
            throw new JsonException("The replacement does not match this proposal.");
        var after = result.Proposal.Days.SelectMany(day => day.Stops).ToArray();
        if (after.Select(stop => stop.Id).Distinct().Count() != after.Length
            || after.Select(stop => stop.RecommendationId).Distinct().Count() != after.Length)
            throw new JsonException("The replacement contains duplicate ideas.");
        for (var dayIndex = 0; dayIndex < proposal.Days.Count; dayIndex++)
        {
            var oldDay = proposal.Days[dayIndex];
            var newDay = result.Proposal.Days[dayIndex];
            if (newDay.Date != oldDay.Date || newDay.Stops.Count != oldDay.Stops.Count)
                throw new JsonException("The replacement changes the selected days.");
            for (var index = 0; index < oldDay.Stops.Count; index++)
            {
                var before = oldDay.Stops[index];
                var stop = newDay.Stops[index];
                if (before.Id == row.Value.Id && result.Replaced)
                {
                    if (stop.Id == Guid.Empty || stop.Id == before.Id || stop.PeriodKey != before.PeriodKey
                        || stop.RecommendationId == Guid.Empty || seenRecommendations.Contains(stop.RecommendationId))
                        throw new JsonException("The replacement repeats an earlier idea.");
                }
                else if (stop.Id != before.Id || stop.RecommendationId != before.RecommendationId || stop.PeriodKey != before.PeriodKey)
                    throw new JsonException("The replacement changes another idea.");
            }
        }
    }

    private Task PersistReplacementResultAsync(DayPlanResponse value, IReadOnlyList<Guid> selected, CancellationToken ct)
    {
        if (!current || user is null || trip is null) throw new OperationCanceledException();
        var history = seenRecommendations.Concat(value.Days.SelectMany(day => day.Stops).Select(stop => stop.RecommendationId))
            .Where(id => id != Guid.Empty).Distinct().ToArray();
        // Save the canonical receipt before committing the visible change. Failed local writes leave the old row retryable.
        return store.SaveAsync(user.Value, trip.Value, new(options, request, value, selected, pendingApplication,
            Days.SelectMany(day => day).Where(stop => stop.IsSaved).Select(stop => stop.Value.Id).ToArray(), proposalRevision,
            DateOnly.FromDateTime(SelectedDate), dayCount, CurrentPreferences(), null, history, proposalRequest, previousAlternative), ct);
    }

    private async Task RecoverCanonicalProposalAsync(string token, PlannerStopRow row, CancellationToken ct)
    {
        var canonical = await client.GenerateAsync(token, proposalRequest!, ct);
        if (!current) return;
        if (canonical.OperationId != proposal?.OperationId || canonical.TripId != trip)
            throw new JsonException("The recovered proposal belongs to another operation.");
        var fresh = await client.OptionsAsync(token, ct);
        if (!current) return;
        if (fresh.TripId != trip) throw new OperationCanceledException();
        var previousIds = Days.SelectMany(day => day).Select(stop => stop.Value.Id).ToHashSet();
        var ids = canonical.Days.SelectMany(day => day.Stops).Select(stop => stop.Id).ToHashSet();
        var selected = Days.SelectMany(day => day).Where(stop => stop.IsSelected && !stop.IsSaved && ids.Contains(stop.Value.Id))
            .Select(stop => stop.Value.Id).ToArray();
        await PersistReplacementResultAsync(canonical, selected, ct);
        if (!current) return;
        var saved = Days.SelectMany(day => day).Where(stop => stop.IsSaved).Select(stop => stop.Value.Id).ToHashSet();
        var itineraryRevision = proposalRevision;
        ApplyProposal(canonical, selected);
        proposalRevision = itineraryRevision;
        foreach (var stop in Days.SelectMany(day => day)) stop.IsSaved = saved.Contains(stop.Value.Id);
        pendingReplacement = null;
        ApplyOptions(fresh, DateOnly.FromDateTime(SelectedDate), useProfile: false);
        Stale = fresh.Revision != itineraryRevision;
        StatusMessage = Text("PlannerReplacementChanged");
        // The old row can have left the collection. Keep a notice at the changed slot as well as in the header.
        foreach (var group in Days)
            foreach (var stop in group.Where(stop => !previousIds.Contains(stop.Value.Id)))
                stop.ReplacementNotice = Text("PlannerReplacementChanged");
        NotifySelection();
    }
}

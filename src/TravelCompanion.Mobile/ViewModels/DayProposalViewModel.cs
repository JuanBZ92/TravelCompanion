using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using TravelCompanion.Mobile.Services;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Mobile.ViewModels;

public sealed partial class DayProposalViewModel(
    TravelCompanionApiClient api,
    AuthSessionService sessions,
    OfflineCacheService offlineCache,
    OfflineSyncCoordinator sync,
    ProductAnalyticsTracker analytics) : ViewModelBase
{
    private DayProposalDto? _proposal;
    private Guid? _trackedProposalId;
    private Guid? _operationId;
    private bool _canApply;
    private bool _canUndo;
    public ObservableCollection<ItineraryChangeDto> Changes { get; } = [];
    public ObservableCollection<ScheduleItemDto> CurrentItems { get; } = [];
    public ObservableCollection<string> Warnings { get; } = [];
    public bool CanApply { get => _canApply; private set => SetProperty(ref _canApply, value); }
    public bool CanUndo { get => _canUndo; private set => SetProperty(ref _canUndo, value); }
    public string Title => _proposal is null ? Resource("ProposalTitle")
        : string.Format(Resource("ProposalTitleFormat"), _proposal.Date);
    public string Narrative => _proposal?.Narrative ?? string.Empty;
    public void SetProposal(DayProposalDto value)
    {
        _proposal = value; Changes.Clear(); Warnings.Clear();
        foreach (var change in value.Changes) Changes.Add(change);
        foreach (var warning in value.Warnings) Warnings.Add(warning);
        CanApply = value.CanApply && value.Changes.Count > 0; OnPropertyChanged(nameof(Title)); OnPropertyChanged(nameof(Narrative));
        _ = CacheProposalAsync(value);
        if (_trackedProposalId != value.Id)
        {
            _trackedProposalId = value.Id;
            _ = analytics.TrackAsync("proposal_previewed", value.Goal.ToString(), tripId: sessions.CurrentTripId);
        }
        _ = LoadCurrentItemsAsync(value);
    }
    [RelayCommand] private Task ApplyAsync() => LoadAsync(async ct =>
    {
        if (_proposal is null) return;
        var token = await sessions.GetTokenAsync() ?? throw new InvalidOperationException(Resource("CommerceSessionExpired"));
        var result = await api.ApplyDayProposalAsync(token, _proposal.Id,
            new(_proposal.Version, _proposal.BasedOnRevision, Guid.NewGuid().ToString("N")), ct)
            ?? throw new InvalidOperationException(Resource("ProposalTripChanged"));
        _operationId = result.OperationId; CanApply = false; CanUndo = true;
        await sync.SynchronizeVersionsAsync(token, force: true, ct);
        StatusMessage = Resource("ProposalApplied");
    });
    [RelayCommand] private Task ExcludeAsync(ItineraryChangeDto change) => ReviseAsync([change.ChangeId], [], null);
    [RelayCommand] private Task ReplaceAsync(ItineraryChangeDto change) => ReviseAsync([], [], change.ChangeId);
    [RelayCommand] private Task MoveEarlierAsync(ItineraryChangeDto change)
    {
        var ordered = Changes.Select(item => item.ChangeId).ToList();
        var index = ordered.IndexOf(change.ChangeId);
        if (index <= 0) return Task.CompletedTask;
        (ordered[index - 1], ordered[index]) = (ordered[index], ordered[index - 1]);
        return ReviseAsync([], ordered, null);
    }

    private Task ReviseAsync(IReadOnlyList<Guid> excluded, IReadOnlyList<Guid> ordered, Guid? replacement) => LoadAsync(async ct =>
    {
        if (_proposal is null) return;
        var token = await sessions.GetTokenAsync() ?? throw new InvalidOperationException(Resource("CommerceSessionExpired"));
        var revised = await api.ReviseDayProposalAsync(token, _proposal.Id,
            new(_proposal.Version, excluded, ordered, replacement), ct)
            ?? throw new InvalidOperationException(Resource("ProposalChanged"));
        SetProposal(revised);
    });
    [RelayCommand] private Task UndoAsync() => LoadAsync(async ct =>
    {
        if (_operationId is not { } id) return;
        var token = await sessions.GetTokenAsync() ?? throw new InvalidOperationException(Resource("CommerceSessionExpired"));
        _ = await api.UndoDayProposalAsync(token, id, ct) ?? throw new InvalidOperationException(Resource("ProposalUndoUnavailable"));
        await sync.SynchronizeVersionsAsync(token, force: true, ct);
        CanUndo = false; StatusMessage = Resource("ProposalRestored");
    });
    private async Task CacheProposalAsync(DayProposalDto proposal)
    {
        try { await offlineCache.SaveAsync(ProposalCacheKey, proposal); }
        catch { /* The online preview remains usable if encrypted local storage is unavailable. */ }
    }
    private async Task LoadCurrentItemsAsync(DayProposalDto proposal)
    {
        try
        {
            var token = await sessions.GetTokenAsync();
            var schedule = string.IsNullOrWhiteSpace(token) ? null : await api.GetScheduleAsync(token);
            if (_proposal?.Id != proposal.Id || schedule is null) return;
            CurrentItems.Clear();
            foreach (var item in schedule.Items.Where(item => item.Date <= proposal.Date
                         && (item.EndsOn ?? item.Date) >= proposal.Date).OrderBy(item => item.StartsAt))
                CurrentItems.Add(item);
        }
        catch { /* A cached proposal remains readable without the current online agenda. */ }
    }
    private string ProposalCacheKey => $"last-day-proposal-trip-{sessions.CurrentTripId?.ToString() ?? "none"}-user-{sessions.CurrentUserId?.ToString() ?? "none"}";
    private static string Resource(string key) => LocalizationResourceManager.Instance.GetString(key);
}

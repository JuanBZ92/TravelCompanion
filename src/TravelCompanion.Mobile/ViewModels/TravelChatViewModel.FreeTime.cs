using System.Globalization;
using CommunityToolkit.Mvvm.Input;
using TravelCompanion.Mobile.Services;
using TravelCompanion.Shared;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Mobile.ViewModels;

public sealed partial class TravelChatViewModel
{
    private TripScheduleDto? _assistantSchedule;
    private bool _isFreeTimeSearch;
    private TimeSpan _freeTimeStart = TimeSpan.FromHours(9);
    private int _freeTimeDurationIndex = 1;
    private TravelTimeWindow? _freeTimeWindow;
    private bool _openingFreeTime;
    private static readonly int[] FreeTimeMinutes = [30, 60, 90, 120];

    public bool IsFreeTimeSearch => _isFreeTimeSearch;
    public string AssistantFreeTime => Resource("AssistantFreeTime");
    public string AssistantFreeTimeHelp => Resource("AssistantFreeTimeHelp");
    public string AssistantSearchTitle => IsFreeTimeSearch ? AssistantFreeTime : AssistantFindPlace;
    public string AssistantSearchHelp => IsFreeTimeSearch ? AssistantFreeTimeHelp : AssistantSearchIntro;
    public string FreeTimeStartLabel => Resource("AssistantFreeTimeStart");
    public string FreeTimeDurationLabel => Resource("AssistantFreeTimeDuration");
    public string FreeTimeEstimate => Resource("AssistantFreeTimeEstimate");
    public IReadOnlyList<string> FreeTimeDurations => FreeTimeMinutes.Select(minutes =>
        string.Format(CultureInfo.CurrentCulture, Resource("AssistantFreeTimeMinutes"), minutes)).ToArray();
    public bool CanSubmitQuickSearch => IsNotBusy && (!IsFreeTimeSearch || _freeTimeWindow is not null);
    public string FreeTimeWindowSummary => _freeTimeWindow is null ? Resource("AssistantFreeTimeNoWindow")
        : string.Format(CultureInfo.CurrentCulture, Resource("AssistantFreeTimeWindow"),
            _freeTimeWindow.StartsAtLocal, _freeTimeWindow.EndsAtLocal, _assistantSchedule?.TimeZoneId ?? "");
    public string FreeTimeNextFixedNote => _freeTimeWindow?.NextFixedAtLocal is { } next
        && next <= DateOnly.FromDateTime(PlanningDate).ToDateTime(TimeOnly.MinValue)
            .Add(_freeTimeStart).AddMinutes(FreeTimeMinutes[FreeTimeDurationIndex])
        ? string.Format(CultureInfo.CurrentCulture, Resource("AssistantFreeTimeNextFixed"), next) : "";
    public bool HasFreeTimeNextFixedNote => FreeTimeNextFixedNote.Length > 0;

    public TimeSpan FreeTimeStart
    {
        get => _freeTimeStart;
        set { if (SetProperty(ref _freeTimeStart, value)) { _pendingRetryRequest = null; RefreshFreeTimeWindow(); } }
    }
    public int FreeTimeDurationIndex
    {
        get => _freeTimeDurationIndex;
        set
        {
            value = Math.Clamp(value, 0, FreeTimeMinutes.Length - 1);
            if (SetProperty(ref _freeTimeDurationIndex, value)) { _pendingRetryRequest = null; RefreshFreeTimeWindow(); }
        }
    }

    [RelayCommand]
    private Task OpenFreeTimeAsync() => OpenFreeTimeForDateAsync(null);

    public async Task OpenFreeTimeForDateAsync(DateOnly? requestedDate)
    {
        if (_openingFreeTime) return;
        _openingFreeTime = true;
        try { await OpenFreeTimeCoreAsync(requestedDate); }
        finally { _openingFreeTime = false; }
    }

    private async Task OpenFreeTimeCoreAsync(DateOnly? requestedDate)
    {
        if (IsBusy || !sessionService.HasSession) return;
        var context = sessionService.ContextVersion;
        var pageVersion = _assistantPageOperationVersion;
        var surfaceVersion = _assistantSurfaceVersion;
        var selectedDate = DateOnly.FromDateTime(PlanningDate);
        var cached = await bootstrapStore.GetCachedAsync();
        if (context != sessionService.ContextVersion || pageVersion != _assistantPageOperationVersion
            || surfaceVersion != _assistantSurfaceVersion
            || !sessionService.HasSession || IsBusy || selectedDate != DateOnly.FromDateTime(PlanningDate)) return;
        var schedule = cached?.Value.Schedule;
        if (schedule is null || schedule.TripId != sessionService.CurrentTripId)
        {
            ErrorMessage = Resource("AssistantProposalNeedsRefresh");
            return;
        }
        var date = requestedDate ?? DateOnly.FromDateTime(PlanningDate);
        if (date < schedule.StartsOn || date > schedule.EndsOn)
        {
            ErrorMessage = Resource("AssistantFreeTimeNoWindow");
            return;
        }
        if (date > DateOnly.FromDateTime(AssistantMaximumDate))
        {
            await PaywallNavigation.OpenAsync(PaywallEntryPoint.Assistant, limitReached: true);
            return;
        }
        _assistantDateSelectedByTraveler = true;
        PlanningDate = date.ToDateTime(TimeOnly.MinValue);
        _assistantSchedule = schedule;
        await UpdateCityForDateAsync(PlanningDate);
        if (context != sessionService.ContextVersion || pageVersion != _assistantPageOperationVersion
            || surfaceVersion != _assistantSurfaceVersion
            || !sessionService.HasSession || IsBusy || date != DateOnly.FromDateTime(PlanningDate)) return;
        OpenQuickSearch();
        _isFreeTimeSearch = true;
        _pendingRetryRequest = null;
        var now = UpcomingActivitySelector.GetTripNow(schedule.TimeZoneId, DateTimeOffset.UtcNow);
        FreeTimeStart = DateOnly.FromDateTime(now) == date
            ? new TimeSpan(now.Hour, now.Minute, 0) : TimeSpan.FromHours(9);
        RefreshFreeTimeWindow();
        NotifyFreeTimeLabels();
    }

    private void RefreshFreeTimeWindow()
    {
        _freeTimeWindow = _assistantSchedule is null ? null : AssistantFreeTimeWindow.Resolve(
            _assistantSchedule, DateOnly.FromDateTime(PlanningDate), _freeTimeStart,
            FreeTimeMinutes[_freeTimeDurationIndex], DateTimeOffset.UtcNow);
        OnPropertyChanged(nameof(FreeTimeWindowSummary));
        OnPropertyChanged(nameof(FreeTimeNextFixedNote));
        OnPropertyChanged(nameof(HasFreeTimeNextFixedNote));
        OnPropertyChanged(nameof(CanSubmitQuickSearch));
        OnPropertyChanged(nameof(AssistantSearchAction));
    }

    private void NotifyFreeTimeLabels()
    {
        foreach (var name in new[] { nameof(IsFreeTimeSearch), nameof(AssistantFreeTime), nameof(AssistantFreeTimeHelp),
                     nameof(AssistantSearchTitle), nameof(AssistantSearchHelp), nameof(FreeTimeStartLabel),
                     nameof(FreeTimeDurationLabel), nameof(FreeTimeEstimate), nameof(FreeTimeDurations),
                     nameof(AssistantSearchAction) }) OnPropertyChanged(name);
    }
}

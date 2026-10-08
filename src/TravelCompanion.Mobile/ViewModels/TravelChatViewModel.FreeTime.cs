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
    private int _freeTimeDurationIndex = 1;
    private TravelTimeWindow? _freeTimeWindow;
    private bool _openingFreeTime;
    private bool _freeTimeAreaStep;
    private bool _freeTimeSurpriseSelected;
    private string? _freeTimeSurpriseCategory;
    private string? _freeTimeArea;
    private GeoPointDto? _freeTimeLocation;
    private ScheduleItemDto? _freeTimeNextPlan;
    private bool _freeTimeLocationUnavailable;
    private bool _freeTimeNoOptions;
    private Guid? _freeTimeNearPlanId;
    private bool _freeTimeNeedsRestart;
    private readonly HashSet<Guid> _freeTimeProposedIds = [];
    private readonly HashSet<string> _freeTimeProposedPlaces = new(StringComparer.Ordinal);
    private Guid? _freeTimeRetryReplacementId;
    private static readonly int[] FreeTimeMinutes = [30, 60, 90, 120];
    internal Func<DateTimeOffset> FreeTimeClock { get; set; } = () => DateTimeOffset.UtcNow;

    public bool IsFreeTimeSearch => _isFreeTimeSearch;
    public bool IsStandardQuickSearch => !IsFreeTimeSearch;
    public bool ShowFreeTimeInterests => IsFreeTimeSearch && !_freeTimeAreaStep;
    public bool ShowFreeTimeArea => IsFreeTimeSearch && _freeTimeAreaStep;
    public bool ShowQuickInterests => !IsFreeTimeSearch || ShowFreeTimeInterests;
    public bool HasFreeTimeNextPlan => _freeTimeNextPlan is not null;
    public bool CanUseFreeTimeCityFallback => ShowFreeTimeArea && IsNotBusy && sessionService.CanUseAssistant
        && _freeTimeArea != "city" && (_freeTimeLocationUnavailable || _freeTimeNoOptions)
        && !string.IsNullOrWhiteSpace(FreeTimeFallbackCity);
    public bool FreeTimeCurrentAreaSelected => _freeTimeArea == "current";
    public bool FreeTimeNextAreaSelected => _freeTimeArea == "next";
    public bool ShowFreeTimeCancel => IsFreeTimeSearch && IsBusy;
    public bool FreeTimeNeedsRestart => IsFreeTimeSearch && _freeTimeNeedsRestart;
    public string FreeTimeRestart => Resource("ExpressRestartToday");
    public string FreeTimeEditSearch => Resource("ExpressEditSearch");
    public string FreeTimeCancel => Resource("AssistantCancel");
    public string AssistantFreeTime => Resource("AssistantFreeTime");
    public string AssistantFreeTimeHelp => Resource("AssistantFreeTimeHelp");
    public string AssistantSearchExpressTitle => Resource("ExpressShortcutTitle");
    public string AssistantSearchTitle => IsFreeTimeSearch ? Resource("ExpressShortcutTitle") : AssistantFindPlace;
    public string AssistantSearchHelp => IsFreeTimeSearch
        ? Resource(_freeTimeAreaStep ? "ExpressAreaHelp" : "ExpressInterestHelp") : AssistantSearchIntro;
    public string AssistantProposalTitle => IsFreeTimeSearch ? Resource("ExpressResultsTitle") : AssistantYourDay;
    public string FreeTimeInterestQuestion => Resource("ExpressInterestQuestion");
    public string FreeTimeSurprise => Resource("ExpressSurprise");
    public string FreeTimeSurpriseHelp => Resource(
        (_freeTimeSurpriseSelected ? _freeTimeSurpriseCategory is not null : !HasSavedFreeTimeInterests)
            ? "ExpressSurpriseRandomHelp" : "ExpressSurpriseHelp");
    public bool FreeTimeSurpriseSelected => IsFreeTimeSearch && _freeTimeSurpriseSelected;
    private bool HasSavedFreeTimeInterests => _cachedPreferenceProfile is { } profile
        && _loadedPreferenceUserId == sessionService.CurrentUserId && profile.UserId == sessionService.CurrentUserId
        && profile.Interests is { Count: > 0 } interests && interests.Any(interest => !string.IsNullOrWhiteSpace(interest));
    public string FreeTimeAreaQuestion => Resource("ExpressAreaQuestion");
    public string FreeTimeCurrentArea => Resource("ExpressCurrentArea");
    public string FreeTimeCurrentAreaHelp => Resource("ExpressCurrentAreaHelp");
    public string FreeTimeNextArea => Resource("ExpressNextArea");
    public string FreeTimeNextAreaHelp => _freeTimeNextPlan is { } plan && _assistantSchedule is { } schedule
        ? $"{AssistantFreeTimeWindow.TripStart(schedule, plan):HH:mm} · {plan.Title}" : "";
    public string FreeTimeCityFallback => string.Format(CultureInfo.CurrentCulture,
        Resource("ExpressCityFallback"), FreeTimeFallbackCity ?? Resource("AssistantDestinationFallback"));
    private string? FreeTimeFallbackCity => _freeTimeArea == "next" && !string.IsNullOrWhiteSpace(_freeTimeNextPlan?.City)
        ? _freeTimeNextPlan.City : City;
    public string FreeTimeAreaSummary => _freeTimeArea switch
    {
        "current" => Resource("ExpressCurrentArea"),
        "next" => $"{Resource("ExpressNextArea")} · {_freeTimeNextPlan?.Title}",
        "city" => FreeTimeCityFallback,
        _ => ""
    };
    public string FreeTimeDurationLabel => Resource("AssistantFreeTimeDuration");
    public string FreeTimeEstimate => Resource("AssistantFreeTimeEstimate");
    public IReadOnlyList<string> FreeTimeDurations => FreeTimeMinutes.Select(minutes =>
        string.Format(CultureInfo.CurrentCulture, Resource("AssistantFreeTimeMinutes"), minutes)).ToArray();
    public bool CanSubmitQuickSearch => IsNotBusy && (!IsFreeTimeSearch || !_freeTimeNeedsRestart && sessionService.CanUseAssistant
        && (!_freeTimeAreaStep ? _quickCategories.Count > 0 || _freeTimeSurpriseSelected
            : _freeTimeArea is not null && _freeTimeWindow is not null));
    public string FreeTimeWindowSummary => _freeTimeWindow is null ? Resource("AssistantFreeTimeNoWindow")
        : string.Format(CultureInfo.CurrentCulture, Resource("AssistantFreeTimeWindow"),
            _freeTimeWindow.StartsAtLocal, _freeTimeWindow.EndsAtLocal, _assistantSchedule?.TimeZoneId ?? "");
    public string FreeTimeSearchLimits => _freeTimeWindow is { } window && _freeTimeArea is not null
        ? string.Format(CultureInfo.CurrentCulture,
            Resource(_freeTimeArea == "city" ? "ExpressSearchLimitsCity" : "ExpressSearchLimitsNear"),
            window.AvailableMinutes) : "";
    public bool HasFreeTimeSearchLimits => FreeTimeSearchLimits.Length > 0;
    public string FreeTimeNextFixedNote => _freeTimeWindow?.NextFixedAtLocal is { } next
        ? string.Format(CultureInfo.CurrentCulture, Resource("AssistantFreeTimeNextFixed"), next) : "";
    public bool HasFreeTimeNextFixedNote => FreeTimeNextFixedNote.Length > 0;
    public string FreeTimeSoonNote => _freeTimeWindow is { } window && _assistantSchedule is { } schedule
        && window.StartsAtLocal > UpcomingActivitySelector.GetTripNow(schedule.TimeZoneId, FreeTimeClock()).AddMinutes(2)
        ? Resource("ExpressAfterCurrent") : "";
    public bool HasFreeTimeSoonNote => IsFreeTimeSearch && FreeTimeSoonNote.Length > 0;

    public int FreeTimeDurationIndex
    {
        get => _freeTimeDurationIndex;
        set
        {
            value = Math.Clamp(value, 0, FreeTimeMinutes.Length - 1);
            if (SetProperty(ref _freeTimeDurationIndex, value))
            { _pendingRetryRequest = null; RefreshFreeTimeWindow(); }
        }
    }

    [RelayCommand]
    private Task OpenFreeTimeAsync() => OpenFreeTimeForDateAsync(null);

    public async Task OpenFreeTimeForDateAsync(DateOnly? requestedDate)
    {
        // The existing route remains compatible; express always means now in the trip's zone.
        if (_openingFreeTime) return;
        _openingFreeTime = true;
        try { await OpenFreeTimeCoreAsync(); }
        finally { _openingFreeTime = false; }
    }

    private async Task OpenFreeTimeCoreAsync()
    {
        if (IsBusy || !sessionService.HasSession) return;
        if (!sessionService.CanUseAssistant) { ErrorMessage = Resource("ExpressAccessUnavailable"); return; }
        var context = sessionService.ContextVersion;
        var pageVersion = _assistantPageOperationVersion;
        var surfaceVersion = _assistantSurfaceVersion;
        var selectedDate = DateOnly.FromDateTime(PlanningDate);
        var cached = await bootstrapStore.GetCachedAsync();
        if (context != sessionService.ContextVersion || pageVersion != _assistantPageOperationVersion
            || surfaceVersion != _assistantSurfaceVersion || !sessionService.CanUseAssistant
            || IsBusy || selectedDate != DateOnly.FromDateTime(PlanningDate)) return;
        var schedule = cached?.Value.Schedule;
        if (schedule is null || schedule.TripId != sessionService.CurrentTripId)
        { ErrorMessage = Resource("AssistantProposalNeedsRefresh"); return; }
        var date = DateOnly.FromDateTime(UpcomingActivitySelector.GetTripNow(schedule.TimeZoneId, FreeTimeClock()));
        if (date < schedule.StartsOn || date > schedule.EndsOn)
        { ErrorMessage = Resource("ExpressOutsideTrip"); return; }
        if (date > DateOnly.FromDateTime(AssistantMaximumDate))
        { await PaywallNavigation.OpenAsync(PaywallEntryPoint.Assistant, limitReached: true); return; }
        _assistantDateSelectedByTraveler = true;
        PlanningDate = date.ToDateTime(TimeOnly.MinValue);
        _assistantSchedule = schedule;
        await UpdateCityForDateAsync(PlanningDate);
        if (context != sessionService.ContextVersion || pageVersion != _assistantPageOperationVersion
            || surfaceVersion != _assistantSurfaceVersion || !sessionService.CanUseAssistant
            || IsBusy || date != DateOnly.FromDateTime(PlanningDate)) return;
        OpenQuickSearch();
        ResetFreeTimeState();
        _isFreeTimeSearch = true;
        _quickCategories.Clear();
        RefreshQuickSelections();
        RefreshFreeTimeWindow();
        NotifyFreeTimeLabels();
    }

    private void ResetFreeTimeState()
    {
        _freeTimeAreaStep = false;
        _freeTimeSurpriseSelected = false;
        _freeTimeSurpriseCategory = null;
        _freeTimeArea = null;
        _freeTimeLocation = null;
        _freeTimeNearPlanId = null;
        _freeTimeNextPlan = null;
        _freeTimeLocationUnavailable = false;
        _freeTimeNoOptions = false;
        _freeTimeNeedsRestart = false;
        _freeTimeProposedIds.Clear();
        _freeTimeProposedPlaces.Clear();
        _freeTimeRetryReplacementId = null;
        _pendingRetryRequest = null;
        _freeTimeDurationIndex = 1;
    }

    [RelayCommand]
    private void SurpriseFreeTime()
    {
        if (IsBusy || !ShowFreeTimeInterests) return;
        var categories = AvailableFreeTimeCategories();
        if (categories.Length == 0) return;
        _freeTimeSurpriseSelected = true;
        _freeTimeSurpriseCategory = HasSavedFreeTimeInterests ? null : categories[Random.Shared.Next(categories.Length)];
        _quickCategories.Clear();
        if (_freeTimeSurpriseCategory is { } category) _quickCategories.Add(category);
        RefreshQuickSelections();
        ContinueFreeTime();
    }

    private string[] AvailableFreeTimeCategories() => QuickCategories
        .Select(option => option.Id["category.".Length..]).Where(GuidedTravelCategories.IsValid)
        .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

    private void ContinueFreeTime()
    {
        _freeTimeAreaStep = true;
        _pendingRetryRequest = null;
        ErrorMessage = null;
        StatusMessage = null;
        RefreshFreeTimeWindow();
        NotifyFreeTimeLabels();
    }

    private bool TryFreeTimeBack()
    {
        if (!ShowFreeTimeArea || IsBusy) return false;
        _freeTimeAreaStep = false;
        ErrorMessage = null;
        StatusMessage = null;
        NotifyFreeTimeLabels();
        return true;
    }

    [RelayCommand]
    private async Task ChooseFreeTimeCurrentAreaAsync()
    {
        if (IsBusy || !ShowFreeTimeArea || !sessionService.CanUseAssistant) return;
        var scope = BeginAssistantOperation();
        try
        {
            _freeTimeArea = null;
            _freeTimeLocation = null;
            _freeTimeNearPlanId = null;
            ErrorMessage = null;
            StatusMessage = Resource("ExpressLocating");
            var location = await scope.AwaitAsync(ct => locationService.GetCurrentLocationAsync(ct));
            VerifyFreeTimeAccess(scope);
            if (location is null || location.Latitude is < -90 or > 90 || location.Longitude is < -180 or > 180)
            {
                _freeTimeArea = null;
                _freeTimeLocation = null;
                _freeTimeLocationUnavailable = true;
                ErrorMessage = Resource("ExpressLocationUnavailable");
                return;
            }
            _freeTimeArea = "current";
            _freeTimeLocation = location;
            _freeTimeNearPlanId = null;
            _freeTimeLocationUnavailable = false;
            _pendingRetryRequest = null;
        }
        catch (OperationCanceledException) when (!IsCurrentFreeTimeScope(scope)) { }
        catch (UnauthorizedAccessException) { if (scope.CanPublish) ErrorMessage = Resource("ExpressAccessUnavailable"); }
        catch (Exception) { if (scope.CanPublish) { _freeTimeLocationUnavailable = true; ErrorMessage = Resource("ExpressLocationUnavailable"); } }
        finally { FinishFreeTimeOperation(scope); }
    }

    [RelayCommand]
    private void ChooseFreeTimeNextArea()
    {
        if (IsBusy || !ShowFreeTimeArea || !sessionService.CanUseAssistant) return;
        RefreshFreeTimeWindow();
        if (_freeTimeNextPlan is null) return;
        _freeTimeArea = "next";
        _freeTimeNearPlanId = _freeTimeNextPlan.Id;
        _freeTimeLocation = null;
        _pendingRetryRequest = null;
        ErrorMessage = null;
        NotifyFreeTimeLabels();
    }

    [RelayCommand]
    private void ChooseFreeTimeCityArea()
    {
        if (!CanUseFreeTimeCityFallback) return;
        City = FreeTimeFallbackCity;
        _freeTimeArea = "city";
        _freeTimeLocation = null;
        _freeTimeNearPlanId = null;
        _pendingRetryRequest = null;
        ErrorMessage = null;
        NotifyFreeTimeLabels();
    }

    private Task SubmitFreeTimeAsync()
    {
        if (IsBusy || !IsFreeTimeSearch) return Task.CompletedTask;
        if (!_freeTimeAreaStep)
        {
            if (_quickCategories.Count > 0 || _freeTimeSurpriseSelected) ContinueFreeTime();
            return Task.CompletedTask;
        }
        return RequestFreeTimeOptionsAsync(null);
    }

    [RelayCommand]
    private void CancelFreeTime()
    {
        if (!IsFreeTimeSearch) return;
        CancelActiveOperations();
        StatusMessage = Resource("AssistantRequestCancelled");
        NotifyFreeTimeLabels();
    }

    [RelayCommand]
    private void EditFreeTimeSearch()
    {
        if (!IsFreeTimeSearch) return;
        if (IsBusy) CancelActiveOperations();
        _freeTimeAreaStep = false;
        _pendingRetryRequest = null;
        ErrorMessage = null;
        StatusMessage = null;
        SelectedDetailCard = null;
        RestoreAssistantSearch();
        NotifyFreeTimeLabels();
    }

    private void RefreshFreeTimeWindow()
    {
        _freeTimeWindow = _assistantSchedule is null ? null : AssistantFreeTimeWindow.ResolveSoon(
            _assistantSchedule, FreeTimeMinutes[_freeTimeDurationIndex], FreeTimeClock());
        _freeTimeNextPlan = _assistantSchedule is null ? null
            : AssistantFreeTimeWindow.NextLocatedPlan(_assistantSchedule, FreeTimeClock());
        foreach (var name in new[] { nameof(FreeTimeWindowSummary), nameof(FreeTimeSearchLimits),
                     nameof(HasFreeTimeSearchLimits), nameof(FreeTimeNextFixedNote),
                     nameof(HasFreeTimeNextFixedNote), nameof(CanSubmitQuickSearch), nameof(AssistantSearchAction),
                     nameof(HasFreeTimeNextPlan), nameof(FreeTimeNextAreaHelp) }) OnPropertyChanged(name);
        OnPropertyChanged(nameof(FreeTimeSoonNote));
        OnPropertyChanged(nameof(HasFreeTimeSoonNote));
    }

    private void NotifyFreeTimeLabels()
    {
        foreach (var name in new[] { nameof(IsFreeTimeSearch), nameof(IsStandardQuickSearch), nameof(AssistantFreeTime),
                     nameof(AssistantFreeTimeHelp), nameof(AssistantSearchTitle), nameof(AssistantSearchHelp),
                     nameof(AssistantSearchExpressTitle),
                     nameof(AssistantProposalTitle), nameof(FreeTimeDurationLabel), nameof(FreeTimeEstimate),
                     nameof(FreeTimeDurations), nameof(AssistantSearchAction), nameof(CanSubmitQuickSearch),
                     nameof(ShowFreeTimeInterests), nameof(ShowFreeTimeArea), nameof(ShowQuickInterests),
                     nameof(FreeTimeInterestQuestion), nameof(FreeTimeSurprise), nameof(FreeTimeSurpriseHelp),
                     nameof(FreeTimeSurpriseSelected), nameof(FreeTimeAreaQuestion),
                     nameof(FreeTimeCurrentArea), nameof(FreeTimeCurrentAreaHelp), nameof(FreeTimeNextArea),
                     nameof(FreeTimeNextAreaHelp), nameof(HasFreeTimeNextPlan), nameof(FreeTimeAreaSummary),
                     nameof(FreeTimeCityFallback), nameof(CanUseFreeTimeCityFallback), nameof(FreeTimeCurrentAreaSelected),
                     nameof(FreeTimeNextAreaSelected) }) OnPropertyChanged(name);
        OnPropertyChanged(nameof(FreeTimeSearchLimits));
        OnPropertyChanged(nameof(HasFreeTimeSearchLimits));
        OnPropertyChanged(nameof(ShowFreeTimeCancel));
        OnPropertyChanged(nameof(FreeTimeCancel));
        OnPropertyChanged(nameof(FreeTimeNeedsRestart));
        OnPropertyChanged(nameof(FreeTimeRestart));
        OnPropertyChanged(nameof(FreeTimeEditSearch));
    }
}

using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using TravelCompanion.Mobile.Pages;
using TravelCompanion.Mobile.Services;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Mobile.ViewModels;

public sealed partial class DayPlannerViewModel(AuthSessionService sessions, DayPlanClient client,
    DayPlannerStore store, MobileBootstrapStore bootstrap, TravelCompanionApiClient api,
    ILogger<DayPlannerViewModel> logger) : ViewModelBase
{
    private readonly Guid? user = sessions.CurrentUserId;
    private readonly Guid? trip = sessions.CurrentTripId;
    private readonly long contextVersion = sessions.ContextVersion;
    private CancellationTokenSource? operation;
    private DayPlanOptionsDto? options;
    private DayPlanRequest? request;
    private DayPlanResponse? proposal;
    private DayPlannerAlternative? previousAlternative;
    [ObservableProperty] private bool showComparison;
    private DayPlanApplyRequest? pendingApplication;
    private TripScheduleDto? schedule;
    private bool initializing;
    private bool suppressSelectionSave;
    private int dayCount = 1;
    private int proposalRevision;
    private bool current => sessions.HasSession && user == sessions.CurrentUserId && trip == sessions.CurrentTripId
        && contextVersion == sessions.ContextVersion;

    [ObservableProperty] private DateTime selectedDate = DateTime.Today;
    [ObservableProperty] private DateTime minimumDate = DateTime.Today;
    [ObservableProperty] private DateTime maximumDate = DateTime.Today;
    [ObservableProperty] private string destination = "";
    [ObservableProperty] private string rangeSummary = "";
    [ObservableProperty] private string quota = "";
    [ObservableProperty] private string busyMessage = "";
    [ObservableProperty] private bool showPreferences;
    [ObservableProperty] private int paceIndex = 1;
    [ObservableProperty] private int budgetIndex = 1;
    [ObservableProperty] private bool hasOptions;
    [ObservableProperty] private bool hasProposal;
    [ObservableProperty] private bool stale;
    public ObservableCollection<PlannerDurationOption> Durations { get; } = [];
    public ObservableCollection<PlannerInterestOption> Interests { get; } = [];
    public ObservableCollection<PlannerDayGroup> Days { get; } = [];
    public string Title => Text("PlannerTitle");
    public string Intro => Text("PlannerIntro");
    public string DateLabel => Text("PlannerStartDate");
    public string DurationLabel => Text("PlannerDuration");
    public string DurationPassHint => Text("PlannerDurationPassHint");
    public bool HasDurationPassHint => HasOptions && options is not null
        && (!options.DayCounts.Contains(5) || !options.DayCounts.Contains(7));
    public string PreferencesLabel => Text("PlannerPreferences");
    public string AdjustLabel => Text("PlannerAdjust");
    public string PaceLabel => Text("PlannerPace");
    public string BudgetLabel => Text("PlannerBudget");
    public string InterestsLabel => Text("PlannerInterests");
    public string GenerateLabel => Text("PlannerGenerate");
    public string CancelLabel => Text("CommonCancel");
    public string RetryLabel => Text("PlannerRetry");
    public string ReviewLabel => Text("PlannerReview");
    public string ContinueLabel => Text("PlannerContinue");
    public string ViewTripLabel => Text("PlannerViewTrip");
    public string ResultTitle => Text("PlannerResult");
    public string ResultIntro => proposal?.Message ?? Text("PlannerResultIntro");
    public string RangeCities => string.Join(" · ", ConfiguredCities().Select(city => city.Trim()).Where(city => city.Length > 0)
        .Distinct(StringComparer.OrdinalIgnoreCase));
    public bool HasRangeCities => !string.IsNullOrWhiteSpace(RangeCities);
    public bool HasExistingPlansSummary => schedule is not null;
    public string ExistingPlansSummary => FormatExistingPlansSummary(DateOnly.FromDateTime(SelectedDate),
        DateOnly.FromDateTime(SelectedDate).AddDays(dayCount - 1));
    public bool HasResultExistingPlansSummary => schedule is not null && proposal?.Days.Count > 0;
    public string ResultExistingPlansSummary => proposal?.Days.Count > 0
        ? FormatExistingPlansSummary(proposal.Days.Min(day => day.Date), proposal.Days.Max(day => day.Date)) : "";
    public string UnavailableDays => proposal is null ? "" : string.Join("\n", proposal.Days
        .Where(day => day.Stops.Count == 0).Select(day => $"{day.Date:d MMM}: {Text("PlannerEmptyDay")}"));
    public bool HasUnavailableDays => !string.IsNullOrWhiteSpace(UnavailableDays);
    public string NewProposalLabel => Text("PlannerAnother");
    public bool CanCompare => current && previousAlternative is not null && proposal is not null && IsNotBusy
        && pendingApplication is null && pendingReplacement is null;
    public string CompareLabel => Text("PlannerCompare");
    public string CurrentOptionLabel => Text("PlannerCurrentOption");
    public string PreviousOptionLabel => Text("PlannerPreviousOption");
    public string ChoosePreviousLabel => Text("PlannerChoosePrevious");
    public string KeepCurrentLabel => Text("PlannerKeepCurrent");
    public string PreviousPreview => Preview(previousAlternative?.Proposal);
    public string CurrentPreview => Preview(proposal);
    public string ApplyBlockedReason => ShowComparison ? Text("PlannerChooseForComparison")
        : !Online ? Text("PlannerOffline")
        : !HasOptions || options?.Enabled != true ? Text("PlannerAccessRequired")
        : Stale ? Text("PlannerStaleHelp")
        : pendingReplacement is not null ? Text("PlannerReplacementPending")
        : SelectedCount == 0 && !ShowOpenTrip ? Text("PlannerSelectToApply") : "";
    public bool HasApplyBlockedReason => !string.IsNullOrWhiteSpace(ApplyBlockedReason);
    public bool CanRecoverProposal => !ShowComparison && Stale && Online && IsNotBusy && pendingApplication is null && pendingReplacement is null;
    public string RecoverProposalLabel => Text("PlannerRecoverProposal");
    public string BackLabel => Text("PaywallBack");
    public string SelectionNotice => pendingApplication is not null ? Text("PlannerPendingSave")
        : pendingReplacement is not null ? Text("PlannerReplacementPending") : Text("PlannerSelectionHelp");
    public string PreferencesSummary
    {
        get
        {
            var selected = Interests.Where(item => item.IsSelected).Select(item => item.Label).Take(4).ToArray();
            var interests = string.Join(", ", selected.Take(3)) + (selected.Length > 3 ? "…" : "");
            var summary = $"{Paces[Math.Clamp(PaceIndex, 0, 2)]} · {Budgets[Math.Clamp(BudgetIndex, 0, 2)]}";
            return interests.Length == 0 ? summary : $"{summary} · {interests}";
        }
    }
    public IReadOnlyList<string> Paces => [Text("PlannerRelaxed"), Text("PlannerBalanced"), Text("PlannerActive")];
    public IReadOnlyList<string> Budgets => [Text("PlannerLow"), Text("PlannerMedium"), Text("PlannerHigh")];
    public bool CanGenerate => IsNotBusy && HasOptions && options?.Enabled == true && pendingApplication is null && pendingReplacement is null && Online;
    public bool CanApply => !ShowComparison && IsNotBusy && HasOptions && options?.Enabled == true && HasProposal && SelectedCount > 0 && pendingReplacement is null && !Stale && Online;
    public bool CanSelect => !ShowComparison && IsNotBusy && pendingApplication is null && pendingReplacement is null;
    public bool ShowRetry => HasError || !HasOptions || !Online;
    public bool ShowOpenTrip => !ShowComparison && SelectedCount == 0 && Days.SelectMany(day => day).Any(stop => stop.IsSaved);
    public bool ShowApply => !ShowComparison && !ShowOpenTrip;
    public int SelectedCount => Days.SelectMany(day => day).Count(stop => stop.IsSelected && !stop.IsSaved);
    public string ApplyLabel => string.Format(Text("PlannerAddIdeas"), SelectedCount);
    public string ResultSummary => proposal is null || proposal.Days.Count == 0 ? ""
        : proposal.Days.Count == 1 ? proposal.Days[0].Date.ToString("dddd d MMMM")
        : $"{proposal.Days[0].Date:d MMM} — {proposal.Days[^1].Date:d MMM}";
    private static bool Online => Connectivity.Current.NetworkAccess == NetworkAccess.Internet;
    public static string Text(string key) => LocalizationResourceManager.Instance[key];
    protected override void OnLoadStateChanged() => NotifyActions();
    partial void OnShowComparisonChanged(bool value) => NotifyActions();
    partial void OnSelectedDateChanged(DateTime value) { if (!initializing) { request = null; UpdateRange(); } }
    partial void OnPaceIndexChanged(int value) { if (!initializing) request = null; OnPropertyChanged(nameof(PreferencesSummary)); }
    partial void OnBudgetIndexChanged(int value) { if (!initializing) request = null; OnPropertyChanged(nameof(PreferencesSummary)); }

    public async Task InitializeAsync(DateOnly? date)
    {
        if (!current || user is null || trip is null) return;
        await RunAsync(async ct =>
        {
            var cached = await store.ReadAsync(user.Value, trip.Value, ct);
            if (!current) return;
            if (cached is not null)
            {
                options = cached.Options; request = cached.Request; proposal = cached.Proposal;
                if (date.HasValue && request is not null && date.Value != request.StartDate) request = null;
                pendingApplication = cached.PendingApplication;
                pendingReplacement = cached.PendingReplacement;
                previousAlternative = cached.PreviousAlternative;
                proposalRequest = cached.ProposalRequest ?? (cached.Request?.OperationId == cached.Proposal?.OperationId ? cached.Request : null);
                seenRecommendations.UnionWith(cached.SeenRecommendationIds ?? []);
                if (options is not null) ApplyOptions(options, date ?? cached.SelectedDate ?? request?.StartDate, useProfile: true);
                if ((cached.Preferences ?? request?.Preferences) is { } preferences) ApplyPreferences(preferences);
                dayCount = cached.SelectedDate.HasValue ? cached.DayCount : request?.DayCount ?? 1;
                if (proposal is not null)
                {
                    ApplyProposal(proposal, cached.SelectedStopIds);
                    foreach (var stop in Days.SelectMany(day => day))
                        stop.IsSaved = cached.SavedStopIds?.Contains(stop.Value.Id) == true;
                    proposalRevision = cached.AppliedRevision ?? proposal.BasedOnRevision;
                }
                UpdateRange();
            }
            var snapshot = await bootstrap.GetCachedAsync(cancellationToken: ct);
            if (!current) return;
            ApplySchedule(snapshot?.Value.Schedule);
            await RefreshOptionsCoreAsync(date ?? DateOnly.FromDateTime(SelectedDate), ct,
                useProfile: cached is null, refreshSchedule: false);
        }, "PlannerChecking");
    }

    private async Task RefreshOptionsCoreAsync(DateOnly? date, CancellationToken ct, bool useProfile = false, bool refreshSchedule = true)
    {
        if (!Online) { StatusMessage = Text("PlannerOffline"); NotifyActions(); return; }
        var token = await sessions.GetTokenAsync();
        if (token is null || !current) return;
        var fresh = await client.OptionsAsync(token, ct);
        if (!current) return;
        if (fresh.TripId != trip)
        {
            HasOptions = false; Stale = true; StatusMessage = Text("PlannerReadOnly"); NotifyActions(); return;
        }
        // Preserve an unacknowledged generation: the server may already have its canonical result.
        // A save whose response was lost must replay its receipt even if it advanced the revision.
        if (proposal is not null && pendingApplication is null && fresh.Revision != proposalRevision) Stale = true;
        ApplyOptions(fresh, date, useProfile);
        if (refreshSchedule)
        {
            var snapshot = await bootstrap.GetCachedAsync(cancellationToken: ct);
            if (!current) return;
            ApplySchedule(snapshot?.Value.Schedule);
        }
        if (pendingApplication is not null) StatusMessage = Text("PlannerPendingSave");
        else if (pendingReplacement is not null) StatusMessage = Text("PlannerReplacementPending");
        await PersistAsync(ct);
    }
    private void ApplySchedule(TripScheduleDto? value)
    {
        // An invalidated bootstrap can still contain the snapshot before our confirmed save.
        if (value is not null && value.TripId == trip && (schedule is null || value.Revision >= schedule.Revision)) schedule = value;
        Destination = schedule?.DestinationName ?? "";
        NotifyRangeContext();
    }
    private IEnumerable<string> ConfiguredCities()
    {
        var start = DateOnly.FromDateTime(SelectedDate);
        var end = start.AddDays(dayCount - 1);
        if (options?.CityDays is { Count: > 0 } cities)
            return cities.Where(day => day.Date >= start && day.Date <= end).OrderBy(day => day.Date).SelectMany(day => day.Cities);
        return ExistingRangeItems(start, end).OrderBy(item => item.Date).ThenBy(item => item.StartsAt).Select(item => item.City);
    }
    private IEnumerable<ScheduleItemDto> ExistingRangeItems(DateOnly start, DateOnly end) =>
        schedule?.Items.Where(item => item.Date <= end && (item.EndsOn ?? item.Date) >= start)
            ?? Enumerable.Empty<ScheduleItemDto>();
    private string FormatExistingPlansSummary(DateOnly start, DateOnly end) => schedule is null ? ""
        : ExistingRangeItems(start, end).Select(item => item.Id).Distinct().Count() switch
    {
        0 => Text("PlannerNoExistingPlans"),
        1 => Text("PlannerExistingPlan"),
        var count => string.Format(Text("PlannerExistingPlans"), count)
    };
    private void NotifyRangeContext()
    {
        OnPropertyChanged(nameof(RangeCities)); OnPropertyChanged(nameof(HasRangeCities));
        OnPropertyChanged(nameof(ExistingPlansSummary)); OnPropertyChanged(nameof(HasExistingPlansSummary));
        OnPropertyChanged(nameof(ResultExistingPlansSummary)); OnPropertyChanged(nameof(HasResultExistingPlansSummary));
    }
    private void ApplyOptions(DayPlanOptionsDto value, DateOnly? date, bool useProfile)
    {
        options = value;
        initializing = true;
        MinimumDate = (value.StartsOn ?? DateOnly.FromDateTime(DateTime.Today)).ToDateTime(TimeOnly.MinValue);
        MaximumDate = (value.EndsOn ?? DateOnly.FromDateTime(DateTime.Today)).ToDateTime(TimeOnly.MinValue);
        if (value.TrialAccess?.IsTrial == true && MaximumDate > MinimumDate.AddDays(2)) MaximumDate = MinimumDate.AddDays(2);
        SelectedDate = (date ?? DateOnly.FromDateTime(SelectedDate)).ToDateTime(TimeOnly.MinValue);
        if (SelectedDate < MinimumDate || SelectedDate > MaximumDate) SelectedDate = MinimumDate;
        if (useProfile) ApplyPreferences(new(value.Profile.TravelPace, value.Profile.BudgetLevel, value.Profile.Interests));
        initializing = false;
        HasOptions = true;
        Quota = value.TrialAccess is { IsTrial: true } access
            ? string.Format(Text("PlannerFreeUses"), access.DayImprovementsRemaining) : Text("PlannerPassUses");
        if (!value.Enabled) StatusMessage = Text("PlannerReadOnly");
        UpdateRange(); NotifyActions();
    }
    private void ApplyPreferences(DayPlanPreferencesDto preferences)
    {
        var prior = initializing; initializing = true;
        PaceIndex = preferences.TravelPace switch { "relaxed" => 0, "efficient" => 2, _ => 1 };
        BudgetIndex = preferences.Budget switch { "low" => 0, "high" => 2, _ => 1 };
        Interests.Clear();
        foreach (var key in new[] { "food", "culture", "nature", "history", "art", "shopping", "gardens", "nightlife" })
            Interests.Add(new(key, Text("PlannerInterest_" + key), preferences.Interests.Contains(key, StringComparer.OrdinalIgnoreCase), () =>
            { request = null; OnPropertyChanged(nameof(PreferencesSummary)); }));
        initializing = prior;
        OnPropertyChanged(nameof(PreferencesSummary));
    }
    private void UpdateRange()
    {
        Durations.Clear();
        foreach (var count in new[] { 1, 3, 5, 7 })
        {
            var available = options?.DayCounts.Contains(count) == true;
            var last = DateOnly.FromDateTime(SelectedDate).AddDays(count - 1);
            var withinTrip = options?.EndsOn is { } tripEnd && last <= tripEnd;
            Durations.Add(new(count, count == 1 ? Text("PlannerOneDay") : string.Format(Text("PlannerDays"), count),
                available && withinTrip && last.ToDateTime(TimeOnly.MinValue) <= MaximumDate, withinTrip, count == dayCount));
        }
        if (!Durations.Any(item => item.Count == dayCount && item.Available)) dayCount = 1;
        foreach (var duration in Durations) duration.IsSelected = duration.Count == dayCount;
        var end = SelectedDate.AddDays(dayCount - 1);
        RangeSummary = dayCount == 1 ? SelectedDate.ToString("ddd d MMM") : $"{SelectedDate:d MMM} — {end:d MMM}";
        NotifyRangeContext();
    }
    [RelayCommand] private void TogglePreferences() => ShowPreferences = !ShowPreferences;
    [RelayCommand] private async Task SelectDurationAsync(PlannerDurationOption? duration)
    {
        if (duration is null || IsBusy || !duration.WithinTrip) return;
        if (!duration.Available) { await PaywallNavigation.OpenAsync(PaywallEntryPoint.Today); return; }
        dayCount = duration.Count; request = null; UpdateRange();
    }
    [RelayCommand] private Task RefreshOptionsAsync() => RunAsync(ct =>
        RefreshOptionsCoreAsync(DateOnly.FromDateTime(SelectedDate), ct), "PlannerChecking");
    public Task RefreshAsync() => RefreshOptionsAsync();
    private DayPlanPreferencesDto CurrentPreferences() => new(
        new[] { "relaxed", "balanced", "efficient" }[Math.Clamp(PaceIndex, 0, 2)],
        new[] { "low", "medium", "high" }[Math.Clamp(BudgetIndex, 0, 2)],
        Interests.Where(item => item.IsSelected).Select(item => item.Key).ToArray());
    [RelayCommand] private Task GenerateAsync() => RunAsync(async ct =>
    {
        if (options?.Enabled != true || trip is null || !Online || pendingApplication is not null || pendingReplacement is not null) return;
        if (Interests.Count(item => item.IsSelected) > 3) { ErrorMessage = Text("PlannerInterestLimit"); return; }
        if (proposal?.OperationId == request?.OperationId) request = null;
        request ??= new(trip.Value, options.Revision, DateOnly.FromDateTime(SelectedDate), dayCount,
            Guid.NewGuid(), CurrentPreferences(), CultureInfo.CurrentUICulture.Name);
        await PersistAsync(ct); // Retry this exact request after a timeout or restart.
        var token = await sessions.GetTokenAsync();
        if (token is null || !current) return;
        var old = CaptureAlternative();
        var response = await client.GenerateAsync(token, request, ct);
        if (!current || response.TripId != trip || response.OperationId != request.OperationId) return;
        previousAlternative = old is not null && SameRange(old.Proposal, response)
            && !SameIdeas(old.Proposal, response) ? old : null;
        ShowComparison = false;
        pendingApplication = null; Stale = false;
        proposalRequest = request; seenRecommendations.Clear();
        if (previousAlternative is not null) seenRecommendations.UnionWith(previousAlternative.SeenRecommendationIds);
        ApplyProposal(response, response.Days.SelectMany(day => day.Stops).Select(stop => stop.Id).ToArray());
        if (options is not null && response.TrialAccess is not null)
        {
            options = options with { TrialAccess = response.TrialAccess };
            UpdateQuota();
        }
        await PersistAsync(ct);
        await Shell.Current.GoToAsync(nameof(DayPlanProposalPage), new ShellNavigationQueryParameters { ["Planner"] = this });
    }, "PlannerGenerating");

    private void ApplyProposal(DayPlanResponse response, IReadOnlyList<Guid> selected)
    {
        proposal = response; proposalRevision = response.BasedOnRevision;
        RememberRecommendations(response);
        Days.Clear();
        foreach (var day in response.Days)
            Days.Add(new(day, selected, SelectionChanged));
        HasProposal = true; OnPropertyChanged(nameof(ResultSummary)); OnPropertyChanged(nameof(ResultIntro));
        OnPropertyChanged(nameof(UnavailableDays)); OnPropertyChanged(nameof(HasUnavailableDays)); NotifyRangeContext(); NotifySelection();
        NotifyComparison();
    }
    private DayPlannerAlternative? CaptureAlternative() => proposal is null ? null : new(proposal, proposalRequest,
        Days.SelectMany(day => day).Where(stop => stop.IsSelected && !stop.IsSaved).Select(stop => stop.Value.Id).ToArray(),
        Days.SelectMany(day => day).Where(stop => stop.IsSaved).Select(stop => stop.Value.Id).ToArray(),
        proposalRevision, seenRecommendations.ToArray());
    private static bool SameRange(DayPlanResponse first, DayPlanResponse second) =>
        first.TripId == second.TripId && first.Days.Select(day => day.Date).SequenceEqual(second.Days.Select(day => day.Date));
    private static bool SameIdeas(DayPlanResponse first, DayPlanResponse second) => first.Days.SelectMany(day => day.Stops)
        .Select(stop => stop.RecommendationId).SequenceEqual(second.Days.SelectMany(day => day.Stops).Select(stop => stop.RecommendationId));
    private string Preview(DayPlanResponse? value) => value is null ? "" : string.Join("\n", value.Days.Select(day =>
        day.Date.ToString("d MMM", CultureInfo.CurrentUICulture) + " · " + string.Join(" · ", day.Stops.Take(2)
            .Select(stop => stop.Card.Title.Length > 64 ? stop.Card.Title[..64] + "…" : stop.Card.Title))
        + (day.Stops.Count > 2 ? $" · +{day.Stops.Count - 2}" : "")));
    private void NotifyComparison()
    {
        foreach (var property in new[] { nameof(CanCompare), nameof(PreviousPreview), nameof(CurrentPreview) }) OnPropertyChanged(property);
        ToggleComparisonCommand.NotifyCanExecuteChanged(); ChoosePreviousCommand.NotifyCanExecuteChanged(); KeepCurrentCommand.NotifyCanExecuteChanged();
    }
    [RelayCommand(CanExecute = nameof(CanCompare))] private void ToggleComparison() => ShowComparison = !ShowComparison;
    [RelayCommand(CanExecute = nameof(CanCompare))] private void KeepCurrent() => ShowComparison = false;
    [RelayCommand(CanExecute = nameof(CanCompare))] private async Task ChoosePreviousAsync()
    {
        if (!CanCompare || previousAlternative is not { } previous) return;
        var old = CaptureAlternative();
        previousAlternative = old;
        request = proposalRequest = previous.Request;
        if (previous.Request?.Preferences is { } preferences) ApplyPreferences(preferences);
        seenRecommendations.UnionWith(previous.SeenRecommendationIds);
        ApplyProposal(previous.Proposal, previous.SelectedStopIds);
        foreach (var stop in Days.SelectMany(day => day)) stop.IsSaved = previous.SavedStopIds.Contains(stop.Value.Id);
        proposalRevision = previous.AppliedRevision;
        Stale = options is not null && options.Revision != proposalRevision;
        ShowComparison = false;
        NotifySelection(); NotifyActions(); await PersistSelectionAsync();
    }
    [RelayCommand] private async Task RecoverProposalAsync()
    {
        if (!CanRecoverProposal) return;
        await RefreshOptionsAsync();
        if (!current || !Online || options?.Enabled != true) return;
        await Shell.Current.GoToAsync("..");
    }
    private void SelectionChanged()
    {
        if (suppressSelectionSave) return;
        NotifySelection();
        _ = PersistSelectionAsync();
    }
    private async Task PersistSelectionAsync()
    {
        try { await PersistAsync(); }
        catch (OperationCanceledException) { }
        catch (Exception error)
        {
            logger.LogWarning("Planner preview could not be saved. FailureType={FailureType}", error.GetType().Name);
            if (current) ErrorMessage = Text("PlannerLocalSaveFailed");
        }
    }
    [RelayCommand] private void SelectDay(PlannerDayGroup? day)
    {
        if (!CanSelect || day is null) return;
        var select = day.Any(stop => !stop.IsSelected && !stop.IsSaved);
        suppressSelectionSave = true;
        try { foreach (var stop in day.Where(stop => !stop.IsSaved)) stop.IsSelected = select; }
        finally { suppressSelectionSave = false; }
        SelectionChanged();
    }
    [RelayCommand] private Task ContinueProposalAsync() => HasProposal && current
        ? Shell.Current.GoToAsync(nameof(DayPlanProposalPage), new ShellNavigationQueryParameters { ["Planner"] = this }) : Task.CompletedTask;
    [RelayCommand] private async Task OpenStopAsync(PlannerStopRow? stop)
    {
        if (stop is null || !current) return;
        var snapshot = await bootstrap.GetCachedAsync();
        var recommendation = snapshot?.Value.Recommendations.FirstOrDefault(item => item.Id == stop.Value.RecommendationId);
        if (!current) return;
        if (recommendation is not null)
            await Shell.Current.GoToAsync(nameof(RecommendationDetailPage), new ShellNavigationQueryParameters { ["Recommendation"] = recommendation });
        else await Shell.Current.DisplayAlertAsync(stop.Title, stop.Description, Text("UxOk"));
    }
    [RelayCommand] private async Task ReviewAsync()
    {
        if (current) await Shell.Current.GoToAsync(nameof(TripReviewPage), new ShellNavigationQueryParameters
        { ["ReviewStartDate"] = DateOnly.FromDateTime(SelectedDate), ["ReviewEndDate"] = DateOnly.FromDateTime(SelectedDate).AddDays(dayCount - 1) });
    }
    [RelayCommand] private async Task ViewTripAsync()
    {
        if (current && ShowOpenTrip) await Shell.Current.GoToAsync("//main/schedule", new ShellNavigationQueryParameters
        { ["InitialDate"] = proposal?.Days.FirstOrDefault()?.Date ?? DateOnly.FromDateTime(SelectedDate) });
    }
    [RelayCommand] private Task ApplyAsync() => RunAsync(async ct =>
    {
        if (ShowComparison || proposal is null || trip is null || SelectedCount == 0 || pendingReplacement is not null || Stale || !Online) return;
        pendingApplication ??= new(proposal.OperationId, trip.Value, proposalRevision, Guid.NewGuid(),
            Days.SelectMany(day => day).Where(stop => stop.IsSelected && !stop.IsSaved).Select(stop => stop.Value.Id).ToArray(),
            CultureInfo.CurrentUICulture.Name);
        NotifyActions(); await PersistAsync(ct);
        var token = await sessions.GetTokenAsync();
        if (token is null || !current) return;
        var result = await client.ApplyAsync(token, pendingApplication, ct);
        if (!current || result.TripId != trip) return;
        if (!result.Applied) { ErrorMessage = result.Message; return; }
        var applied = pendingApplication.SelectedStopIds.ToHashSet();
        suppressSelectionSave = true;
        try
        {
            foreach (var stop in Days.SelectMany(day => day).Where(stop => applied.Contains(stop.Value.Id)))
            { stop.IsSaved = true; stop.IsSelected = false; }
        }
        finally { suppressSelectionSave = false; }
        proposalRevision = result.Revision; pendingApplication = null;
        if (options is not null) options = options with { Revision = result.Revision, TrialAccess = result.TrialAccess ?? options.TrialAccess };
        if (schedule is not null)
        {
            var returnedIds = result.Items.Select(item => item.Id).ToHashSet();
            schedule = schedule with { Revision = result.Revision,
                Items = schedule.Items.Where(item => !returnedIds.Contains(item.Id)).Concat(result.Items).ToArray() };
            NotifyRangeContext();
        }
        UpdateQuota();
        StatusMessage = result.Message; bootstrap.Invalidate();
        await PersistAsync(ct); NotifySelection();
        SemanticScreenReader.Default.Announce(StatusMessage);
        _ = RefreshRemindersAsync();
    }, "PlannerSaving");
    [RelayCommand] private void Cancel() => operation?.Cancel();
    public void CancelLoadingOperation() => operation?.Cancel();
    public void NotifyNetworkState() => NotifyActions();
    public async Task SaveDraftAsync() { if (current) await PersistSelectionAsync(); }
    private Task PersistAsync(CancellationToken ct = default) => user is { } userId && trip is { } tripId && current
        ? store.SaveAsync(userId, tripId, new(options, request, proposal,
            Days.SelectMany(day => day).Where(stop => stop.IsSelected && !stop.IsSaved).Select(stop => stop.Value.Id).ToArray(), pendingApplication,
            Days.SelectMany(day => day).Where(stop => stop.IsSaved).Select(stop => stop.Value.Id).ToArray(), proposalRevision,
            DateOnly.FromDateTime(SelectedDate), dayCount, CurrentPreferences(), pendingReplacement,
            seenRecommendations.ToArray(), proposalRequest, previousAlternative), ct)
        : Task.CompletedTask;
    private async Task RunAsync(Func<CancellationToken, Task> action, string message)
    {
        if (IsBusy || !current) return;
        operation?.Dispose(); operation = new CancellationTokenSource();
        try
        {
            IsBusy = true; ErrorMessage = null; StatusMessage = null; BusyMessage = Text(message);
            await action(operation.Token);
        }
        catch (DayPlanApiException error)
        {
            if (!current) return;
            if (error.Code == "stale") { Stale = true; request = null; pendingApplication = null; await PersistSelectionAsync(); }
            if (error.Code is "selection" or "operation") { request = null; pendingApplication = null; pendingReplacement = null; await PersistSelectionAsync(); }
            if (error.Code == "access") HasOptions = false;
            ErrorMessage = error.Code is "stale" or "quota" or "upgrade" or "selection" or "access" or "operation"
                ? error.Message : Text("PlannerConnectionError");
            if (error.Code is "quota" or "upgrade") await PaywallNavigation.OpenAsync(PaywallEntryPoint.Today, limitReached: true);
        }
        catch (DayPlannerStorageException error)
        {
            logger.LogWarning("Planner preview could not be saved. FailureType={FailureType}", error.InnerException?.GetType().Name);
            if (current) ErrorMessage = Text("PlannerLocalSaveFailed");
        }
        catch (OperationCanceledException)
        {
            if (current)
            {
                if (operation.IsCancellationRequested) StatusMessage = Text("PlannerCancelled");
                else ErrorMessage = Text("PlannerConnectionError");
            }
        }
        catch (Exception error) when (error is HttpRequestException or IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        { if (current) ErrorMessage = Text("PlannerConnectionError"); }
        catch (Exception error)
        {
            logger.LogWarning("Planner operation failed. FailureType={FailureType}", error.GetType().Name);
            if (current) ErrorMessage = error is System.Security.Cryptography.CryptographicException
                ? Text("PlannerLocalSaveFailed") : Text("PlannerConnectionError");
        }
        finally { IsBusy = false; NotifyActions(); }
    }
    private void NotifySelection()
    {
        OnPropertyChanged(nameof(ApplyLabel)); OnPropertyChanged(nameof(ShowOpenTrip)); OnPropertyChanged(nameof(ShowApply)); NotifyActions();
    }
    private void NotifyActions()
    {
        OnPropertyChanged(nameof(CanGenerate)); OnPropertyChanged(nameof(CanApply));
        OnPropertyChanged(nameof(CanSelect)); OnPropertyChanged(nameof(SelectionNotice));
        OnPropertyChanged(nameof(ShowApply)); OnPropertyChanged(nameof(ShowOpenTrip));
        OnPropertyChanged(nameof(ShowRetry));
        OnPropertyChanged(nameof(ApplyBlockedReason)); OnPropertyChanged(nameof(HasApplyBlockedReason));
        OnPropertyChanged(nameof(CanRecoverProposal)); NotifyComparison();
        OnPropertyChanged(nameof(DurationPassHint)); OnPropertyChanged(nameof(HasDurationPassHint));
        foreach (var stop in Days.SelectMany(day => day))
        {
            stop.SetSelectable(CanSelect);
            stop.SetReplaceable(CanReplace(stop));
            if (!IsBusy && pendingReplacement?.StopId == stop.Value.Id)
                stop.ReplacementNotice = Text("PlannerReplacementPending");
        }
    }
    private void UpdateQuota() => Quota = options?.TrialAccess is { IsTrial: true } access
        ? string.Format(Text("PlannerFreeUses"), access.DayImprovementsRemaining) : Text("PlannerPassUses");
    private async Task RefreshRemindersAsync()
    {
        try { if (current) await api.NotifyItineraryChangedAsync(); }
        catch (Exception error)
        { logger.LogWarning("Planner reminders could not refresh. FailureType={FailureType}", error.GetType().Name); }
    }
}

public sealed partial class PlannerDurationOption(int count, string label, bool available, bool withinTrip, bool selected) : ObservableObject
{
    public int Count { get; } = count;
    public string Label { get; } = label;
    public bool Available { get; } = available;
    public bool WithinTrip { get; } = withinTrip;
    public int ColumnIndex => Count switch { 1 => 0, 3 => 1, 5 => 2, 7 => 3, _ => 0 };
    public string Hint => Available ? "" : DayPlannerViewModel.Text(WithinTrip ? "PlannerWithPass" : "PlannerOutsideTrip");
    public string AccessibilityLabel => IsSelected
        ? string.Format(DayPlannerViewModel.Text("PlannerDurationSelected"), PlainAccessibilityLabel)
        : PlainAccessibilityLabel;
    private string PlainAccessibilityLabel => string.IsNullOrWhiteSpace(Hint) ? Label : $"{Label}. {Hint}";
    [ObservableProperty] private bool isSelected = selected;
    public Color Background => IsSelected ? Color.FromArgb("#3D3329") : Color.FromArgb("#FFFCF8");
    public Color Foreground => IsSelected ? Colors.White : Color.FromArgb("#302920");
    partial void OnIsSelectedChanged(bool value) { OnPropertyChanged(nameof(Background)); OnPropertyChanged(nameof(Foreground)); OnPropertyChanged(nameof(AccessibilityLabel)); }
}
public sealed partial class PlannerInterestOption(string key, string label, bool selected, Action changed) : ObservableObject
{
    public string Key { get; } = key;
    public string Label { get; } = label;
    [ObservableProperty] private bool isSelected = selected;
    partial void OnIsSelectedChanged(bool value) => changed();
}
public sealed class PlannerDayGroup : ObservableCollection<PlannerStopRow>
{
    public string Title { get; }
    public string Cities { get; }
    public string EmptyMessage { get; }
    public bool IsEmpty => Count == 0;
    public string SelectLabel => DayPlannerViewModel.Text("PlannerSelectDay");
    public PlannerDayGroup(DayPlanDayDto day, IReadOnlyList<Guid> selected, Action changed)
    {
        Title = day.Date.ToString("dddd d MMMM"); Cities = string.Join(" · ", day.Cities);
        EmptyMessage = day.Stops.Count == 0 ? DayPlannerViewModel.Text("PlannerEmptyDay")
            : day.MissingMoments.Count == 0 ? "" : DayPlannerViewModel.Text("PlannerPartialDay");
        foreach (var stop in day.Stops) Add(new(stop, selected.Contains(stop.Id), changed, Cities));
    }
}
public sealed partial class PlannerStopRow(DayPlanStopDto value, bool selected, Action changed, string? fallbackPlace = null) : ObservableObject
{
    public DayPlanStopDto Value { get; } = value;
    public string Title => Value.Card.Title;
    public string Description => Value.Card.Description ?? "";
    public string Place => string.IsNullOrWhiteSpace(Value.Place) ? fallbackPlace ?? "" : Value.Place;
    public bool HasPlace => !string.IsNullOrWhiteSpace(Place);
    public string Moment => DayPlannerViewModel.Text("AssistantProposal" + (Value.PeriodKey switch
    { "morning" => "Morning", "midday" => "Midday", "afternoon" => "Afternoon", "night" => "Night", _ => "Flexible" }));
    public string Reason => Value.Card.WhyItFits.FirstOrDefault() ?? "";
    public string Warnings => string.Join("\n", Value.Card.Warnings);
    public bool HasWarnings => Value.Card.Warnings.Count > 0;
    public string OpenDescription => string.Format(DayPlannerViewModel.Text("PlannerOpenIdea"), Title);
    public string SelectDescription => string.Format(DayPlannerViewModel.Text("PlannerSelectIdea"), Title);
    public string SavedLabel => DayPlannerViewModel.Text("AssistantProposalSaved");
    public string ReplaceDescription => string.Format(DayPlannerViewModel.Text("PlannerReplaceIdea"), Title);
    public string ReplacingLabel => DayPlannerViewModel.Text("PlannerReplacingIdea");
    public bool HasReplacementNotice => !string.IsNullOrWhiteSpace(ReplacementNotice);
    [ObservableProperty] private string replacementNotice = "";
    partial void OnReplacementNoticeChanged(string value) => OnPropertyChanged(nameof(HasReplacementNotice));
    [ObservableProperty] private bool isReplacing;
    [ObservableProperty] private bool isSelected = selected;
    [ObservableProperty] private bool isSaved;
    private bool selectable = true;
    private bool replaceable;
    public bool CanReplaceRow => replaceable && !IsSaved && !IsReplacing;
    public void SetReplaceable(bool value) { replaceable = value; OnPropertyChanged(nameof(CanReplaceRow)); }
    public bool CanSelectRow => selectable && !IsSaved;
    public void SetSelectable(bool value) { selectable = value; OnPropertyChanged(nameof(CanSelectRow)); }
    partial void OnIsSavedChanged(bool value) { OnPropertyChanged(nameof(CanSelectRow)); OnPropertyChanged(nameof(CanReplaceRow)); }
    partial void OnIsReplacingChanged(bool value) => OnPropertyChanged(nameof(CanReplaceRow));
    partial void OnIsSelectedChanged(bool value) => changed();
}

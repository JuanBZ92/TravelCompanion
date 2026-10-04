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
    private DayPlanApplyRequest? pendingApplication;
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
    public string UnavailableDays => proposal is null ? "" : string.Join("\n", proposal.Days
        .Where(day => day.Stops.Count == 0).Select(day => $"{day.Date:d MMM}: {Text("PlannerEmptyDay")}"));
    public bool HasUnavailableDays => !string.IsNullOrWhiteSpace(UnavailableDays);
    public string NewProposalLabel => Text("PlannerAnother");
    public string BackLabel => Text("PaywallBack");
    public string SelectionNotice => pendingApplication is not null ? Text("PlannerPendingSave") : Text("PlannerSelectionHelp");
    public string PreferencesSummary => $"{Paces[Math.Clamp(PaceIndex, 0, 2)]} · {Budgets[Math.Clamp(BudgetIndex, 0, 2)]}";
    public IReadOnlyList<string> Paces => [Text("PlannerRelaxed"), Text("PlannerBalanced"), Text("PlannerActive")];
    public IReadOnlyList<string> Budgets => [Text("PlannerLow"), Text("PlannerMedium"), Text("PlannerHigh")];
    public bool CanGenerate => IsNotBusy && HasOptions && options?.Enabled == true && pendingApplication is null && Online;
    public bool CanApply => IsNotBusy && HasOptions && options?.Enabled == true && HasProposal && SelectedCount > 0 && !Stale && Online;
    public bool CanSelect => IsNotBusy && pendingApplication is null;
    public bool ShowRetry => HasError || !HasOptions || !Online;
    public bool ShowOpenTrip => SelectedCount == 0 && Days.SelectMany(day => day).Any(stop => stop.IsSaved);
    public bool ShowApply => !ShowOpenTrip;
    public int SelectedCount => Days.SelectMany(day => day).Count(stop => stop.IsSelected && !stop.IsSaved);
    public string ApplyLabel => string.Format(Text("PlannerAddIdeas"), SelectedCount);
    public string ResultSummary => proposal is null || proposal.Days.Count == 0 ? ""
        : proposal.Days.Count == 1 ? proposal.Days[0].Date.ToString("dddd d MMMM")
        : $"{proposal.Days[0].Date:d MMM} — {proposal.Days[^1].Date:d MMM}";
    private static bool Online => Connectivity.Current.NetworkAccess == NetworkAccess.Internet;
    public static string Text(string key) => LocalizationResourceManager.Instance[key];
    protected override void OnLoadStateChanged() => NotifyActions();
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
            Destination = snapshot?.Value.Schedule?.DestinationName ?? "";
            await RefreshOptionsCoreAsync(date ?? DateOnly.FromDateTime(SelectedDate), ct, useProfile: cached is null);
        }, "PlannerChecking");
    }

    private async Task RefreshOptionsCoreAsync(DateOnly? date, CancellationToken ct, bool useProfile = false)
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
        if (pendingApplication is not null) StatusMessage = Text("PlannerPendingSave");
        await PersistAsync(ct);
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
            Interests.Add(new(key, Text("PlannerInterest_" + key), preferences.Interests.Contains(key, StringComparer.OrdinalIgnoreCase), () => request = null));
        initializing = prior;
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
        if (options?.Enabled != true || trip is null || !Online || pendingApplication is not null) return;
        if (Interests.Count(item => item.IsSelected) > 3) { ErrorMessage = Text("PlannerInterestLimit"); return; }
        if (proposal?.OperationId == request?.OperationId) request = null;
        request ??= new(trip.Value, options.Revision, DateOnly.FromDateTime(SelectedDate), dayCount,
            Guid.NewGuid(), CurrentPreferences(), CultureInfo.CurrentUICulture.Name);
        await PersistAsync(ct); // Retry this exact request after a timeout or restart.
        var token = await sessions.GetTokenAsync();
        if (token is null || !current) return;
        var response = await client.GenerateAsync(token, request, ct);
        if (!current || response.TripId != trip || response.OperationId != request.OperationId) return;
        pendingApplication = null; Stale = false;
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
        Days.Clear();
        foreach (var day in response.Days)
            Days.Add(new(day, selected, SelectionChanged));
        HasProposal = true; OnPropertyChanged(nameof(ResultSummary)); OnPropertyChanged(nameof(ResultIntro));
        OnPropertyChanged(nameof(UnavailableDays)); OnPropertyChanged(nameof(HasUnavailableDays)); NotifySelection();
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
        if (proposal is null || trip is null || SelectedCount == 0 || Stale || !Online) return;
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
            DateOnly.FromDateTime(SelectedDate), dayCount, CurrentPreferences()), ct)
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
            if (error.Code is "selection" or "operation") { request = null; pendingApplication = null; await PersistSelectionAsync(); }
            if (error.Code == "access") HasOptions = false;
            ErrorMessage = error.Code is "stale" or "quota" or "upgrade" or "selection" or "access" or "operation"
                ? error.Message : Text("PlannerConnectionError");
            if (error.Code is "quota" or "upgrade") await PaywallNavigation.OpenAsync(PaywallEntryPoint.Today, limitReached: true);
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
        OnPropertyChanged(nameof(ShowRetry));
        foreach (var stop in Days.SelectMany(day => day)) stop.SetSelectable(CanSelect);
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
    public string Hint => Available ? "" : DayPlannerViewModel.Text(WithinTrip ? "PlannerWithPass" : "PlannerOutsideTrip");
    [ObservableProperty] private bool isSelected = selected;
    public Color Background => IsSelected ? Color.FromArgb("#3D3329") : Color.FromArgb("#FFFCF8");
    public Color Foreground => IsSelected ? Colors.White : Color.FromArgb("#302920");
    partial void OnIsSelectedChanged(bool value) { OnPropertyChanged(nameof(Background)); OnPropertyChanged(nameof(Foreground)); }
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
        foreach (var stop in day.Stops) Add(new(stop, selected.Contains(stop.Id), changed));
    }
}
public sealed partial class PlannerStopRow(DayPlanStopDto value, bool selected, Action changed) : ObservableObject
{
    public DayPlanStopDto Value { get; } = value;
    public string Title => Value.Card.Title;
    public string Description => Value.Card.Description ?? "";
    public string Moment => DayPlannerViewModel.Text("AssistantProposal" + (Value.PeriodKey switch
    { "morning" => "Morning", "midday" => "Midday", "afternoon" => "Afternoon", "night" => "Night", _ => "Flexible" }));
    public string Reason => Value.Card.WhyItFits.FirstOrDefault() ?? "";
    public string Warnings => string.Join("\n", Value.Card.Warnings);
    public bool HasWarnings => Value.Card.Warnings.Count > 0;
    public string OpenDescription => string.Format(DayPlannerViewModel.Text("PlannerOpenIdea"), Title);
    public string SelectDescription => string.Format(DayPlannerViewModel.Text("PlannerSelectIdea"), Title);
    public string SavedLabel => DayPlannerViewModel.Text("AssistantProposalSaved");
    [ObservableProperty] private bool isSelected = selected;
    [ObservableProperty] private bool isSaved;
    private bool selectable = true;
    public bool CanSelectRow => selectable && !IsSaved;
    public void SetSelectable(bool value) { selectable = value; OnPropertyChanged(nameof(CanSelectRow)); }
    partial void OnIsSavedChanged(bool value) => OnPropertyChanged(nameof(CanSelectRow));
    partial void OnIsSelectedChanged(bool value) => changed();
}

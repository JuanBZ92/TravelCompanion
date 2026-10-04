using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using TravelCompanion.Mobile.Pages;
using TravelCompanion.Mobile.Services;
using TravelCompanion.Shared;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Mobile.ViewModels;

public sealed partial class TripReviewViewModel(AuthSessionService sessions, MobileBootstrapStore bootstrap,
    TravelCompanionApiClient api, ProductAnalyticsTracker analytics) : ViewModelBase
{
    private DateOnly? startsOn;
    private DateOnly? endsOn;
    public void SetRange(DateOnly first, DateOnly last)
    {
        if (last < first) return;
        startsOn = first; endsOn = last;
    }
    public ObservableCollection<TripReviewDayViewModel> Days { get; } = [];
    public string Title => Text("ReviewTrip");
    public string Coverage => Text("ReviewCoverage");
    public string EmptyText => Text("ReviewNoDates");
    private static string Text(string key) => LocalizationResourceManager.Instance[key];

    [RelayCommand]
    private Task LoadReviewAsync() => LoadAsync(async ct =>
    {
        var user = sessions.CurrentUserId;
        var trip = sessions.CurrentTripId;
        var cached = await bootstrap.GetCachedAsync(cancellationToken: ct);
        if (user != sessions.CurrentUserId || trip != sessions.CurrentTripId) return;
        if (cached?.Value.Schedule is { } saved) Apply(saved);
        var token = await sessions.GetTokenAsync();
        if (token is null) return;
        if (Connectivity.Current.NetworkAccess == NetworkAccess.Internet)
        {
            var schedule = await api.GetScheduleAsync(token, ct);
            if (user != sessions.CurrentUserId || trip != sessions.CurrentTripId) return;
            if (schedule is not null && schedule.TripId == trip) Apply(schedule);
        }
        await analytics.TrackAsync("day_review_viewed", "trip_review", cancellationToken: ct);
    });

    private void Apply(TripScheduleDto schedule)
    {
        if (schedule.TripId != sessions.CurrentTripId) return;
        Days.Clear();
        foreach (var day in (schedule.DayReviews ?? ScheduleReviewAnalyzer.Analyze(schedule.Items, schedule.StartsOn, schedule.EndsOn))
            .Where(day => (!startsOn.HasValue || day.Date >= startsOn) && (!endsOn.HasValue || day.Date <= endsOn)))
            Days.Add(new TripReviewDayViewModel(day, schedule, sessions));
    }
}

public sealed class TripReviewDayViewModel
{
    public TripReviewDayViewModel(DayReviewDto review, TripScheduleDto schedule, AuthSessionService sessions)
    {
        Date = review.Date.ToString("ddd dd MMM");
        var english = System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName != "es";
        var covered = schedule.Items.Where(item => item.Date <= review.Date && (item.EndsOn ?? item.Date) >= review.Date).ToList();
        var hasFixedTimes = covered.Any(item => item.HasExactTime && item.Type != ReservationType.Lodging);
        Title = english ? review.Status switch
        {
            DayReviewStatuses.NeedsAttention => "Your day needs adjustments",
            DayReviewStatuses.Tight => "Points to review",
            _ => hasFixedTimes ? "No conflicts detected" : "No fixed times to check"
        } : hasFixedTimes ? review.Title : "Sin horarios fijos que comprobar";
        Summary = english ? $"{review.Issues.Count} points to review" : review.Summary;
        Issues = review.Issues.Select(issue => new TripReviewIssueViewModel(issue, schedule, sessions, review.Date)).ToList();
        ViewDayCommand = new AsyncRelayCommand(() => Shell.Current.GoToAsync("//main/schedule",
            new ShellNavigationQueryParameters { ["InitialDate"] = review.Date }));
        ImproveCommand = new AsyncRelayCommand(async () =>
        {
            if (!sessions.CanEditItinerary || sessions.IsTrial && !FreePlanningPolicy.CanPlanDate(schedule.StartsOn, review.Date))
            { await PaywallNavigation.OpenAsync(PaywallEntryPoint.Today, limitReached: true); return; }
            await Shell.Current.GoToAsync(nameof(ImproveDayPage), new ShellNavigationQueryParameters
            {
                ["Date"] = review.Date
            });
        });
        CanImprove = sessions.IsBuilder;
    }
    public string Date { get; }
    public string Title { get; }
    public string Summary { get; }
    public string ViewDayText => LocalizationResourceManager.Instance["ReviewViewDay"];
    public string ImproveText => LocalizationResourceManager.Instance["ReviewImprove"];
    public bool CanImprove { get; }
    public IReadOnlyList<TripReviewIssueViewModel> Issues { get; }
    public IAsyncRelayCommand ViewDayCommand { get; }
    public IAsyncRelayCommand ImproveCommand { get; }
}

public sealed class TripReviewIssueViewModel
{
    public TripReviewIssueViewModel(DayReviewIssueDto issue, TripScheduleDto schedule, AuthSessionService sessions, DateOnly reviewDate)
    {
        var english = System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName != "es";
        Message = english ? issue.Kind switch
        {
            DayReviewIssueKinds.Overlap => "These plans overlap. Review their times.",
            DayReviewIssueKinds.TightTransfer => "There may not be enough time between these plans.",
            DayReviewIssueKinds.PackedDay => "This day contains many fixed activities.",
            _ => "Some information is missing; not all conflicts can be checked."
        } : issue.Message;
        var candidates = schedule.Items.Where(item => issue.ItemIds.Contains(item.Id)).ToList();
        CanOpen = candidates.Count > 0;
        OpenCommand = new AsyncRelayCommand(async () =>
        {
            if (!CanOpen || sessions.CurrentTripId != schedule.TripId) return;
            await Shell.Current.GoToAsync(nameof(DayConflictPage), new ShellNavigationQueryParameters
            {
                ["ReviewDate"] = reviewDate,
                ["IssueKey"] = DayConflictCursor.Key(issue)
            });
        });
    }
    public string Message { get; }
    public bool CanOpen { get; }
    public string OpenText => LocalizationResourceManager.Instance["ReviewPlans"];
    public IAsyncRelayCommand OpenCommand { get; }
}

using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using TravelCompanion.Mobile.Pages;
using TravelCompanion.Mobile.Services;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Mobile.ViewModels;

public sealed partial class ThematicRoutesViewModel(
    TravelCompanionApiClient api,
    AuthSessionService sessions,
    OfflineCacheService offlineCache,
    ProductAnalyticsTracker analytics) : ViewModelBase
{
    private string _newRouteName = string.Empty;
    private string _newRouteCity = "Tokyo";
    private DateTime _routeDate = DateTime.Today;
    private TimeSpan _windowStart = new(9, 0, 0);
    private TimeSpan _windowEnd = new(21, 0, 0);
    private string _selectedPace = "balanced";
    public ObservableCollection<ThematicRouteDto> Routes { get; } = [];
    public IReadOnlyList<string> PaceOptions { get; } = ["slow", "balanced", "fast"];
    public string NewRouteName { get => _newRouteName; set => SetProperty(ref _newRouteName, value); }
    public string NewRouteCity { get => _newRouteCity; set => SetProperty(ref _newRouteCity, value); }
    public DateTime RouteDate { get => _routeDate; set => SetProperty(ref _routeDate, value); }
    public TimeSpan WindowStart { get => _windowStart; set => SetProperty(ref _windowStart, value); }
    public TimeSpan WindowEnd { get => _windowEnd; set => SetProperty(ref _windowEnd, value); }
    public string SelectedPace { get => _selectedPace; set => SetProperty(ref _selectedPace, value); }
    [RelayCommand] private Task LoadRoutesAsync() => LoadAsync(async ct =>
    {
        var token = await sessions.GetTokenAsync() ?? throw new InvalidOperationException(Resource("CommerceSessionExpired"));
        IReadOnlyList<ThematicRouteDto> routes;
        try
        {
            routes = await api.GetThematicRoutesAsync(token, ct);
            await offlineCache.SaveAsync(CacheKey, routes.ToList(), ct);
        }
        catch (HttpRequestException)
        {
            routes = (await offlineCache.GetAsync<List<ThematicRouteDto>>(CacheKey, cancellationToken: ct))?.Value ?? [];
            if (routes.Count == 0) throw;
        }
        Routes.Clear(); foreach (var route in routes) Routes.Add(route);
        await analytics.TrackAsync("route_viewed", "routes_library", tripId: sessions.CurrentTripId, cancellationToken: ct);
    });

    [RelayCommand] private async Task CreateRouteAsync()
    {
        if (string.IsNullOrWhiteSpace(NewRouteName) || string.IsNullOrWhiteSpace(NewRouteCity)) return;
        var labels = new[] { Resource("RoutesThemeFood"), Resource("RoutesThemeHistory"), Resource("RoutesThemeArt"), Resource("RoutesThemeNature"), Resource("RoutesThemeShopping") };
        var selected = await Shell.Current.DisplayActionSheetAsync(Resource("RoutesThemeTitle"), Resource("CommonCancel"), null, labels);
        var index = Array.IndexOf(labels, selected); if (index < 0) return;
        await LoadAsync(async ct =>
        {
            var token = await sessions.GetTokenAsync() ?? throw new InvalidOperationException(Resource("CommerceSessionExpired"));
            var route = await api.CreateThematicRouteAsync(token, new(NewRouteName.Trim(), (RouteTheme)index, NewRouteCity.Trim(),
                DateOnly.FromDateTime(RouteDate), TimeOnly.FromTimeSpan(WindowStart), TimeOnly.FromTimeSpan(WindowEnd), SelectedPace), ct)
                ?? throw new InvalidOperationException(Resource("RoutesNotEnoughStops"));
            Routes.Insert(0, route);
            NewRouteName = string.Empty;
            await offlineCache.SaveAsync(CacheKey, Routes.ToList(), ct);
        });
    }

    [RelayCommand] private async Task EditRouteAsync(ThematicRouteDto route)
    {
        if (route.Origin != RouteOrigin.Personal) return;
        var name = await Shell.Current.DisplayPromptAsync(Resource("RoutesEditTitle"), Resource("RoutesNamePrompt"), Resource("CommonSave"), Resource("CommonCancel"), initialValue: route.Name);
        if (string.IsNullOrWhiteSpace(name)) return;
        var stops = route.Stops.OrderBy(item => item.SortOrder).ToList();
        var keep = Resource("RoutesKeepOrder");
        var reverse = Resource("RoutesReverseOrder");
        var move = Resource("RoutesMoveStop");
        var remove = Resource("RoutesDeleteStop");
        var replace = Resource("RoutesReplaceStop");
        var choice = await Shell.Current.DisplayActionSheetAsync(Resource("RoutesOrderTitle"), Resource("CommonCancel"), null,
            keep, reverse, move, remove, replace);
        if (choice is null || choice == Resource("CommonCancel")) return;
        if (choice == reverse) stops.Reverse();
        else if (choice == move)
        {
            var selected = await SelectStopAsync(stops, "RoutesSelectStop");
            if (selected is null) return;
            var earlier = Resource("RoutesMoveEarlier");
            var direction = await Shell.Current.DisplayActionSheetAsync(Resource("RoutesMoveStop"), Resource("CommonCancel"), null,
                earlier, Resource("RoutesMoveLater"));
            var index = stops.IndexOf(selected);
            var target = direction == earlier ? index - 1 : index + 1;
            if (direction is null || target < 0 || target >= stops.Count) return;
            stops.RemoveAt(index); stops.Insert(target, selected);
        }
        else if (choice == remove)
        {
            if (stops.Count <= 2) { await Shell.Current.DisplayAlertAsync(Resource("RoutesEditTitle"), Resource("RoutesMinimumStops"), Resource("CommonContinue")); return; }
            var selected = await SelectStopAsync(stops, "RoutesSelectStop");
            if (selected is null) return;
            stops.Remove(selected);
        }
        else if (choice == replace)
        {
            var selected = await SelectStopAsync(stops, "RoutesSelectStop");
            if (selected is null) return;
            var token = await sessions.GetTokenAsync() ?? throw new InvalidOperationException(Resource("CommerceSessionExpired"));
            var used = stops.Select(item => item.RecommendationId).ToHashSet();
            var alternatives = (await api.GetRecommendationsAsync(token))
                .Where(item => !used.Contains(item.Id)
                    && item.Neighborhood.Contains(route.City, StringComparison.OrdinalIgnoreCase))
                .OrderBy(item => item.Title).Take(20).ToList();
            if (alternatives.Count == 0) { await Shell.Current.DisplayAlertAsync(Resource("RoutesEditTitle"), Resource("RoutesNoAlternatives"), Resource("CommonContinue")); return; }
            var labels = alternatives.Select(item => item.Title).ToArray();
            var alternativeLabel = await Shell.Current.DisplayActionSheetAsync(Resource("RoutesReplaceStop"), Resource("CommonCancel"), null, labels);
            var alternative = alternatives.FirstOrDefault(item => item.Title == alternativeLabel);
            if (alternative is null) return;
            var index = stops.IndexOf(selected);
            stops[index] = selected with { RecommendationId = alternative.Id, Title = alternative.Title,
                DurationMinutes = Math.Max(15, alternative.SuggestedDurationMinutes) };
        }
        var ids = stops.Select(item => item.RecommendationId).ToList();
        await LoadAsync(async ct =>
        {
            var token = await sessions.GetTokenAsync() ?? throw new InvalidOperationException(Resource("CommerceSessionExpired"));
            var updated = await api.UpdateThematicRouteAsync(token, route.Id, new(name.Trim(), route.Version, ids), ct)
                ?? throw new InvalidOperationException(Resource("RoutesChanged"));
            var index = Routes.IndexOf(route); if (index >= 0) Routes[index] = updated;
            await offlineCache.SaveAsync(CacheKey, Routes.ToList(), ct);
        });
    }

    private static async Task<RouteStopDto?> SelectStopAsync(IReadOnlyList<RouteStopDto> stops, string titleKey)
    {
        var labels = stops.Select((item, index) => $"{index + 1}. {item.Title}").ToArray();
        var selected = await Shell.Current.DisplayActionSheetAsync(Resource(titleKey), Resource("CommonCancel"), null, labels);
        var index = Array.IndexOf(labels, selected);
        return index >= 0 ? stops[index] : null;
    }

    [RelayCommand] private async Task DeleteRouteAsync(ThematicRouteDto route)
    {
        if (route.Origin != RouteOrigin.Personal) return;
        var keepActivities = Resource("RoutesRemoveKeepActivities");
        var removeActivities = Resource("RoutesRemoveWithActivities");
        var choice = await Shell.Current.DisplayActionSheetAsync(Resource("RoutesRemoveTitle"), Resource("CommonCancel"), null,
            keepActivities, removeActivities);
        if (choice is null || choice == Resource("CommonCancel")) return;
        var shouldRemoveActivities = choice == removeActivities;
        if (shouldRemoveActivities && !await Shell.Current.DisplayAlertAsync(Resource("RoutesRemoveTitle"),
                Resource("RoutesRemoveActivitiesWarning"), Resource("CommonRemove"), Resource("CommonCancel"))) return;
        await LoadAsync(async ct =>
        {
            var token = await sessions.GetTokenAsync() ?? throw new InvalidOperationException(Resource("CommerceSessionExpired"));
            int? revision = null;
            if (shouldRemoveActivities)
                revision = (await api.GetScheduleAsync(token, ct) ?? throw new InvalidOperationException(Resource("RoutesTripLoadError"))).Revision;
            if (!await api.DeleteThematicRouteAsync(token, route.Id, shouldRemoveActivities, revision, ct))
                throw new InvalidOperationException(Resource("RoutesRemoveError"));
            Routes.Remove(route); await offlineCache.SaveAsync(CacheKey, Routes.ToList(), ct);
        });
    }

    [RelayCommand] private Task SaveTemplateAsync(ThematicRouteDto route) => LoadAsync(async ct =>
    {
        if (route.Origin != RouteOrigin.Yuku) return;
        var token = await sessions.GetTokenAsync() ?? throw new InvalidOperationException(Resource("CommerceSessionExpired"));
        var copy = await api.CopyThematicRouteAsync(token, route.Id, ct)
            ?? throw new InvalidOperationException(Resource("RoutesCopyError"));
        Routes.Insert(0, copy);
        await offlineCache.SaveAsync(CacheKey, Routes.ToList(), ct);
    });

    [RelayCommand] private Task ApplyRouteAsync(ThematicRouteDto route) => LoadAsync(async ct =>
    {
        var token = await sessions.GetTokenAsync() ?? throw new InvalidOperationException(Resource("CommerceSessionExpired"));
        var schedule = await api.GetScheduleAsync(token, ct) ?? throw new InvalidOperationException(Resource("RoutesTripLoadError"));
        var date = route.PlannedDate ?? DateOnly.FromDateTime(RouteDate);
        if (date < schedule.StartsOn) date = schedule.StartsOn; if (date > schedule.EndsOn) date = schedule.EndsOn;
        var proposal = await api.PrepareRouteApplicationAsync(token, route.Id,
            new(date, route.WindowStart ?? TimeOnly.FromTimeSpan(WindowStart), route.WindowEnd ?? TimeOnly.FromTimeSpan(WindowEnd),
                schedule.Revision, Guid.NewGuid().ToString("N")), ct)
            ?? throw new InvalidOperationException(Resource("RoutesPrepareError"));
        await offlineCache.SaveAsync(ProposalCacheKey, proposal, ct);
        await Shell.Current.GoToAsync(nameof(DayProposalPage), new Dictionary<string, object> { ["Proposal"] = proposal });
    });

    [RelayCommand] private Task ReorganizeDayAsync() => LoadAsync(async ct =>
    {
        var token = await sessions.GetTokenAsync() ?? throw new InvalidOperationException(Resource("CommerceSessionExpired"));
        var schedule = await api.GetScheduleAsync(token, ct) ?? throw new InvalidOperationException(Resource("RoutesTripLoadError"));
        var date = DateOnly.FromDateTime(RouteDate);
        if (date < schedule.StartsOn) date = schedule.StartsOn; if (date > schedule.EndsOn) date = schedule.EndsOn;
        var proposal = await api.CreateDayProposalAsync(token, new(date, DayPlanningGoal.Reorganize, schedule.Revision,
            TimeOnly.FromTimeSpan(WindowStart), TimeOnly.FromTimeSpan(WindowEnd), Guid.NewGuid().ToString("N")), ct)
            ?? throw new InvalidOperationException(Resource("RoutesProposalError"));
        await offlineCache.SaveAsync(ProposalCacheKey, proposal, ct);
        await Shell.Current.GoToAsync(nameof(DayProposalPage), new Dictionary<string, object> { ["Proposal"] = proposal });
    });

    [RelayCommand] private Task OpenSavedProposalAsync() => LoadAsync(async ct =>
    {
        var proposal = (await offlineCache.GetAsync<DayProposalDto>(ProposalCacheKey, cancellationToken: ct))?.Value
            ?? throw new InvalidOperationException(Resource("RoutesNoSavedProposal"));
        await Shell.Current.GoToAsync(nameof(DayProposalPage), new Dictionary<string, object> { ["Proposal"] = proposal });
    });

    private string CacheKey => $"thematic-routes-trip-{sessions.CurrentTripId?.ToString() ?? "none"}-user-{sessions.CurrentUserId?.ToString() ?? "none"}";
    private string ProposalCacheKey => $"last-day-proposal-trip-{sessions.CurrentTripId?.ToString() ?? "none"}-user-{sessions.CurrentUserId?.ToString() ?? "none"}";
    private static string Resource(string key) => LocalizationResourceManager.Instance.GetString(key);
}

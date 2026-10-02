using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using TravelCompanion.Mobile.Pages;
using TravelCompanion.Mobile.Services;
using TravelCompanion.Shared;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Mobile.ViewModels;

public sealed partial class TripPreparationViewModel(AuthSessionService sessions, TravelCompanionApiClient api,
    MobileBootstrapStore bootstrap, OfflineCacheService cache, OfflineTripPreparationService offline,
    MobileSyncStateStore syncState, TripDocumentStore documents, ProductAnalyticsTracker analytics) : ViewModelBase
{
    private readonly Guid? user = sessions.CurrentUserId;
    private readonly Guid? trip = sessions.CurrentTripId;
    private readonly long contextVersion = sessions.ContextVersion;
    public ObservableCollection<PreparationRow> Items { get; } = [];
    private string summary = "";
    public string Summary { get => summary; private set => SetProperty(ref summary, value); }
    private bool canSave;
    public bool CanSave { get => canSave; private set => SetProperty(ref canSave, value); }
    public string Title => Text("PreparationTitle");
    public string Introduction => Text("PreparationIntro");
    public string OfflineScope => Text("OfflineScope");
    private string CacheKey => $"preparation-{user:N}-{trip:N}";
    private bool IsCurrent => sessions.HasSession && sessions.CurrentUserId == user && sessions.CurrentTripId == trip
        && sessions.ContextVersion == contextVersion;
    public static string Text(string key) => LocalizationResourceManager.Instance[key];

    [RelayCommand] private Task LoadPreparationAsync() => LoadAsync(RefreshAsync);

    private async Task RefreshAsync(CancellationToken ct)
    {
        CanSave = false;
        if (!IsCurrent || trip is null) { Items.Clear(); Summary = Text("PreparationNoSchedule"); return; }
        var stored = await cache.GetAsync<List<TripPreparationItemDto>>(CacheKey, cancellationToken: ct);
        if (!IsCurrent) return;
        Apply(stored?.Value ?? TripPreparationKeys.All.Select(key => new TripPreparationItemDto(key, false, 0)).ToList());
        var saved = await bootstrap.GetCachedAsync(cancellationToken: ct);
        var schedule = saved?.Value.Schedule;
        var manifest = await offline.GetAsync(ct);
        var localDocuments = await documents.ListAsync(ct);
        var versions = await syncState.GetCachedStateAsync(ct);
        if (!IsCurrent) return;
        var progress = TripPreparationProgress.Create(trip.Value, schedule, manifest, versions?.CatalogVersion,
            sessions.HasCuratedDocs ? versions?.DocumentsVersion : null);
        Summary = (progress.HasSchedule ? string.Format(Text("PreparationSummary"), progress.ActivityDays, progress.Issues, localDocuments.Count)
            : Text("PreparationNoSchedule")) + "\n" + Text(progress.OfflineStatusKey) + "\n"
            + string.Format(Text("PreparationResources"), progress.DownloadedResources, progress.DownloadableResources)
            + "\n" + Text("PreparationCached");
        if (Connectivity.Current.NetworkAccess != NetworkAccess.Internet) return;
        var token = await sessions.GetTokenAsync();
        if (token is null || !IsCurrent) return;
        var latest = await api.GetPreparationAsync(token, trip.Value, ct);
        if (!IsCurrent || latest is null) return;
        await cache.SaveAsync(CacheKey, latest, ct);
        if (!IsCurrent) return;
        Apply(latest);
        CanSave = true;
        await analytics.TrackAsync("trip_preparation_viewed", "preparation", tripId: trip, cancellationToken: ct);
    }

    private void Apply(IEnumerable<TripPreparationItemDto> items)
    {
        Items.Clear();
        foreach (var item in items) Items.Add(new(item, ToggleItemCommand));
    }

    [RelayCommand]
    private Task ToggleItemAsync(PreparationRow? row) => LoadAsync(async ct =>
    {
        if (row is null || !CanSave || !IsCurrent || trip is null) return;
        CanSave = false;
        var token = await sessions.GetTokenAsync() ?? throw new UnauthorizedAccessException();
        if (!IsCurrent) return;
        var saved = await api.SavePreparationAsync(token, trip.Value, row.Item, ct);
        if (!IsCurrent) return;
        await RefreshAsync(ct);
        if (!saved) ErrorMessage = Text("PreparationConflict");
        else await analytics.TrackAsync("trip_preparation_updated", "preparation", tripId: trip, cancellationToken: ct);
    });

    [RelayCommand] private Task ReviewAsync() => IsCurrent ? Shell.Current.GoToAsync(nameof(TripReviewPage)) : Task.CompletedTask;
    [RelayCommand] private Task DocumentsAsync() => IsCurrent ? Shell.Current.GoToAsync(nameof(DocsPage)) : Task.CompletedTask;
    [RelayCommand]
    private Task PrepareOfflineAsync() => LoadAsync(async ct =>
    {
        if (!IsCurrent) return;
        if (sessions.IsFreeMapPreview) { await PaywallNavigation.OpenAsync(PaywallEntryPoint.Offline); return; }
        await offline.PrepareAsync(ct);
        if (IsCurrent) await RefreshAsync(ct);
    });
}

public sealed record PreparationRow(TripPreparationItemDto Item, IAsyncRelayCommand<PreparationRow> Command)
{
    public string Label => TripPreparationViewModel.Text("Preparation_" + Item.Key);
    public string Action => TripPreparationViewModel.Text(Item.Completed ? "PreparationDone" : "PreparationConfirm");
}

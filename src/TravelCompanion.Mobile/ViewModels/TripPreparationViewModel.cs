using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using TravelCompanion.Mobile.Pages;
using TravelCompanion.Mobile.Services;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Mobile.ViewModels;

public sealed partial class TripPreparationViewModel(
    AuthSessionService sessions,
    TravelCompanionApiClient api,
    MobileBootstrapStore bootstrap,
    OfflineCacheService cache,
    OfflineTripPreparationService offline,
    MobileSyncStateStore syncState,
    TripDocumentStore documents,
    TripPreparationOrganizerStore organizer,
    TripDocumentAttachmentService attachments,
    ProductAnalyticsTracker analytics) : ViewModelBase
{
    private readonly Guid? user = sessions.CurrentUserId;
    private readonly Guid? trip = sessions.CurrentTripId;
    private readonly long contextVersion = sessions.ContextVersion;
    public ObservableCollection<PreparationRow> Items { get; } = [];
    private string summary = "";
    public string Summary { get => summary; private set => SetProperty(ref summary, value); }
    private string offlineStatus = "";
    public string OfflineStatus { get => offlineStatus; private set => SetProperty(ref offlineStatus, value); }
    private bool canSave;
    public bool CanSave { get => canSave; private set => SetProperty(ref canSave, value); }
    private string importNotice = "";
    public string ImportNotice { get => importNotice; private set { SetProperty(ref importNotice, value); OnPropertyChanged(nameof(HasImportNotice)); } }
    public bool HasImportNotice => !string.IsNullOrWhiteSpace(ImportNotice);
    private bool legacyImported;
    private Task? legacyImport;
    internal Task LegacyImportCompletion => legacyImport ?? Task.CompletedTask;
    private CancellationTokenSource? legacyImportCancellation;
    public string Title => Text("PreparationTitle");
    private string destination = "";
    public string Destination { get => destination; private set => SetProperty(ref destination, value); }
    public string Introduction => Text("PreparationIntro");
    public string OfflineScope => Text("OfflineScope");
    public string LocalNotice => Text("LocalDocumentsNotice");
    public string BackDescription => Text("PaywallBack");
    public string ReviewAction => Text("ReviewTrip");
    public string DocumentsAction => Text("PreparationDocuments");
    public string OfflineAction => Text("PreparationDownload");
    private string CacheKey => $"preparation-{user:N}-{trip:N}";
    private bool IsCurrent => sessions.HasSession && sessions.CurrentUserId == user && sessions.CurrentTripId == trip
        && sessions.ContextVersion == contextVersion;
    public static string Text(string key) => LocalizationResourceManager.Instance[key];

    [RelayCommand]
    private async Task LoadPreparationAsync()
    {
        await LoadAsync(RefreshAsync);
        if (IsCurrent && CanSave) _ = TrackPreparationAsync("trip_preparation_viewed", "preparation");
        // Legacy migration is optional network work; local actions are ready before it begins.
        if (!IsCurrent || !CanSave || legacyImported || legacyImport is { IsCompleted: false }
            || Connectivity.Current.NetworkAccess != NetworkAccess.Internet) return;
        legacyImportCancellation = new();
        legacyImport = ImportLegacyAsync(legacyImportCancellation.Token);
    }

    public void CancelLegacyImport() => legacyImportCancellation?.Cancel();

    private async Task TrackPreparationAsync(string name, string source)
    {
        if (!IsCurrent) return;
        try { await analytics.TrackAsync(name, source, tripId: trip); }
        catch (Exception exception) { ClientDiagnostics.Record("preparation_analytics_failed", exception: exception); }
    }

    private async Task RefreshAsync(CancellationToken ct)
    {
        CanSave = false;
        if (!IsCurrent || trip is null)
        {
            Items.Clear();
            Summary = Text("PreparationNoSchedule");
            OfflineStatus = "";
            return;
        }
        var cachedLegacy = await cache.GetAsync<List<TripPreparationItemDto>>(CacheKey, cancellationToken: ct);
        if (!IsCurrent) return;
        var state = await organizer.GetAsync(ct);
        if (!IsCurrent) return;
        if (!state.LegacyImported && cachedLegacy is not null)
            state = await organizer.ImportLegacyOnceAsync(cachedLegacy.Value, ct);
        if (!IsCurrent) return;
        legacyImported = state.LegacyImported;
        var localDocuments = (await documents.ListAsync(ct)).Where(item => item.SourceUrl is null).ToList();
        if (!IsCurrent) return;
        Apply(state, localDocuments);

        var saved = await bootstrap.GetCachedAsync(cancellationToken: ct);
        if (!IsCurrent) return;
        Destination = saved?.Value.Schedule?.DestinationName ?? "";
        var manifest = await offline.GetAsync(ct);
        var versions = await syncState.GetCachedStateAsync(ct);
        if (!IsCurrent) return;
        var progress = TripPreparationProgress.Create(trip.Value, saved?.Value.Schedule, manifest, versions?.CatalogVersion,
            sessions.HasCuratedDocs ? versions?.DocumentsVersion : null);
        Summary = string.Format(Text("PreparationOrganizerSummary"), Items.Count(item => item.IsOrganized), Items.Count);
        OfflineStatus = Text(progress.OfflineStatusKey) + "\n"
            + string.Format(Text("PreparationResources"), progress.DownloadedResources, progress.DownloadableResources);
        CanSave = IsCurrent;
    }

    private async Task ImportLegacyAsync(CancellationToken ct)
    {
        try
        {
            var token = await sessions.GetTokenAsync();
            if (token is null || !IsCurrent || trip is null) return;
            var latest = await api.GetPreparationAsync(token, trip.Value, ct);
            ct.ThrowIfCancellationRequested();
            if (!IsCurrent) return;
            if (latest is null) { ImportNotice = Text("PreparationLegacyUnavailable"); return; }
            var state = await organizer.ImportLegacyOnceAsync(latest, ct);
            if (!IsCurrent) return;
            legacyImported = state.LegacyImported;
            await cache.SaveAsync(CacheKey, latest, ct);
            if (!IsCurrent) return;
            // Read again: an attachment or manual decision may have finished during the request.
            var localDocuments = (await documents.ListAsync(ct)).Where(item => item.SourceUrl is null).ToList();
            state = await organizer.GetAsync(ct);
            if (!IsCurrent) return;
            Apply(state, localDocuments);
            Summary = string.Format(Text("PreparationOrganizerSummary"), Items.Count(item => item.IsOrganized), Items.Count);
            ImportNotice = "";
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested || !IsCurrent) { }
        catch (Exception exception)
        {
            ClientDiagnostics.Record("preparation_legacy_import_failed", exception: exception);
            if (IsCurrent) ImportNotice = Text("PreparationLegacyUnavailable");
        }
        finally
        {
            legacyImportCancellation?.Dispose();
            legacyImportCancellation = null;
        }
    }

    private void Apply(PreparationOrganizerState state, IReadOnlyList<LocalTripDocument> localDocuments)
    {
        Items.Clear();
        foreach (var definition in TripPreparationCategoryCatalog.All)
        {
            var manual = state.Categories.Single(item => item.Key == definition.Key);
            var count = TripPreparationOrganizationPolicy.DocumentCount(definition.Category, localDocuments);
            Items.Add(new PreparationRow(definition, manual, count, AttachItemCommand, ViewDocumentsCommand, MoreItemCommand));
        }
    }

    [RelayCommand]
    private Task AttachItemAsync(PreparationRow? row) => LoadAsync(async ct =>
    {
        if (row is null || !CanSave || !IsCurrent) return;
        LocalTripDocument? saved;
        try
        {
            saved = await attachments.PickAndAttachAsync(row.Category, row.Key,
                string.Format(Text("AttachDocumentToCategory"), row.Label), ct);
        }
        catch (IOException)
        {
            ErrorMessage = Text("DocumentError") + " " + Text("DocumentLimit");
            return;
        }
        if (saved is null || !IsCurrent) return;
        StatusMessage = string.Format(Text("DocumentSavedInCategory"), row.Label);
        SemanticScreenReader.Default.Announce(StatusMessage);
        await RefreshAsync(ct);
        _ = TrackPreparationAsync("trip_preparation_updated", "document_added");
    });

    [RelayCommand]
    private Task ViewDocumentsAsync(PreparationRow? row) => IsCurrent && row is not null
        ? Shell.Current.GoToAsync(nameof(DocsPage), new ShellNavigationQueryParameters { ["DocumentCategory"] = row!.Category }) : Task.CompletedTask;

    [RelayCommand]
    private Task MoreItemAsync(PreparationRow? row) => LoadAsync(async ct =>
    {
        if (row is null || !CanSave || !IsCurrent) return;
        var outside = Text("PreparationOutsideApp");
        var notNeeded = Text("PreparationNotNeeded");
        var pending = Text("PreparationPending");
        var selected = await Shell.Current.DisplayActionSheetAsync(row.Label, Text("CommonCancel"), null,
            outside, notNeeded, pending);
        var state = selected == outside ? PreparationManualState.OutsideApp
            : selected == notNeeded ? PreparationManualState.NotNeeded
            : selected == pending ? PreparationManualState.Pending : (PreparationManualState?)null;
        if (!state.HasValue || !IsCurrent) return;
        ct.ThrowIfCancellationRequested();
        await organizer.SetManualStateAsync(row.Key, state.Value, ct);
        await RefreshAsync(ct);
        _ = TrackPreparationAsync("trip_preparation_updated", "manual_state");
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

public sealed record PreparationRow(
    TripPreparationCategoryDefinition Definition,
    PreparationCategoryState State,
    int DocumentCount,
    IAsyncRelayCommand<PreparationRow> AttachCommand,
    IAsyncRelayCommand<PreparationRow> ViewCommand,
    IAsyncRelayCommand<PreparationRow> MoreCommand)
{
    public string Key => Definition.Key;
    public LocalDocumentCategory Category => Definition.Category;
    public string Label => TripPreparationViewModel.Text("Preparation_" + Key);
    public string Description => TripPreparationViewModel.Text("PreparationDescription_" + Definition.ResourceSuffix);
    public string AddAction => TripPreparationViewModel.Text("PreparationAdd_" + Definition.ResourceSuffix);
    public string CountText => DocumentCount == 1
        ? TripPreparationViewModel.Text("PreparationOneDocument")
        : string.Format(TripPreparationViewModel.Text("PreparationDocumentCount"), DocumentCount);
    public string StateText => DocumentCount > 0 ? CountText : State.ManualState switch
    {
        PreparationManualState.OutsideApp => TripPreparationViewModel.Text("PreparationOutsideAppStatus"),
        PreparationManualState.NotNeeded => TripPreparationViewModel.Text("PreparationNotNeededStatus"),
        _ => TripPreparationViewModel.Text("PreparationPendingStatus")
    };
    public bool HasDocuments => DocumentCount > 0;
    public bool IsOrganized => TripPreparationOrganizationPolicy.IsOrganized(State.ManualState, DocumentCount);
    public string CardDescription => $"{Label}. {StateText}. {AddAction}";
    public string ViewDescription => $"{Label}. {StateText}. {ViewDocumentsAction}";
    public string ViewDocumentsAction => TripPreparationViewModel.Text("PreparationViewDocuments");
    public string MenuDescription => string.Format(TripPreparationViewModel.Text("PreparationOptionsNamed"), Label);
}

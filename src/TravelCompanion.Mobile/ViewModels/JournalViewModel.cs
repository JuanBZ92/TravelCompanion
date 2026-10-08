using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TravelCompanion.Mobile.Pages;
using TravelCompanion.Mobile.Services;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Mobile.ViewModels;

public sealed partial class JournalThumbnail(JournalMemory memory, int index) : ObservableObject
{
    public JournalMemory Memory { get; } = memory;
    public int Index { get; } = index;
    public Guid PhotoId => Memory.Images[Index].Id;
    [ObservableProperty] private ImageSource source = ImageSource.FromFile("journal_photo.svg");
    public string Description => JournalText.Format("JournalPhotoNumber", Index + 1, Memory.Images.Length);
}
public sealed record JournalRow(JournalMemory Memory, IReadOnlyList<JournalThumbnail> Photos, bool HasActivity)
{
    public string Title => JournalText.DisplayTitle(Memory);
    public string Day => Memory.Date.ToString("dd");
    public string Month => Memory.Date.ToString("MMM").ToUpper(System.Globalization.CultureInfo.CurrentUICulture);
    public string Year => Memory.Date.ToString("yyyy");
    public string Notes => Memory.Text;
    public bool HasNotes => !string.IsNullOrWhiteSpace(Notes);
    public string Place => Memory.City;
    public bool HasPlace => !string.IsNullOrWhiteSpace(Memory.Title)
        && !string.IsNullOrWhiteSpace(Place)
        && !string.Equals(Memory.Title.Trim(), Place.Trim(), StringComparison.CurrentCultureIgnoreCase);
    public string Status => Memory.Status;
    public bool HasStatus => Status.Length > 0;
    public bool HasPhotos => Photos.Count > 0;
    public bool HasNoPhotos => !HasPhotos;
    public JournalThumbnail? Cover => Photos.FirstOrDefault();
    public JournalThumbnail? SecondPhoto => Photos.ElementAtOrDefault(1);
    public JournalThumbnail? ThirdPhoto => Photos.ElementAtOrDefault(2);
    public bool HasSecondPhoto => SecondPhoto is not null;
    public bool HasThirdPhoto => ThirdPhoto is not null;
    public string PhotoCount => JournalText.Photos(Memory.Images.Length);
    public string OpenPhotosDescription => $"{JournalText.Get("JournalOpenPhotos")} · {PhotoCount} · {Title}";
    public string OpenDescription => JournalText.Format("JournalRead", Title);
}
public sealed partial class JournalViewModel(MobileBootstrapStore bootstrapStore, AuthSessionService sessions,
    JournalStore store) : ViewModelBase, ISessionStateResettable
{
    private JournalScope? renderedScope;
    private int renderVersion;
    private bool opening;
    private int firstVisible = -1;
    private int lastVisible = -1;
    private CancellationTokenSource? thumbnailCancellation;
    private readonly JournalThumbnailCache<ImageSource> thumbnailCache = new();
    public ObservableCollection<JournalRow> Entries { get; } = [];
    public string TripTitle { get; private set; } = "";
    public string Heading => JournalText.Get("JournalHeading");
    public string WriteAction => JournalText.Get("JournalWrite");
    public string MenuAction => JournalText.Get("JournalOptions");
    public string DraftAction => JournalText.Format("JournalContinueDraft", Drafts.Count);
    public bool HasDrafts => Drafts.Count > 0;
    public string EmptyTitle => JournalText.Get("JournalEmptyTitle");
    public string ChooseTripAction => JournalText.Get("JournalChooseTrip");
    public string Summary => Entries.Count == 0 ? JournalText.Get("JournalInvitation")
        : $"{JournalText.Memories(Entries.Count)} · {JournalText.Photos(Entries.Sum(x => x.Memory.Images.Length))}";
    public bool CanExport => DeviceInfo.Platform == DevicePlatform.Android && Entries.Count > 0;
    public bool HasTrip => sessions.HasSession && sessions.CurrentTripId.HasValue;
    public bool CanAddMemory => HasTrip;
    public bool NeedsTrip => !HasTrip;
    public string EmptyMessage => JournalText.Get(HasTrip ? "JournalPrompt" : "JournalNeedsTrip");
    public IReadOnlyList<ScheduleItemDto> Activities { get; private set; } = [];
    public IReadOnlyList<JournalMemory> Memories { get; private set; } = [];
    public IReadOnlyList<JournalDraft> Drafts { get; private set; } = [];
    public Task LoadAsync() => LoadJournalAsync();
    [RelayCommand] private Task RefreshAsync() => LoadJournalAsync(waitForSync: true);
    private async Task LoadJournalAsync(bool waitForSync = false)
    {
        JournalScope? localScope = null;
        var cancellationToken = CancellationToken.None;
        var needsSchedule = false;
        await base.LoadAsync(async ct =>
        {
            if (!HasTrip) { Clear(); return; }
            var scope = store.Scope();
            var cached = await bootstrapStore.GetCachedAsync(cancellationToken: ct);
            if (!store.IsCurrent(scope)) return;
            var schedule = cached?.Value.Schedule;
            Activities = schedule?.TripId == scope.TripId ? schedule.Items : [];
            TripTitle = schedule?.DestinationName ?? JournalText.Get("JournalMyTrip");
            var version = Interlocked.Increment(ref renderVersion);
            await ApplyAsync(scope, await store.LoadAsync(scope, Activities, false, ct), ct, version);
            localScope = scope;
            cancellationToken = ct;
            needsSchedule = schedule is null;
        });
        if (localScope is not { } loadedScope || cancellationToken.IsCancellationRequested
            || Connectivity.Current.NetworkAccess != NetworkAccess.Internet) return;
        var refresh = SyncRemoteAsync(loadedScope, needsSchedule, waitForSync, cancellationToken);
        if (waitForSync) await refresh;
    }
    private async Task SyncRemoteAsync(JournalScope scope, bool needsSchedule, bool userRequested, CancellationToken ct)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(12));
            if (needsSchedule)
            {
                var token = await sessions.GetTokenAsync();
                if (!store.IsCurrent(scope) || ct.IsCancellationRequested) return;
                if (!string.IsNullOrEmpty(token))
                {
                    var fresh = (await bootstrapStore.RefreshAsync(token, cancellationToken: timeout.Token))?.Schedule;
                    if (!store.IsCurrent(scope) || ct.IsCancellationRequested) return;
                    if (fresh?.TripId == scope.TripId) { Activities = fresh.Items; TripTitle = fresh.DestinationName; }
                }
            }
            // The store applies its own HTTP deadline. Keep its local writes and
            // rendering on the page token so a slow response cannot cancel them.
            var syncFailed = false;
            var version = Interlocked.Increment(ref renderVersion);
            var memories = await store.LoadAsync(scope, Activities, true, ct, () => syncFailed = true);
            await ApplyAsync(scope, memories, ct, version);
            if (store.IsCurrent(scope)) ErrorMessage = userRequested && syncFailed
                ? JournalText.Get("JournalSyncUnavailable") : null;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception) { if (userRequested && store.IsCurrent(scope)) ErrorMessage = JournalText.Get("JournalSyncUnavailable"); }
    }
    public async Task RefreshLocalSynchronizationAsync(JournalScope scope, CancellationToken ct)
    {
        if (renderedScope != scope || !store.IsCurrent(scope)) return;
        var version = Interlocked.Increment(ref renderVersion);
        var memories = await store.LoadAsync(scope, [], false, ct);
        ct.ThrowIfCancellationRequested();
        if (renderedScope != scope || !store.IsCurrent(scope)) return;
        await ApplyAsync(scope, memories, ct, version);
    }
    private async Task ApplyAsync(JournalScope scope, IReadOnlyList<JournalMemory> memories, CancellationToken ct,
        int version)
    {
        var drafts = await store.DraftsAsync(scope, ct);
        ct.ThrowIfCancellationRequested();
        if (!store.IsCurrent(scope) || version != Volatile.Read(ref renderVersion)) return;
        var rows = new List<JournalRow>();
        var activityIds = Activities.Select(x => x.Id).ToHashSet();
        var sameScope = renderedScope == scope;
        var oldRows = sameScope ? Entries.ToDictionary(x => x.Memory.Key) : new Dictionary<string, JournalRow>();
        foreach (var memory in memories.Where(x => !x.IsDraft && !x.Deleted && x.HasContent))
        {
            var hasActivity = !memory.IsFree && activityIds.Contains(memory.Id);
            if (oldRows.TryGetValue(memory.Key, out var previous) && previous.HasActivity == hasActivity
                && JournalEntries.SameContent(previous.Memory, memory))
            {
                rows.Add(previous);
                continue;
            }
            var photos = new List<JournalThumbnail>();
            var indices = JournalEntries.PhotoPreviewIndices(memory);
            foreach (var i in indices)
            {
                ct.ThrowIfCancellationRequested();
                var photo = new JournalThumbnail(memory, i);
                photos.Add(photo);
            }
            rows.Add(new(memory, photos, hasActivity));
        }
        ct.ThrowIfCancellationRequested();
        if (!store.IsCurrent(scope)) return;
        if (!sameScope)
        {
            StopThumbnails(); thumbnailCache.Clear(); Entries.Clear();
            firstVisible = -1; lastVisible = -1;
        }
        renderedScope = scope;
        Memories = memories; Drafts = drafts;
        JournalEntries.ReconcileRows(Entries, rows, x => x.Memory.Key);
        if (firstVisible < 0 || firstVisible >= Entries.Count) { firstVisible = 0; lastVisible = 4; }
        NotifyContentChanged();
        LoadVisibleThumbnails();
    }

    public void SetVisibleRange(int first, int last)
    {
        if (first < 0 || last < first || (first == firstVisible && last == lastVisible)) return;
        firstVisible = first; lastVisible = last;
        LoadVisibleThumbnails();
    }

    public new void CancelLoading() { base.CancelLoading(); StopThumbnails(); }

    private void StopThumbnails()
    {
        thumbnailCancellation?.Cancel(); thumbnailCancellation?.Dispose(); thumbnailCancellation = null;
    }

    private void LoadVisibleThumbnails()
    {
        StopThumbnails();
        if (renderedScope is not { } scope || !store.IsCurrent(scope)) return;
        var thumbnails = JournalEntries.VisibleRows(Entries.Count, firstVisible, lastVisible)
            .SelectMany(i => Entries[i].Photos).ToArray();
        thumbnailCancellation = new CancellationTokenSource();
        _ = LoadThumbnailsAsync(scope, thumbnails, thumbnailCancellation.Token);
    }

    private async Task LoadThumbnailsAsync(JournalScope scope, IReadOnlyList<JournalThumbnail> thumbnails, CancellationToken ct)
    {
        try
        {
            var visible = thumbnails.Select(x => x.PhotoId).ToHashSet();
            foreach (var thumbnail in thumbnails)
            {
                ct.ThrowIfCancellationRequested();
                if (!store.IsCurrent(scope) || renderedScope != scope) return;
                if (thumbnailCache.TryGet(thumbnail.PhotoId, out var cached)) { thumbnail.Source = cached!; continue; }
                try
                {
                    var bytes = await store.PhotoAsync(scope, thumbnail.PhotoId, true, ct);
                    ct.ThrowIfCancellationRequested();
                    if (bytes is not { Length: > 0 } || !store.IsCurrent(scope) || renderedScope != scope) continue;
                    var source = ImageSource.FromStream(() => new MemoryStream(bytes));
                    thumbnailCache.Add(thumbnail.PhotoId, source); thumbnail.Source = source;
                    TrimThumbnails(visible);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception) { /* A missing/damaged photo must not stop later thumbnails. */ }
            }
            TrimThumbnails(visible);
        }
        catch (OperationCanceledException) { }
        catch (Exception) { /* Missing or damaged local photos leave the placeholder visible. */ }
    }
    private void TrimThumbnails(IReadOnlySet<Guid> visible)
    {
        var evicted = thumbnailCache.Trim(visible).ToHashSet();
        if (evicted.Count == 0) return;
        foreach (var thumbnail in Entries.SelectMany(x => x.Photos).Where(x => evicted.Contains(x.PhotoId)))
            thumbnail.Source = ImageSource.FromFile("journal_photo.svg");
    }
    private void NotifyContentChanged()
    {
        foreach (var name in new[] { nameof(TripTitle), nameof(Summary), nameof(CanExport), nameof(HasTrip), nameof(NeedsTrip),
            nameof(CanAddMemory), nameof(EmptyMessage), nameof(DraftAction), nameof(HasDrafts), nameof(Heading), nameof(WriteAction),
            nameof(MenuAction), nameof(EmptyTitle), nameof(ChooseTripAction) }) OnPropertyChanged(name);
        AddMemoryCommand.NotifyCanExecuteChanged();
    }
    [RelayCommand] private Task OpenAccountAsync() => sessions.IsFreeMapPreview
        ? BuilderSetupNavigation.OpenAsync() : Shell.Current.GoToAsync("//main/account");
    [RelayCommand] private async Task OpenPhotoAsync(JournalThumbnail? photo)
    {
        if (opening || photo is null || renderedScope is not { } scope || !store.IsCurrent(scope)
            || !Entries.Any(row => row.Photos.Any(thumbnail => ReferenceEquals(thumbnail, photo)))) return;
        opening = true;
        try
        {
            CancelLoading();
            await Shell.Current.Navigation.PushModalAsync(new JournalPhotoPage(scope, photo.Memory, photo.Index));
        }
        finally { opening = false; }
    }
    [RelayCommand] private async Task OpenEntryAsync(JournalRow? row)
    {
        if (opening || row is null || renderedScope is not { } scope || !store.IsCurrent(scope)
            || !Entries.Any(entry => ReferenceEquals(entry, row))) return;
        opening = true;
        try
        {
            CancelLoading();
            await Shell.Current.Navigation.PushModalAsync(new JournalReadingPage(scope, row.Memory,
                row.Memory.IsFree ? null : Activities.FirstOrDefault(x => x.Id == row.Memory.Id)));
        }
        finally { opening = false; }
    }
    [RelayCommand(CanExecute = nameof(CanAddMemory))] private Task AddMemoryAsync()
    {
        CancelLoading();
        return Shell.Current.Navigation.PushModalAsync(new JournalMemoryPage(store.Scope(),
            JournalMemory.NewFree(sessions.CurrentTripId!.Value, DateOnly.FromDateTime(DateTime.Today)), null));
    }
    [RelayCommand] private async Task ContinueDraftAsync()
    {
        if (Drafts.Count == 0) return;
        var scope = store.Scope();
        var draft = Drafts[0];
        if (Drafts.Count > 1)
        {
            var labels = Drafts.Select((x, i) => $"{i + 1}. {JournalText.Title(x.Memory)}").ToArray();
            var choice = await Shell.Current.DisplayActionSheetAsync(JournalText.Get("JournalDrafts"), JournalText.Get("JournalCancel"), null, labels);
            var index = Array.IndexOf(labels, choice); if (index < 0) return; draft = Drafts[index];
        }
        if (!store.IsCurrent(scope)) return;
        CancelLoading();
        await Shell.Current.Navigation.PushModalAsync(new JournalMemoryPage(scope, draft.Memory, null, draft: draft));
    }
    [RelayCommand] private async Task MenuAsync()
    {
        if (!HasTrip) return;
        var scope = store.Scope();
        var activity = JournalText.Get("JournalChooseActivity"); var export = JournalText.Get("JournalExport");
        var options = new List<string>(); if (Activities.Count > 0) options.Add(activity); if (CanExport) options.Add(export);
        if (options.Count == 0) return;
        var choice = await Shell.Current.DisplayActionSheetAsync(MenuAction, JournalText.Get("JournalCancel"), null, options.ToArray());
        if (!store.IsCurrent(scope)) return;
        if (choice == activity) await Shell.Current.Navigation.PushModalAsync(new JournalActivityPickerPage(Activities, Memories));
        if (choice == export) await Shell.Current.Navigation.PushModalAsync(new JournalExportPage(scope, TripTitle, Memories.Where(x => !x.IsDraft && !x.Deleted && x.HasContent).ToArray()));
    }
    private void Clear()
    {
        Interlocked.Increment(ref renderVersion);
        StopThumbnails(); thumbnailCache.Clear(); firstVisible = -1; lastVisible = -1;
        renderedScope = null; Entries.Clear(); Activities = []; Memories = []; Drafts = []; TripTitle = ""; NotifyContentChanged();
    }
    public void ResetForNewSession() { ResetLoadState(); Clear(); }
}

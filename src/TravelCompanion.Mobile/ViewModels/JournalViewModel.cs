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
    public string PhotoCount => JournalText.Photos(Memory.Images.Length);
    public string OpenDescription => JournalText.Format("JournalRead", Title);
}
public sealed partial class JournalViewModel(MobileBootstrapStore bootstrapStore, AuthSessionService sessions,
    JournalStore store) : ViewModelBase, ISessionStateResettable
{
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
            await ApplyAsync(scope, await store.LoadAsync(scope, Activities, false, ct), ct);
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
            var memories = await store.LoadAsync(scope, Activities, true, ct, () => syncFailed = true);
            await ApplyAsync(scope, memories, ct);
            if (store.IsCurrent(scope)) ErrorMessage = userRequested && syncFailed
                ? JournalText.Get("JournalSyncUnavailable") : null;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception) { if (userRequested && store.IsCurrent(scope)) ErrorMessage = JournalText.Get("JournalSyncUnavailable"); }
    }
    private async Task ApplyAsync(JournalScope scope, IReadOnlyList<JournalMemory> memories, CancellationToken ct)
    {
        var drafts = await store.DraftsAsync(scope, ct);
        var rows = new List<JournalRow>();
        var activityIds = Activities.Select(x => x.Id).ToHashSet();
        var thumbnails = new List<(JournalThumbnail Thumbnail, Guid PhotoId)>();
        foreach (var memory in memories.Where(x => !x.IsDraft && !x.Deleted && x.HasContent))
        {
            var photos = new List<JournalThumbnail>();
            var indices = Enumerable.Range(0, memory.Images.Length).OrderByDescending(i => memory.Images[i].Id == memory.CoverId).Take(3);
            foreach (var i in indices)
            {
                ct.ThrowIfCancellationRequested();
                var photo = new JournalThumbnail(memory, i);
                photos.Add(photo);
                thumbnails.Add((photo, memory.Images[i].Id));
            }
            rows.Add(new(memory, photos, !memory.IsFree && activityIds.Contains(memory.Id)));
        }
        ct.ThrowIfCancellationRequested();
        if (!store.IsCurrent(scope)) return;
        Memories = memories; Drafts = drafts;
        Entries.Clear(); foreach (var row in rows) Entries.Add(row);
        NotifyContentChanged();
        _ = LoadThumbnailsAsync(scope, thumbnails, ct);
    }
    private async Task LoadThumbnailsAsync(JournalScope scope,
        IEnumerable<(JournalThumbnail Thumbnail, Guid PhotoId)> thumbnails, CancellationToken ct)
    {
        try
        {
            foreach (var (thumbnail, id) in thumbnails)
            {
                ct.ThrowIfCancellationRequested();
                var bytes = await store.PhotoAsync(scope, id, true);
                if (bytes is { Length: > 0 } && store.IsCurrent(scope))
                    thumbnail.Source = ImageSource.FromStream(() => new MemoryStream(bytes));
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception) { /* Missing or damaged local photos leave the placeholder visible. */ }
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
    [RelayCommand] private Task OpenPhotoAsync(JournalThumbnail? photo) => photo is null ? Task.CompletedTask :
        Shell.Current.Navigation.PushModalAsync(new JournalPhotoPage(store.Scope(), photo.Memory, photo.Index));
    [RelayCommand] private Task OpenEntryAsync(JournalRow? row)
    {
        if (row is null) return Task.CompletedTask;
        CancelLoading();
        return Shell.Current.Navigation.PushModalAsync(new JournalReadingPage(store.Scope(), row.Memory,
            row.Memory.IsFree ? null : Activities.FirstOrDefault(x => x.Id == row.Memory.Id)));
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
    private void Clear() { Entries.Clear(); Activities = []; Memories = []; Drafts = []; TripTitle = ""; NotifyContentChanged(); }
    public void ResetForNewSession() { ResetLoadState(); Clear(); }
}

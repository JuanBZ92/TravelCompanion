using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using TravelCompanion.Mobile.Pages;
using TravelCompanion.Mobile.Services;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Mobile.ViewModels;

public sealed record JournalThumbnail(JournalMemory Memory, int Index, ImageSource? Source)
{
    public string Description => JournalText.Format("JournalPhotoNumber", Index + 1, Memory.Images.Length);
}
public sealed record JournalRow(JournalMemory Memory, IReadOnlyList<JournalThumbnail> Photos, bool HasActivity)
{
    public string Title => JournalText.Title(Memory);
    public string Notes => Memory.Text;
    public bool HasNotes => !string.IsNullOrWhiteSpace(Notes);
    public string Place => Memory.City;
    public bool HasPlace => !string.IsNullOrWhiteSpace(Place);
    public string Status => Memory.Status;
    public bool HasStatus => Status.Length > 0;
    public bool HasPhotos => Photos.Count > 0;
    public JournalThumbnail? Cover => Photos.FirstOrDefault();
    public IReadOnlyList<JournalThumbnail> ExtraPhotos => Photos.Skip(1).ToArray();
    public string PhotoCount => JournalText.Photos(Memory.Images.Length);
    public string ReadAction => JournalText.Get("JournalReadAction");
    public string OpenDescription => JournalText.Format("JournalRead", Title);
}
public sealed class JournalDayGroup(DateOnly date, IEnumerable<JournalRow> rows) : ObservableCollection<JournalRow>(rows)
{
    public string Day => date.ToString("dd");
    public string Month => date.ToString("MMMM yyyy");
    public string Cities => string.Join(" · ", this.Select(x => x.Place).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct());
}
public sealed partial class JournalViewModel(MobileBootstrapStore bootstrapStore, AuthSessionService sessions,
    JournalStore store) : ViewModelBase, ISessionStateResettable
{
    public ObservableCollection<JournalRow> Entries { get; } = [];
    public ObservableCollection<JournalDayGroup> Groups { get; } = [];
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
    [RelayCommand] private Task RefreshAsync() => LoadJournalAsync();
    private Task LoadJournalAsync() => base.LoadAsync(async ct =>
    {
        if (!HasTrip) { Clear(); return; }
        var scope = store.Scope();
        var cached = await bootstrapStore.GetCachedAsync(cancellationToken: ct);
        if (!store.IsCurrent(scope)) return;
        var schedule = cached?.Value.Schedule;
        Activities = schedule?.TripId == scope.TripId ? schedule.Items : [];
        TripTitle = schedule?.DestinationName ?? JournalText.Get("JournalMyTrip");
        await ApplyAsync(scope, await store.LoadAsync(scope, Activities, false, ct), ct);
        if (Connectivity.Current.NetworkAccess == NetworkAccess.Internet)
        {
            try
            {
                // Local writing is already available. Load activity choices when the catalog cache is absent.
                if (schedule is null)
                {
                    var token = await sessions.GetTokenAsync();
                    if (!string.IsNullOrEmpty(token))
                    {
                        var fresh = (await bootstrapStore.RefreshAsync(token, cancellationToken: ct))?.Schedule;
                        if (!store.IsCurrent(scope)) return;
                        if (fresh?.TripId == scope.TripId) { Activities = fresh.Items; TripTitle = fresh.DestinationName; }
                    }
                }
                await ApplyAsync(scope, await store.LoadAsync(scope, Activities, true, ct), ct);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception) { ErrorMessage = JournalText.Get("JournalSyncUnavailable"); }
        }
    });
    private async Task ApplyAsync(JournalScope scope, IReadOnlyList<JournalMemory> memories, CancellationToken ct)
    {
        var drafts = await store.DraftsAsync(scope, ct);
        var rows = new List<JournalRow>();
        foreach (var memory in memories.Where(x => !x.IsDraft && !x.Deleted && x.HasContent))
        {
            var photos = new List<JournalThumbnail>();
            var indices = Enumerable.Range(0, memory.Images.Length).OrderByDescending(i => memory.Images[i].Id == memory.CoverId).Take(3);
            foreach (var i in indices)
            {
                ct.ThrowIfCancellationRequested();
                var bytes = await store.PhotoAsync(scope, memory.Images[i].Id, true);
                photos.Add(new(memory, i, bytes is null ? ImageSource.FromFile("journal_photo.svg")
                    : ImageSource.FromStream(() => new MemoryStream(bytes))));
            }
            rows.Add(new(memory, photos, !memory.IsFree && Activities.Any(x => x.Id == memory.Id)));
        }
        if (!store.IsCurrent(scope)) return;
        Memories = memories; Drafts = drafts;
        Entries.Clear(); foreach (var row in rows) Entries.Add(row);
        Groups.Clear();
        foreach (var group in rows.GroupBy(x => x.Memory.Date).OrderBy(x => x.Key)) Groups.Add(new(group.Key, group));
        NotifyContentChanged();
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
    [RelayCommand] private Task OpenEntryAsync(JournalRow? row) => row is null ? Task.CompletedTask :
        Shell.Current.Navigation.PushModalAsync(new JournalReadingPage(store.Scope(), row.Memory,
            row.Memory.IsFree ? null : Activities.FirstOrDefault(x => x.Id == row.Memory.Id)));
    [RelayCommand(CanExecute = nameof(CanAddMemory))] private Task AddMemoryAsync() =>
        Shell.Current.Navigation.PushModalAsync(new JournalMemoryPage(store.Scope(), JournalMemory.NewFree(sessions.CurrentTripId!.Value, DateOnly.FromDateTime(DateTime.Today)), null));
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
    private void Clear() { Entries.Clear(); Groups.Clear(); Activities = []; Memories = []; Drafts = []; TripTitle = ""; NotifyContentChanged(); }
    public void ResetForNewSession() { ResetLoadState(); Clear(); }
}

using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using TravelCompanion.Mobile.Pages;
using TravelCompanion.Mobile.Services;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Mobile.ViewModels;

public sealed record JournalRow(JournalMemory Memory, ImageSource? Cover, bool HasActivity)
{
    public string Title => Memory.Note.Title;
    public string Notes => Memory.Text;
    public string Context => $"{Memory.Note.Date:d MMMM} · {Memory.Note.City}";
    public string Status => Memory.Status;
    public bool HasStatus => Status.Length > 0;
    public bool HasCover => Cover is not null;
    public string PhotoCount => Memory.Images.Length == 0 ? "" : $"{Memory.Images.Length} fotos";
}

public sealed class JournalDayGroup(string title, IEnumerable<JournalRow> rows) : ObservableCollection<JournalRow>(rows)
{
    public string Title { get; } = title;
}

public sealed partial class JournalViewModel(MobileBootstrapStore bootstrapStore, AuthSessionService sessions,
    JournalStore store) : ViewModelBase, ISessionStateResettable
{
    public ObservableCollection<JournalRow> Entries { get; } = [];
    public ObservableCollection<JournalDayGroup> Groups { get; } = [];
    public string TripTitle { get; private set; } = "Tu viaje, en recuerdos";
    public string Summary => Entries.Count == 0 ? "Los lugares pasan. Tus recuerdos quedan." : $"{Entries.Count} recuerdos · {Entries.Sum(x => x.Memory.Images.Length)} fotos";
    public bool CanExport => DeviceInfo.Platform == DevicePlatform.Android && Entries.Count > 0;
    public IReadOnlyList<ScheduleItemDto> Activities { get; private set; } = [];
    public IReadOnlyList<JournalMemory> Memories { get; private set; } = [];
    public Task LoadAsync() => LoadJournalAsync(true);
    [RelayCommand] private Task RefreshAsync() => LoadJournalAsync(true);
    private Task LoadJournalAsync(bool refresh) => base.LoadAsync(async ct =>
    {
        var scope = store.Scope();
        var cached = await bootstrapStore.GetCachedAsync(cancellationToken: ct);
        var schedule = cached?.Value.Schedule;
        if (schedule is null)
        {
            var token = await sessions.GetTokenAsync();
            if (!string.IsNullOrEmpty(token)) schedule = (await bootstrapStore.RefreshAsync(token, cancellationToken: ct))?.Schedule;
        }
        if (!store.IsCurrent(scope)) return;
        Activities = schedule?.TripId == scope.TripId ? schedule.Items : [];
        TripTitle = schedule?.DestinationName ?? "Mi viaje";
        var memories = await store.LoadAsync(scope, Activities,
            refresh && Connectivity.Current.NetworkAccess == NetworkAccess.Internet, ct);
        if (!store.IsCurrent(scope)) return;
        Memories = memories;
        var rows = new List<JournalRow>();
        foreach (var memory in memories.Where(x => x.HasContent))
        {
            byte[]? bytes = memory.CoverId is { } id ? await store.PhotoAsync(scope, id, true) : null;
            rows.Add(new(memory, bytes is null ? null : ImageSource.FromStream(() => new MemoryStream(bytes)), Activities.Any(x => x.Id == memory.Note.ActivityId)));
        }
        if (!store.IsCurrent(scope)) return;
        Entries.Clear();
        foreach (var row in rows) Entries.Add(row);
        Groups.Clear();
        foreach (var group in rows.GroupBy(x => (x.Memory.Note.Date, x.Memory.Note.City)))
            Groups.Add(new($"{group.Key.Date:d MMMM} · {group.Key.City}", group));
        OnPropertyChanged(nameof(TripTitle)); OnPropertyChanged(nameof(Summary)); OnPropertyChanged(nameof(CanExport));
    });
    [RelayCommand] private Task OpenEntryAsync(JournalRow? row) => row is null ? Task.CompletedTask :
        Shell.Current.Navigation.PushModalAsync(new JournalMemoryPage(store.Scope(), row.Memory,
            Activities.FirstOrDefault(x => x.Id == row.Memory.Note.ActivityId)));
    [RelayCommand] private Task AddMemoryAsync() => Shell.Current.Navigation.PushModalAsync(new JournalActivityPickerPage(Activities, Memories));
    [RelayCommand] private Task AddPhotoAsync(JournalRow? row) => row is null ? Task.CompletedTask :
        Shell.Current.Navigation.PushModalAsync(new JournalMemoryPage(store.Scope(), row.Memory,
            Activities.FirstOrDefault(x => x.Id == row.Memory.Note.ActivityId), startWithPhotos: true));
    [RelayCommand] private Task OpenActivityAsync(JournalRow? row) => row is not null
        && Activities.FirstOrDefault(x => x.Id == row.Memory.Note.ActivityId) is { } item
        ? Shell.Current.GoToAsync(nameof(ScheduleItemDetailPage), new Dictionary<string, object> { ["ScheduleItem"] = item })
        : Task.CompletedTask;
    [RelayCommand] private Task ExportAsync() => Shell.Current.Navigation.PushModalAsync(new JournalExportPage(store.Scope(), TripTitle,
        Memories.Where(x => x.HasContent).ToArray()));
    public void ResetForNewSession()
    {
        ResetLoadState(); Entries.Clear(); Groups.Clear(); Activities = []; Memories = []; TripTitle = "Tu viaje, en recuerdos";
    }
}

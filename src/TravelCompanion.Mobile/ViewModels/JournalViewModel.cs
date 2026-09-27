using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using TravelCompanion.Mobile.Pages;
using TravelCompanion.Mobile.Services;

namespace TravelCompanion.Mobile.ViewModels;

public sealed partial class JournalViewModel(MobileBootstrapStore bootstrapStore, AuthSessionService sessionService)
    : ViewModelBase, ISessionStateResettable
{
    public ObservableCollection<JournalEntry> Entries { get; } = [];
    public string Introduction => LocalizationResourceManager.Instance["JournalIntroduction"];
    public string EmptyText => LocalizationResourceManager.Instance["JournalEmpty"];

    public Task LoadAsync() => LoadJournalAsync(false);

    [RelayCommand]
    private Task RefreshAsync() => LoadJournalAsync(true);

    private Task LoadJournalAsync(bool refresh) => base.LoadAsync(async cancellationToken =>
    {
        var context = sessionService.ContextVersion;
        var tripId = sessionService.CurrentTripId;
        var cached = await bootstrapStore.GetCachedAsync(cancellationToken: cancellationToken);
        var snapshot = cached?.Value;
        if (refresh || snapshot is null)
        {
            var token = await sessionService.GetTokenAsync();
            if (!string.IsNullOrWhiteSpace(token))
            {
                var latest = await bootstrapStore.RefreshAsync(token, cancellationToken: cancellationToken);
                snapshot = latest ?? snapshot;
                if (latest is null) ErrorMessage = LocalizationResourceManager.Instance["JournalRefreshError"];
            }
        }
        cancellationToken.ThrowIfCancellationRequested();
        if (context != sessionService.ContextVersion || tripId != sessionService.CurrentTripId) return;
        Entries.Clear();
        if (snapshot?.Schedule is { } schedule && schedule.TripId == tripId)
            foreach (var entry in JournalEntries.Build(schedule.Items)) Entries.Add(entry);
        OnPropertyChanged(nameof(Introduction));
        OnPropertyChanged(nameof(EmptyText));
    });

    [RelayCommand]
    private Task OpenEntryAsync(JournalEntry? entry) => entry is null ? Task.CompletedTask
        : Shell.Current.GoToAsync(nameof(ScheduleItemDetailPage),
            new Dictionary<string, object> { ["ScheduleItem"] = entry.Item });

    public void ResetForNewSession()
    {
        ResetLoadState();
        Entries.Clear();
    }
}

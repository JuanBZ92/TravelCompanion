using CommunityToolkit.Mvvm.Input;
using TravelCompanion.Mobile.Services;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Mobile.ViewModels;

public sealed partial class ScheduleItemDetailViewModel(
    TravelCompanionApiClient apiClient,
    AuthSessionService sessionService,
    MobileBootstrapStore bootstrapStore) : ViewModelBase, IQueryAttributable
{
    private ScheduleItemDto? _scheduleItem;
    private string _curatedNotes = string.Empty;
    public string CuratedNotes
    {
        get => _curatedNotes;
        private set
        {
            if (SetProperty(ref _curatedNotes, value)) OnPropertyChanged(nameof(HasCuratedNotes));
        }
    }
    public bool HasCuratedNotes => !string.IsNullOrWhiteSpace(CuratedNotes);

    public ScheduleItemDto? ScheduleItem
    {
        get => _scheduleItem;
        set
        {
            if (SetProperty(ref _scheduleItem, value))
            {
                OnPropertyChanged(nameof(ReservationReferenceLabel));
                OnPropertyChanged(nameof(ReservationTimeLabel));
                OnPropertyChanged(nameof(AddressText));
                OnPropertyChanged(nameof(NotesText));
                OnPropertyChanged(nameof(HasNotes));
                OnPropertyChanged(nameof(HasAddress));
            }
        }
    }

    public string ReservationReferenceLabel
    {
        get
        {
            if (ScheduleItem is null)
            {
                return string.Empty;
            }

            return string.IsNullOrWhiteSpace(ScheduleItem.ConfirmationCode)
                ? ScheduleItem.LocationName
                : $"Codigo {ScheduleItem.ConfirmationCode}";
        }
    }

    public string ReservationTimeLabel
    {
        get
        {
            if (ScheduleItem is null)
            {
                return string.Empty;
            }

            var date = ScheduleItem.Date.ToString("dddd d MMMM");
            return ScheduleItem.HasEnd
                ? $"{date} · {ScheduleItem.StartsAt:HH\\:mm} - {ScheduleItem.EndDisplay.Replace("Hasta: ", string.Empty).Replace("Horario de llegada: ", string.Empty)}"
                : $"{date} · {ScheduleItem.StartsAt:HH\\:mm}";
        }
    }

    public string AddressText => ScheduleItem is null || string.IsNullOrWhiteSpace(ScheduleItem.Address)
        ? "Direccion no disponible"
        : ScheduleItem.Address;

    public string NotesText => ScheduleItem?.Notes ?? string.Empty;
    public bool HasNotes => !string.IsNullOrWhiteSpace(NotesText);
    public bool HasAddress => ScheduleItem is not null && !string.IsNullOrWhiteSpace(ScheduleItem.Address);

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("ScheduleItem", out var value) && value is ScheduleItemDto item)
        {
            ScheduleItem = item;
            CuratedNotes = string.Empty;
            _ = LoadCuratedNotesAsync(item);
        }
    }

    private async Task LoadCuratedNotesAsync(ScheduleItemDto item)
    {
        if (item.RecommendationId is not { } recommendationId) return;
        var context = sessionService.ContextVersion;
        bool IsCurrent() => ReferenceEquals(ScheduleItem, item) && sessionService.HasSession
            && context == sessionService.ContextVersion;
        try
        {
            var cached = await bootstrapStore.GetCachedAsync();
            if (!IsCurrent()) return;
            CuratedNotes = cached?.Value.Recommendations.FirstOrDefault(recommendation => recommendation.Id == recommendationId)?.DisplayDescription ?? string.Empty;
            var token = await sessionService.GetTokenAsync();
            if (string.IsNullOrWhiteSpace(token) || !IsCurrent()) return;
            var recommendation = await apiClient.GetMobileRecommendationDetailAsync(token, recommendationId);
            if (IsCurrent() && recommendation is not null) CuratedNotes = recommendation.DisplayDescription;
        }
        catch (Exception)
        {
            // Keep cached editorial notes and the user's notes readable offline.
        }
    }

    [RelayCommand]
    private async Task OpenMapsAsync()
    {
        if (ScheduleItem is null)
        {
            return;
        }

        await GoogleMapsLauncher.OpenAsync($"{ScheduleItem.LocationName}, {ScheduleItem.Address}", ScheduleItem.ProviderPlaceId);
    }
}

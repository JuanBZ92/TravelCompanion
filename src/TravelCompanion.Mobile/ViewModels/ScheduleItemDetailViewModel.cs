using CommunityToolkit.Mvvm.Input;
using TravelCompanion.Mobile.Services;
using TravelCompanion.Shared;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Mobile.ViewModels;

public sealed partial class ScheduleItemDetailViewModel(
    TravelCompanionApiClient apiClient,
    AuthSessionService sessionService,
    MobileBootstrapStore bootstrapStore,
    OfflineCacheService offlineCache,
    TripDocumentStore documents,
    ReservationDocumentLinkStore documentLinks) : ViewModelBase, IQueryAttributable
{
    private ScheduleItemDto? _scheduleItem;
    private ReservationDocumentLink? _documentLink;
    public bool HasLinkedDocument => _documentLink is not null;
    public string LinkedDocumentTitle => _documentLink?.Title ?? string.Empty;
    public bool CanLinkDocument => ScheduleItem is not null && sessionService.HasKnownValidAccess
        && (documents.CanAttach || sessionService.HasCuratedDocs);
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
                _documentLink = null;
                OnPropertyChanged(nameof(HasLinkedDocument));
                OnPropertyChanged(nameof(LinkedDocumentTitle));
                OnPropertyChanged(nameof(ReservationReferenceLabel));
                OnPropertyChanged(nameof(HasReservationReference));
                OnPropertyChanged(nameof(ReservationTimeLabel));
                OnPropertyChanged(nameof(AddressText));
                OnPropertyChanged(nameof(NotesText));
                OnPropertyChanged(nameof(HasNotes));
                OnPropertyChanged(nameof(HasAddress));
                OnPropertyChanged(nameof(CanLinkDocument));
            }
        }
    }

    public bool HasReservationReference => !string.IsNullOrWhiteSpace(ReservationReferenceLabel)
        && !string.Equals(ReservationReferenceLabel.Trim(), ScheduleItem?.Title?.Trim(), StringComparison.OrdinalIgnoreCase);

    public string ReservationReferenceLabel
    {
        get
        {
            if (ScheduleItem is null)
            {
                return string.Empty;
            }

            if (ScheduleItem.ConfirmationCode == "AI-PLAN") return string.Empty;

            return string.IsNullOrWhiteSpace(ScheduleItem.ConfirmationCode)
                ? ScheduleItem.LocationName
                : $"Codigo {ScheduleItem.ConfirmationCode}";
        }
    }

    public string ReservationTimeLabel
    {
        get
        {
            return ScheduleItem is null ? string.Empty : FormatReservationTime(ScheduleItem);
        }
    }

    internal static string FormatReservationTime(ScheduleItemDto item)
    {
        if (item.Type == ReservationType.Lodging && item.EndsOn.HasValue)
        {
            return ScheduleStayDateFormatter.Detailed(item);
        }

        var startDate = item.Date.ToString("dddd d MMMM");
        if (!item.HasExactTime) return $"{startDate} · {item.PeriodDisplay}";
        return item.HasEnd
            ? $"{startDate} · {item.StartsAt:HH\\:mm} - {item.EndDisplay.Replace("Hasta: ", string.Empty).Replace("Horario de llegada: ", string.Empty)}"
            : $"{startDate} · {item.StartsAt:HH\\:mm}";
    }

    public string AddressText => ScheduleItem is null || string.IsNullOrWhiteSpace(ScheduleItem.Address)
        ? "Direccion no disponible"
        : ScheduleItem.Address;

    public string NotesText => ItineraryNotePreview.IsAssistantPlaceholder(ScheduleItem?.Notes)
        ? string.Empty : ScheduleItem?.Notes ?? string.Empty;
    public bool HasNotes => !string.IsNullOrWhiteSpace(NotesText);
    public bool HasAddress => ScheduleItem is not null && !string.IsNullOrWhiteSpace(ScheduleItem.Address);

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("ScheduleItem", out var value) && value is ScheduleItemDto item)
        {
            ScheduleItem = item;
            CuratedNotes = string.Empty;
            _ = LoadCuratedNotesAsync(item);
            _ = RefreshLinkedDocumentAsync();
        }
    }

    public async Task RefreshLinkedDocumentAsync()
    {
        var item = ScheduleItem;
        if (item is null) return;
        var version = sessionService.ContextVersion;
        try
        {
            var link = await documentLinks.GetAsync(item.Id);
            if (link?.LocalDocumentId is { } id && !await documents.ExistsAsync(id))
            {
                await documentLinks.RemoveDocumentAsync(id);
                link = null;
            }
            if (ReferenceEquals(ScheduleItem, item) && version == sessionService.ContextVersion)
            {
                _documentLink = link;
                OnPropertyChanged(nameof(HasLinkedDocument));
                OnPropertyChanged(nameof(LinkedDocumentTitle));
                OnPropertyChanged(nameof(CanLinkDocument));
            }
        }
        catch { /* A changed session or unavailable local index must not hide reservation details. */ }
    }

    [RelayCommand]
    private async Task LinkDocumentAsync()
    {
        if (!CanLinkDocument || ScheduleItem is not { } item) return;
        var version = sessionService.ContextVersion;
        try
        {
            var local = await documents.ListAsync();
            var choices = local.Select(doc => new ReservationDocumentLink(item.Id, doc.Id, null, doc.Title)).ToList();
            if (sessionService.HasCuratedDocs)
            {
                var key = $"mobile-docs-{sessionService.CurrentTripId?.ToString() ?? "trip-auto"}-{sessionService.CurrentUserId?.ToString() ?? "anonymous"}";
                var cached = await offlineCache.GetAsync<TravelDocsDto>(key, maxAge: null);
                var curated = cached?.Value;
                if (curated is null && Connectivity.Current.NetworkAccess == NetworkAccess.Internet)
                {
                    var token = await sessionService.GetTokenAsync();
                    if (!string.IsNullOrWhiteSpace(token)) curated = await apiClient.GetTravelDocsAsync(token);
                }
                if (curated is not null)
                    choices.AddRange(curated.HotelDocuments.Concat(curated.OtherDocuments)
                        .Select(doc => new ReservationDocumentLink(item.Id, null, doc.FileUrl, doc.Title)));
            }
            if (version != sessionService.ContextVersion || !ReferenceEquals(ScheduleItem, item)) return;
            if (choices.Count == 0)
            {
                await Shell.Current.DisplayAlertAsync("Documentos", "Agregá un archivo en la pestaña Documentos y volvé a esta reserva.", "OK");
                return;
            }
            var labels = choices.Select((choice, index) => $"{index + 1}. {choice.Title}").ToArray();
            var selected = await Shell.Current.DisplayActionSheetAsync("Vincular documento", "Cancelar", null, labels);
            var position = Array.IndexOf(labels, selected);
            if (position < 0 || version != sessionService.ContextVersion) return;
            await documentLinks.SetAsync(choices[position]);
            await RefreshLinkedDocumentAsync();
        }
        catch { await Shell.Current.DisplayAlertAsync("Documentos", "No pudimos vincular el documento.", "OK"); }
    }

    [RelayCommand]
    private async Task OpenLinkedDocumentAsync()
    {
        if (_documentLink is not { } link || ScheduleItem?.Id != link.ReservationId) return;
        try
        {
            await documents.OpenLinkedAsync(link);
        }
        catch
        {
            await RefreshLinkedDocumentAsync();
            await Shell.Current.DisplayAlertAsync("Documento no disponible",
                "Comprobá que el archivo exista y que haya un visor instalado. Si es un documento del viaje, intentá con conexión.", "OK");
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

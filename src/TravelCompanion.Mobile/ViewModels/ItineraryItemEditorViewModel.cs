using CommunityToolkit.Mvvm.Input;
using TravelCompanion.Mobile.Services;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Mobile.ViewModels;

public sealed partial class ItineraryItemEditorViewModel(
    TravelCompanionApiClient apiClient,
    AuthSessionService sessionService,
    MobileBootstrapStore bootstrapStore,
    MobileTodayStore todayStore) : ViewModelBase
{
    private RecommendationDto? _recommendation;
    private DateTime _date = DateTime.Today;
    private string _selectedPeriod = "Tarde";
    private bool _useExactTime;
    private TimeSpan _time = new(15, 0, 0);
    private string _notes = string.Empty;
    private string _titleText = string.Empty;
    private string _locationName = string.Empty;
    private string _address = string.Empty;
    private int _revision;
    private ScheduleItemDto? _existingItem;

    public IReadOnlyList<string> Periods { get; } = ["Mañana", "Medio día", "Tarde", "Noche"];
    public string HeaderTitle => _recommendation?.Title ?? "Nuevo plan";
    public string Subtitle => _recommendation?.Neighborhood ?? "Agrega una idea personal";
    public string TitleText { get => _titleText; set => SetProperty(ref _titleText, value); }
    public string LocationName { get => _locationName; set => SetProperty(ref _locationName, value); }
    public string Address { get => _address; set => SetProperty(ref _address, value); }
    public DateTime Date { get => _date; set => SetProperty(ref _date, value); }
    public string SelectedPeriod { get => _selectedPeriod; set => SetProperty(ref _selectedPeriod, value); }
    public bool UseExactTime { get => _useExactTime; set => SetProperty(ref _useExactTime, value); }
    public TimeSpan Time { get => _time; set => SetProperty(ref _time, value); }
    public string Notes { get => _notes; set => SetProperty(ref _notes, value); }

    public async Task InitializeAsync(RecommendationDto recommendation)
    {
        _recommendation = recommendation;
        TitleText = recommendation.Title;
        LocationName = recommendation.Title;
        Address = recommendation.Neighborhood;
        OnPropertyChanged(nameof(HeaderTitle));
        OnPropertyChanged(nameof(Subtitle));
        var token = await sessionService.GetTokenAsync();
        var setup = string.IsNullOrWhiteSpace(token) ? null : await apiClient.GetBuilderTripSetupAsync(token);
        if (setup is not null)
        {
            _revision = setup.Revision;
            Date = (setup.ArrivalDate ?? DateOnly.FromDateTime(DateTime.Today)).ToDateTime(TimeOnly.MinValue);
        }
    }

    public async Task InitializeManualAsync(DateOnly date, string periodKey)
    {
        _recommendation = null;
        TitleText = string.Empty;
        LocationName = string.Empty;
        Address = string.Empty;
        Date = date.ToDateTime(TimeOnly.MinValue);
        SelectedPeriod = periodKey switch { "morning" => "Mañana", "midday" => "Medio día", "night" => "Noche", _ => "Tarde" };
        OnPropertyChanged(nameof(HeaderTitle));
        OnPropertyChanged(nameof(Subtitle));
        var token = await sessionService.GetTokenAsync();
        var setup = string.IsNullOrWhiteSpace(token) ? null : await apiClient.GetBuilderTripSetupAsync(token);
        _revision = setup?.Revision ?? 0;
    }

    public async Task InitializeExistingAsync(ScheduleItemDto item)
    {
        _existingItem = item;
        _recommendation = null;
        TitleText = item.Title;
        LocationName = item.LocationName;
        Address = item.Address;
        Notes = item.Notes;
        Date = item.Date.ToDateTime(TimeOnly.MinValue);
        SelectedPeriod = item.StartsAt.Hour switch { < 12 => "Mañana", < 15 => "Medio día", < 19 => "Tarde", _ => "Noche" };
        UseExactTime = item.HasExactTime;
        Time = item.StartsAt.ToTimeSpan();
        OnPropertyChanged(nameof(HeaderTitle));
        OnPropertyChanged(nameof(Subtitle));
        var token = await sessionService.GetTokenAsync();
        var setup = string.IsNullOrWhiteSpace(token) ? null : await apiClient.GetBuilderTripSetupAsync(token);
        _revision = setup?.Revision ?? 0;
    }

    [RelayCommand]
    private Task SaveAsync() => LoadAsync(async ct =>
    {
        if (string.IsNullOrWhiteSpace(TitleText))
        {
            ErrorMessage = "Escribe un nombre para el plan.";
            return;
        }
        var token = await sessionService.GetTokenAsync();
        if (string.IsNullOrWhiteSpace(token)) return;
        var periodKey = SelectedPeriod switch
        {
            "Mañana" => "morning",
            "Medio día" => "midday",
            "Noche" => "night",
            _ => "afternoon"
        };
        var mutation = new ItineraryItemMutationRequest(
            _recommendation is null || _recommendation.Id == Guid.Empty ? null : _recommendation.Id,
            _recommendation?.ProviderPlaceId, TitleText.Trim(), DateOnly.FromDateTime(Date), periodKey,
            UseExactTime, UseExactTime ? TimeOnly.FromTimeSpan(Time) : null, null,
            _recommendation?.Neighborhood.Split(',')[0], LocationName, Address,
            Notes, _recommendation?.Latitude, _recommendation?.Longitude, _revision, Guid.NewGuid().ToString("N"));
        var result = _existingItem is null
            ? await apiClient.CreateItineraryItemAsync(token, mutation, ct)
            : await apiClient.UpdateItineraryItemAsync(token, _existingItem.Id, mutation, ct);
        if (result is null || !result.Success)
        {
            ErrorMessage = result?.Message ?? "No se pudo guardar. Comprueba tu conexión.";
            return;
        }
        if (result.Item is not null) await bootstrapStore.UpsertScheduleItemAsync(result.Item, ct);
        await todayStore.ClearUserCacheAsync(sessionService.CurrentUserId, ct);
        await Shell.Current.GoToAsync("//main/schedule");
    });

    [RelayCommand]
    private Task CancelAsync() => Shell.Current.GoToAsync("//main/map");
}

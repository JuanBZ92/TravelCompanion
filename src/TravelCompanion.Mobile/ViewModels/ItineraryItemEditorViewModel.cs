using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.Input;
using TravelCompanion.Mobile.Services;
using TravelCompanion.Shared;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Mobile.ViewModels;

public sealed partial class ItineraryItemEditorViewModel(
    TravelCompanionApiClient apiClient,
    AuthSessionService sessionService,
    MobileBootstrapStore bootstrapStore,
    MobileTodayStore todayStore,
    MapViewModel mapViewModel) : ViewModelBase
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
    private IReadOnlyList<BuilderTripSetupSegmentDto> _segments = [];
    private CancellationTokenSource? _placeSearch;
    private string _placeSessionToken = Guid.NewGuid().ToString();
    private string? _selectedGooglePlaceId;
    private decimal? _selectedLatitude;
    private decimal? _selectedLongitude;
    private string _currentCity = string.Empty;
    private string _fallbackCity = string.Empty;
    private string? _placeSearchMessage;
    private bool _applyingPlaceSelection;

    public IReadOnlyList<string> Periods { get; } = ["Mañana", "Medio día", "Tarde", "Noche"];
    public ObservableCollection<PlaceSuggestionDto> PlaceSuggestions { get; } = [];
    public string HeaderTitle => _recommendation?.Title ?? "Nuevo plan";
    public string Subtitle => _recommendation?.Neighborhood ?? "Agrega una idea personal";
    public string TitleText { get => _titleText; set => SetProperty(ref _titleText, value); }
    public string LocationName
    {
        get => _locationName;
        set
        {
            if (SetProperty(ref _locationName, value) && !_applyingPlaceSelection)
            {
                ClearSelectedPlace();
                CancelPlaceSearch();
            }
        }
    }
    public string Address
    {
        get => _address;
        set
        {
            if (SetProperty(ref _address, value) && !_applyingPlaceSelection)
            {
                ClearSelectedPlace();
            }
        }
    }
    public DateTime Date
    {
        get => _date;
        set
        {
            if (SetProperty(ref _date, value))
            {
                CancelPlaceSearch();
                RefreshCurrentCity();
            }
        }
    }
    public string SelectedPeriod { get => _selectedPeriod; set => SetProperty(ref _selectedPeriod, value); }
    public bool UseExactTime { get => _useExactTime; set => SetProperty(ref _useExactTime, value); }
    public TimeSpan Time { get => _time; set => SetProperty(ref _time, value); }
    public string Notes { get => _notes; set => SetProperty(ref _notes, value); }
    public string CurrentCity { get => _currentCity; private set => SetProperty(ref _currentCity, value); }
    public string? PlaceSearchMessage
    {
        get => _placeSearchMessage;
        private set
        {
            if (SetProperty(ref _placeSearchMessage, value))
            {
                OnPropertyChanged(nameof(HasPlaceSearchMessage));
            }
        }
    }
    public bool HasPlaceSearchMessage => !string.IsNullOrWhiteSpace(PlaceSearchMessage);
    public bool CanSearchPlaces => _recommendation is null;

    public async Task InitializeAsync(RecommendationDto recommendation)
    {
        CancelPlaceSearch();
        _existingItem = null;
        _recommendation = recommendation;
        _fallbackCity = recommendation.Neighborhood.Split(',')[0].Trim();
        _applyingPlaceSelection = true;
        TitleText = recommendation.Title;
        LocationName = recommendation.Title;
        Address = recommendation.Neighborhood;
        _applyingPlaceSelection = false;
        OnPropertyChanged(nameof(HeaderTitle));
        OnPropertyChanged(nameof(Subtitle));
        OnPropertyChanged(nameof(CanSearchPlaces));
        var setup = await LoadSetupAsync();
        if (setup is not null) Date = (setup.ArrivalDate ?? DateOnly.FromDateTime(DateTime.Today)).ToDateTime(TimeOnly.MinValue);
    }

    public async Task InitializeManualAsync(DateOnly date, string periodKey)
    {
        CancelPlaceSearch();
        _existingItem = null;
        _recommendation = null;
        _fallbackCity = string.Empty;
        _applyingPlaceSelection = true;
        TitleText = string.Empty;
        LocationName = string.Empty;
        Address = string.Empty;
        _applyingPlaceSelection = false;
        ClearSelectedPlace();
        Date = date.ToDateTime(TimeOnly.MinValue);
        SelectedPeriod = periodKey switch { "morning" => "Mañana", "midday" => "Medio día", "night" => "Noche", _ => "Tarde" };
        OnPropertyChanged(nameof(HeaderTitle));
        OnPropertyChanged(nameof(Subtitle));
        OnPropertyChanged(nameof(CanSearchPlaces));
        await LoadSetupAsync();
    }

    public async Task InitializeExistingAsync(ScheduleItemDto item)
    {
        CancelPlaceSearch();
        _existingItem = item;
        _recommendation = null;
        _fallbackCity = item.City;
        _applyingPlaceSelection = true;
        TitleText = item.Title;
        LocationName = item.LocationName;
        Address = item.Address;
        _applyingPlaceSelection = false;
        _selectedGooglePlaceId = item.ItemSource == ItineraryItemSource.GooglePlace
            ? item.ProviderPlaceId
            : null;
        _selectedLatitude = null;
        _selectedLongitude = null;
        Notes = item.Notes;
        Date = item.Date.ToDateTime(TimeOnly.MinValue);
        SelectedPeriod = item.StartsAt.Hour switch { < 12 => "Mañana", < 15 => "Medio día", < 19 => "Tarde", _ => "Noche" };
        UseExactTime = item.HasExactTime;
        Time = item.StartsAt.ToTimeSpan();
        OnPropertyChanged(nameof(HeaderTitle));
        OnPropertyChanged(nameof(Subtitle));
        OnPropertyChanged(nameof(CanSearchPlaces));
        await LoadSetupAsync();
    }

    public async Task SearchPlaceSuggestionsAsync()
    {
        if (_applyingPlaceSelection) return;
        CancelPlaceSearch();
        PlaceSearchMessage = null;
        if (!CanSearchPlaces) return;

        var query = NormalizeSearchText(LocationName);
        var city = CurrentCity.Trim();
        if (query.Length < 3 || string.IsNullOrWhiteSpace(city)) return;

        var operation = new CancellationTokenSource();
        _placeSearch = operation;
        var sessionToken = _placeSessionToken;
        try
        {
            await Task.Delay(350, operation.Token);
            var token = await sessionService.GetTokenAsync();
            if (string.IsNullOrWhiteSpace(token)) return;
            var results = await apiClient.AutocompletePlacesAsync(token, new PlaceAutocompleteRequest(
                query,
                city,
                sessionToken,
                CultureInfo.CurrentUICulture.Name,
                PlaceAutocompleteMode.Place), operation.Token);
            if (operation.IsCancellationRequested
                || !ReferenceEquals(_placeSearch, operation)
                || !string.Equals(NormalizeSearchText(LocationName), query, StringComparison.Ordinal)
                || !string.Equals(CurrentCity.Trim(), city, StringComparison.Ordinal)
                || !string.Equals(_placeSessionToken, sessionToken, StringComparison.Ordinal)) return;

            foreach (var result in results.Take(5)) PlaceSuggestions.Add(result);
            PlaceSearchMessage = results.Count == 0
                ? "No encontramos coincidencias. Puedes escribir el lugar y la dirección manualmente."
                : null;
        }
        catch (OperationCanceledException) { }
        catch (Exception)
        {
            if (!operation.IsCancellationRequested)
            {
                PlaceSearchMessage = "La búsqueda no está disponible. Puedes completar el lugar manualmente.";
            }
        }
        finally
        {
            if (ReferenceEquals(_placeSearch, operation)) _placeSearch = null;
            operation.Dispose();
        }
    }

    public async Task SelectPlaceAsync(PlaceSuggestionDto suggestion)
    {
        CancelPlaceSearch(clearSuggestions: false);
        var query = NormalizeSearchText(LocationName);
        var city = CurrentCity.Trim();
        var sessionToken = _placeSessionToken;
        var operation = new CancellationTokenSource();
        _placeSearch = operation;
        try
        {
            var token = await sessionService.GetTokenAsync();
            if (string.IsNullOrWhiteSpace(token)) return;
            var place = await apiClient.GetPlaceDetailsAsync(token, new PlaceDetailsRequest(
                suggestion.PlaceId,
                sessionToken,
                CultureInfo.CurrentUICulture.Name), operation.Token);
            if (place is null
                || operation.IsCancellationRequested
                || !ReferenceEquals(_placeSearch, operation)
                || !string.Equals(NormalizeSearchText(LocationName), query, StringComparison.Ordinal)
                || !string.Equals(CurrentCity.Trim(), city, StringComparison.Ordinal))
            {
                if (place is null) PlaceSearchMessage = "No pudimos cargar ese lugar. Puedes completarlo manualmente.";
                return;
            }

            _applyingPlaceSelection = true;
            LocationName = place.Title;
            Address = place.Neighborhood;
            _applyingPlaceSelection = false;
            _selectedGooglePlaceId = place.ProviderPlaceId;
            _selectedLatitude = place.Latitude;
            _selectedLongitude = place.Longitude;
            if (string.IsNullOrWhiteSpace(TitleText)) TitleText = place.Title;
            PlaceSuggestions.Clear();
            PlaceSearchMessage = null;
        }
        catch (OperationCanceledException) { }
        catch (Exception)
        {
            if (!operation.IsCancellationRequested)
            {
                PlaceSearchMessage = "No pudimos cargar ese lugar. Puedes completarlo manualmente.";
            }
        }
        finally
        {
            _placeSessionToken = Guid.NewGuid().ToString();
            if (ReferenceEquals(_placeSearch, operation)) _placeSearch = null;
            operation.Dispose();
        }
    }

    public void CancelPlaceSearches() => CancelPlaceSearch();

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
        Guid? recommendationId = _recommendation is not null && _recommendation.Id != Guid.Empty
            ? _recommendation.Id
            : _existingItem?.RecommendationId;
        var mutation = new ItineraryItemMutationRequest(
            recommendationId,
            recommendationId.HasValue ? null : _selectedGooglePlaceId, TitleText.Trim(), DateOnly.FromDateTime(Date), periodKey,
            UseExactTime, UseExactTime ? TimeOnly.FromTimeSpan(Time) : null, null,
            recommendationId.HasValue ? _recommendation?.Neighborhood.Split(',')[0] ?? CurrentCity : CurrentCity,
            LocationName, Address,
            Notes, _recommendation?.Latitude ?? _selectedLatitude, _recommendation?.Longitude ?? _selectedLongitude,
            _revision, Guid.NewGuid().ToString("N"));
        var result = await SaveMutationAsync(mutation);
        if (result?.HasOverlap == true)
        {
            var confirmed = await Shell.Current.DisplayAlertAsync(
                "Ya hay un plan en este momento",
                "¿Quieres agregar este plan igualmente? Los dos quedarán visibles en el mismo bloque.",
                "Agregar igualmente",
                "Cancelar");
            if (!confirmed)
            {
                ErrorMessage = null;
                return;
            }

            result = await SaveMutationAsync(mutation with { ConfirmOverlap = true });
        }

        if (result is null || !result.Success)
        {
            ErrorMessage = result?.Message ?? "No se pudo guardar. Comprueba tu conexión.";
            return;
        }
        if (result.Item is not null) await bootstrapStore.UpsertScheduleItemAsync(result.Item, ct);
        await todayStore.ClearUserCacheAsync(sessionService.CurrentUserId, ct);
        mapViewModel.ResetSelection();
        await Shell.Current.Navigation.PopToRootAsync(animated: false);
        await Shell.Current.GoToAsync("//main/schedule");

        Task<ItineraryItemMutationResponse?> SaveMutationAsync(ItineraryItemMutationRequest request) =>
            _existingItem is null
                ? apiClient.CreateItineraryItemAsync(token, request, ct)
                : apiClient.UpdateItineraryItemAsync(token, _existingItem.Id, request, ct);
    });

    [RelayCommand]
    private async Task CancelAsync()
    {
        CancelPlaceSearch();
        await Shell.Current.Navigation.PopToRootAsync(animated: false);
    }

    private async Task<BuilderTripSetupDto?> LoadSetupAsync()
    {
        var token = await sessionService.GetTokenAsync();
        var setup = string.IsNullOrWhiteSpace(token) ? null : await apiClient.GetBuilderTripSetupAsync(token);
        _revision = setup?.Revision ?? 0;
        _segments = setup?.Segments ?? [];
        RefreshCurrentCity();
        return setup;
    }

    private void RefreshCurrentCity()
    {
        var date = DateOnly.FromDateTime(Date);
        var segment = _segments.FirstOrDefault(item => date >= item.StartsOn && date <= item.EndsOn);
        CurrentCity = segment?.City?.Trim() ?? _fallbackCity;
    }

    private void CancelPlaceSearch(bool clearSuggestions = true)
    {
        _placeSearch?.Cancel();
        _placeSearch = null;
        if (clearSuggestions) PlaceSuggestions.Clear();
        PlaceSearchMessage = null;
    }

    private void ClearSelectedPlace()
    {
        _selectedGooglePlaceId = null;
        _selectedLatitude = null;
        _selectedLongitude = null;
    }

    private static string NormalizeSearchText(string value) =>
        string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}

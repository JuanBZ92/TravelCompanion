using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using TravelCompanion.Mobile.Services;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Mobile.ViewModels;

public sealed partial class FreeMapViewModel(
    AuthSessionService sessionService,
    TravelCompanionApiClient apiClient,
    FreeMapStore freeMapStore) : ViewModelBase, ISessionStateResettable
{
    private FreeMapCityDto? _selectedCity;
    private FreeMapPreviewDto? _preview;
    private FreeMapMarkerDto? _selectedMarker;

    public ObservableCollection<FreeMapCityDto> Cities { get; } = [];

    public FreeMapCityDto? SelectedCity
    {
        get => _selectedCity;
        set
        {
            if (SetProperty(ref _selectedCity, value) && value is not null && HasLoaded)
            {
                _ = LoadSelectedCityAsync(value);
            }
        }
    }

    public FreeMapPreviewDto? Preview
    {
        get => _preview;
        private set
        {
            if (SetProperty(ref _preview, value))
            {
                OnPropertyChanged(nameof(HasPreview));
                OnPropertyChanged(nameof(MapSummary));
                OnPropertyChanged(nameof(HasContactUrl));
                OnPropertyChanged(nameof(ShowPinOnlyAction));
                SelectedMarker = null;
            }
        }
    }

    public FreeMapMarkerDto? SelectedMarker
    {
        get => _selectedMarker;
        private set
        {
            if (SetProperty(ref _selectedMarker, value))
            {
                OnPropertyChanged(nameof(HasSelection));
                OnPropertyChanged(nameof(IsUnlockedSelection));
                OnPropertyChanged(nameof(IsLockedSelection));
                OnPropertyChanged(nameof(ShowMapSummary));
                OnPropertyChanged(nameof(SelectedRecommendation));
                OnPropertyChanged(nameof(SelectedTagsText));
                OnPropertyChanged(nameof(SelectedLocationText));
                OnPropertyChanged(nameof(SelectedMetadataText));
            }
        }
    }

    public bool HasPreview => Preview is not null;
    public bool HasSelection => SelectedMarker is not null;
    public bool IsUnlockedSelection => SelectedMarker?.Access == FreeMapMarkerAccess.Unlocked;
    public bool IsLockedSelection => SelectedMarker?.Access == FreeMapMarkerAccess.Locked;
    public bool ShowMapSummary => Preview is not null && SelectedMarker is null;
    public bool HasContactUrl => !string.IsNullOrWhiteSpace(Preview?.ContactUrl);
    public bool ShowPinOnlyAction => Preview is not null && !HasContactUrl;
    public RecommendationDto? SelectedRecommendation => SelectedMarker?.Recommendation;
    public string SelectedTagsText => SelectedRecommendation is null
        ? string.Empty
        : string.Join(" · ", SelectedRecommendation.Tags);
    public string SelectedLocationText => SelectedRecommendation is null
        ? string.Empty
        : SelectedRecommendation.Rating.HasValue
            ? $"{SelectedRecommendation.Neighborhood} · Rating {SelectedRecommendation.Rating:0.0}"
            : SelectedRecommendation.Neighborhood;
    public string SelectedMetadataText => SelectedRecommendation is null
        ? string.Empty
        : $"{FormatPrice(SelectedRecommendation.PriceLevel)} · {SelectedRecommendation.SuggestedDurationMinutes} min · {SelectedRecommendation.DistanceKm:0.0} km del centro";
    public string MapSummary => Preview is null
        ? string.Empty
        : $"{Preview.UnlockedCount} lugares abiertos · {Preview.LockedCount} por descubrir";

    [RelayCommand]
    private Task LoadAsync() => LoadAsync(LoadInitialAsync);

    [RelayCommand]
    private Task RefreshAsync()
    {
        IsRefreshing = true;
        return LoadAsync(async cancellationToken =>
        {
            var token = await RequireTokenAsync();
            var cities = await freeMapStore.RefreshCitiesAsync(token, cancellationToken)
                ?? throw new InvalidOperationException("No pudimos actualizar las ciudades.");
            ApplyCities(cities, SelectedCity?.Slug);
            await RefreshSelectedCityAsync(token, cancellationToken, allowCacheFallback: true);
        });
    }

    public void SelectMarker(FreeMapMarkerDto marker) => SelectedMarker = marker;

    [RelayCommand]
    private void CloseSelection() => SelectedMarker = null;

    [RelayCommand]
    private async Task OpenSelectedMapAsync()
    {
        if (SelectedRecommendation is null)
        {
            return;
        }

        await GoogleMapsLauncher.OpenAsync(
            SelectedRecommendation.Latitude,
            SelectedRecommendation.Longitude);
    }

    [RelayCommand]
    private async Task ContactAsync()
    {
        if (Uri.TryCreate(Preview?.ContactUrl, UriKind.Absolute, out var uri))
        {
            await Launcher.Default.OpenAsync(uri);
        }
    }

    [RelayCommand]
    private Task UseAnotherPinAsync() => EndPreviewSessionAsync();

    [RelayCommand]
    private Task LogoutAsync() => EndPreviewSessionAsync();

    public void ResetForNewSession()
    {
        ResetLoadState();
        Cities.Clear();
        _selectedCity = null;
        OnPropertyChanged(nameof(SelectedCity));
        Preview = null;
    }

    private async Task LoadInitialAsync(CancellationToken cancellationToken)
    {
        var token = await RequireTokenAsync();
        var cachedCities = await freeMapStore.GetCachedCitiesAsync(cancellationToken);
        if (cachedCities is not null)
        {
            ApplyCities(cachedCities.Value, SelectedCity?.Slug);
            await ApplyCachedSelectedCityAsync(cancellationToken);
            StatusMessage = $"Mostrando mapa guardado. {OfflineCacheService.FormatSavedAt(cachedCities.SavedAt)}";
        }

        try
        {
            var remoteCities = await freeMapStore.RefreshCitiesAsync(token, cancellationToken);
            if (remoteCities is null || remoteCities.Count == 0)
            {
                throw new InvalidOperationException("No hay ciudades disponibles para el mapa gratuito.");
            }

            ApplyCities(remoteCities, SelectedCity?.Slug);
            await RefreshSelectedCityAsync(token, cancellationToken, allowCacheFallback: cachedCities is not null);
            StatusMessage = null;
        }
        catch when (cachedCities is not null && Preview is not null)
        {
            StatusMessage = $"Modo offline. {OfflineCacheService.FormatSavedAt(cachedCities.SavedAt)}";
        }
    }

    private Task LoadSelectedCityAsync(FreeMapCityDto city)
    {
        return LoadAsync(async cancellationToken =>
        {
            var token = await RequireTokenAsync();
            var cached = await freeMapStore.GetCachedCityAsync(city.Slug, cancellationToken);
            if (cached is not null)
            {
                Preview = cached.Value;
                StatusMessage = $"Actualizando {city.Name}...";
            }

            try
            {
                var remote = await freeMapStore.RefreshCityAsync(token, city.Slug, cancellationToken);
                if (remote is null)
                {
                    throw new InvalidOperationException("No pudimos cargar esta ciudad.");
                }

                Preview = remote;
                StatusMessage = null;
            }
            catch when (cached is not null)
            {
                StatusMessage = $"Modo offline. {OfflineCacheService.FormatSavedAt(cached.SavedAt)}";
            }
        });
    }

    private async Task RefreshSelectedCityAsync(
        string token,
        CancellationToken cancellationToken,
        bool allowCacheFallback)
    {
        if (SelectedCity is null)
        {
            Preview = null;
            return;
        }

        var remote = await freeMapStore.RefreshCityAsync(token, SelectedCity.Slug, cancellationToken);
        if (remote is not null)
        {
            Preview = remote;
            return;
        }

        if (allowCacheFallback)
        {
            var cached = await freeMapStore.GetCachedCityAsync(SelectedCity.Slug, cancellationToken);
            if (cached is not null)
            {
                Preview = cached.Value;
                return;
            }
        }

        throw new InvalidOperationException("No pudimos cargar el mapa gratuito.");
    }

    private async Task ApplyCachedSelectedCityAsync(CancellationToken cancellationToken)
    {
        if (SelectedCity is null)
        {
            return;
        }

        var cached = await freeMapStore.GetCachedCityAsync(SelectedCity.Slug, cancellationToken);
        if (cached is not null)
        {
            Preview = cached.Value;
        }
    }

    private void ApplyCities(IReadOnlyList<FreeMapCityDto> cities, string? selectedSlug)
    {
        Cities.Clear();
        foreach (var city in cities.OrderBy(city => city.SortOrder).ThenBy(city => city.Name))
        {
            Cities.Add(city);
        }

        var selected = Cities.FirstOrDefault(city => city.Slug == selectedSlug) ?? Cities.FirstOrDefault();
        _selectedCity = selected;
        OnPropertyChanged(nameof(SelectedCity));
    }

    private async Task<string> RequireTokenAsync()
    {
        var token = await sessionService.GetTokenAsync();
        if (!string.IsNullOrWhiteSpace(token))
        {
            return token;
        }

        sessionService.Clear();
        await Shell.Current.GoToAsync("//login");
        throw new OperationCanceledException();
    }

    private async Task EndPreviewSessionAsync()
    {
        var token = await sessionService.GetTokenAsync();
        if (!string.IsNullOrWhiteSpace(token))
        {
            try
            {
                await apiClient.LogoutAsync(token);
            }
            catch
            {
                // Local logout must remain available while Render is offline.
            }
        }

        await freeMapStore.ClearAsync();
        sessionService.Clear();
        ResetForNewSession();
        await Shell.Current.GoToAsync("//login");
    }

    private static string FormatPrice(string? priceLevel) => priceLevel?.Trim().ToLowerInvariant() switch
    {
        "free" => "Gratis",
        "low" => "Coste bajo",
        "high" => "Coste alto",
        _ => "Coste medio"
    };
}

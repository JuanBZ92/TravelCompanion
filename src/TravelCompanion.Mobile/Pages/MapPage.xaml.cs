using System;
using System.ComponentModel;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Maui;
using TravelCompanion.Mobile.Controls;
using TravelCompanion.Mobile.ViewModels;
using TravelCompanion.Shared.Dtos;

#if IOS || MACCATALYST
using MapKit;
using UIKit;
#endif
#if !WINDOWS
using MauiMap = Microsoft.Maui.Controls.Maps.Map;
using Microsoft.Maui.Controls.Maps;
using Microsoft.Maui.Maps;
#endif

namespace TravelCompanion.Mobile.Pages;

public partial class MapPage : ContentPage
{
    private const double CollapsedSearchPanelHeight = 300;
    private const double MinimumExpandedSearchPanelHeight = 340;
    private const double MaximumExpandedSearchPanelHeight = 520;
    private readonly MapViewModel _viewModel;
    private readonly ILogger<MapPage> _logger;
    private CancellationTokenSource? _searchDebounce;
    private bool _isSearchPanelExpanded;

#if !WINDOWS
    private readonly MauiMap _map;
    private readonly Dictionary<Pin, EventHandler<PinClickedEventArgs>> _pinHandlers =
        new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<string, RecommendationMapPin> _pinsBySelectionKey = new(StringComparer.Ordinal);
    private Circle? _selectionIndicator;
    private bool _isSubscribedToRecommendations;
    private bool _hasRenderedPins;
    private bool _mapPinsRefreshPending;
#endif

    public MapPage()
        : this(
            MauiProgram.Services.GetRequiredService<MapViewModel>(),
            MauiProgram.Services.GetRequiredService<ILogger<MapPage>>())
    {
    }

    public MapPage(
        MapViewModel viewModel,
        ILogger<MapPage> logger)
    {
        var stopwatch = Stopwatch.StartNew();
        InitializeComponent();
        stopwatch.Stop();
        BindingContext = viewModel;
        _viewModel = viewModel;
        _logger = logger;

#if !WINDOWS
        var mapStopwatch = Stopwatch.StartNew();
        _map = new MauiMap(MapSpan.FromCenterAndRadius(
            new Location(35.681236, 139.767125),
            Distance.FromKilometers(8)))
        {
            IsShowingUser = false,
            MapType = MapType.Street
        };

        MapContainer.Children.Clear();
        MapContainer.Children.Add(_map);
        _map.MapClicked += (_, _) => DismissSearchKeyboard();
#if IOS || MACCATALYST
        _map.HandlerChanged += OnMapHandlerChanged;
#endif
        mapStopwatch.Stop();
        _logger.LogInformation(
            "Map native control initialized in {ElapsedMs}ms.",
            mapStopwatch.Elapsed.TotalMilliseconds);
#endif

        _logger.LogInformation(
            "Map page initialized in {ElapsedMs}ms. HasLoaded={HasLoaded}.",
            stopwatch.Elapsed.TotalMilliseconds,
            _viewModel.HasLoaded);
    }

    protected override async void OnAppearing()
    {
        var stopwatch = Stopwatch.StartNew();
        base.OnAppearing();

#if !WINDOWS
        SubscribeToRecommendations();
#endif

        var wasLoaded = _viewModel.HasLoaded;
        if (wasLoaded)
        {
#if !WINDOWS
            if (!_hasRenderedPins)
            {
                TryRefreshMapPins();
            }
#endif
        }

        try
        {
            await _viewModel.LoadNearbyRecommendationsCommand.ExecuteAsync(null);
        }
        catch (Exception ex)
        {
            _viewModel.ErrorMessage = $"Error loading map: {ex.Message}";
        }
        finally
        {
            stopwatch.Stop();
            _logger.LogInformation(
                "Map page appeared in {ElapsedMs}ms. WarmState={WarmState}; HasLoaded={HasLoaded}.",
                stopwatch.Elapsed.TotalMilliseconds,
                wasLoaded,
                _viewModel.HasLoaded);
        }
    }

    protected override void OnDisappearing()
    {
        CancelSearchDebounce();
        _viewModel.CancelSearch();
        _viewModel.CancelLoading();
        DismissSearchKeyboard();
        base.OnDisappearing();
#if !WINDOWS
        UnsubscribeFromRecommendations();
        _hasRenderedPins = false;
#endif
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MapViewModel.VisibleNearbyRecommendations))
        {
            void ApplyRecommendations()
            {
                _ = ResetResultsScrollAsync();
            }

            if (Dispatcher.IsDispatchRequired)
            {
                Dispatcher.Dispatch(ApplyRecommendations);
            }
            else
            {
                ApplyRecommendations();
            }
        }
#if !WINDOWS
        else if (e.PropertyName == nameof(MapViewModel.MapRecommendations))
        {
            if (PlaceSearch.IsFocused)
            {
                // Updating hundreds of native map annotations while the user types blocks
                // the UI thread. The result list is already visible above the keyboard, so
                // keep the current pins stable and synchronize them once search ends.
                _mapPinsRefreshPending = true;
                return;
            }

            if (Dispatcher.IsDispatchRequired)
            {
                Dispatcher.Dispatch(() => TryRefreshMapPins());
            }
            else
            {
                TryRefreshMapPins();
            }
        }
        else if (e.PropertyName == nameof(MapViewModel.SelectedRecommendation))
        {
            void ApplySelection()
            {
                DismissSearchKeyboard();
                TryRefreshMapPins(moveToBounds: false);
                TryFocusRecommendation(_viewModel.SelectedRecommendation);
            }

            if (Dispatcher.IsDispatchRequired)
            {
                Dispatcher.Dispatch(ApplySelection);
            }
            else
            {
                ApplySelection();
            }
        }
#endif
    }

    private async Task ResetResultsScrollAsync()
    {
        try
        {
            await ResultsScroll.ScrollToAsync(0, 0, false);
        }
        catch (Exception exception)
        {
            _logger.LogDebug(exception, "Could not reset the map results scroll position.");
        }
    }

#if !WINDOWS
    private void SubscribeToRecommendations()
    {
        if (_isSubscribedToRecommendations)
        {
            return;
        }

        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        _isSubscribedToRecommendations = true;
    }

    private void UnsubscribeFromRecommendations()
    {
        if (!_isSubscribedToRecommendations)
        {
            return;
        }

        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        _isSubscribedToRecommendations = false;
    }

    private void RefreshMapPins(bool moveToBounds = true)
    {
        var stopwatch = Stopwatch.StartNew();
        var recommendationsByKey = _viewModel.MapRecommendations
            .GroupBy(recommendation => recommendation.SelectionKey, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        foreach (var staleKey in _pinsBySelectionKey.Keys.Except(recommendationsByKey.Keys, StringComparer.Ordinal).ToList())
        {
            var stalePin = _pinsBySelectionKey[staleKey];
            if (_pinHandlers.Remove(stalePin, out var staleHandler))
            {
                stalePin.MarkerClicked -= staleHandler;
            }
            _map.Pins.Remove(stalePin);
            _pinsBySelectionKey.Remove(staleKey);
        }

        foreach (var (selectionKey, recommendation) in recommendationsByKey)
        {
            if (!_pinsBySelectionKey.TryGetValue(selectionKey, out var pin))
            {
                pin = new RecommendationMapPin
                {
                    Label = recommendation.Title,
                    Address = recommendation.Neighborhood,
                    Type = PinType.Place,
                    Location = new Location((double)recommendation.Latitude, (double)recommendation.Longitude),
                    IsSelected = selectionKey == _viewModel.SelectedRecommendation?.SelectionKey
                };
                EventHandler<PinClickedEventArgs> handler = (_, args) =>
                {
                    args.HideInfoWindow = true;
                    void SelectPin()
                    {
                        var current = _viewModel.MapRecommendations
                            .FirstOrDefault(item => item.SelectionKey == selectionKey);
                        if (current is not null) _viewModel.SelectRecommendationCommand.Execute(current);
                    }
                    if (Dispatcher.IsDispatchRequired) Dispatcher.Dispatch(SelectPin); else SelectPin();
                };
                _pinHandlers[pin] = handler;
                _pinsBySelectionKey[selectionKey] = pin;
                pin.MarkerClicked += handler;
                _map.Pins.Add(pin);
            }
            else
            {
                pin.Label = recommendation.Title;
                pin.Address = recommendation.Neighborhood;
                pin.Location = new Location((double)recommendation.Latitude, (double)recommendation.Longitude);
            }

            pin.IsSelected = selectionKey == _viewModel.SelectedRecommendation?.SelectionKey;
            pin.Handler?.UpdateValue(nameof(RecommendationMapPin.IsSelected));
#if IOS || MACCATALYST
            UpdateApplePinSelection(pin);
#endif
        }

        UpdateSelectionIndicator();

        if (moveToBounds)
        {
            MoveToRecommendationBounds(_viewModel.MapRecommendations);
        }

        _hasRenderedPins = true;
        stopwatch.Stop();
        _logger.LogInformation(
            "Map pins refreshed in {ElapsedMs}ms. Pins={PinCount}.",
            stopwatch.Elapsed.TotalMilliseconds,
            _viewModel.MapRecommendations.Count);
    }

    private void TryRefreshMapPins(bool moveToBounds = true)
    {
        try
        {
            RefreshMapPins(moveToBounds);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Could not render map pins.");
            _viewModel.ErrorMessage = "No pudimos mostrar los marcadores del mapa. Inténtalo de nuevo.";
        }
    }

    private void MoveToRecommendationBounds(IReadOnlyCollection<RecommendationDto> recommendations)
    {
        if (recommendations.Count == 0)
        {
            return;
        }

        var centerLatitude = recommendations.Average(recommendation => (double)recommendation.Latitude);
        var centerLongitude = recommendations.Average(recommendation => (double)recommendation.Longitude);

        var maxLatitudeDelta = recommendations.Max(recommendation => Math.Abs((double)recommendation.Latitude - centerLatitude));
        var maxLongitudeDelta = recommendations.Max(recommendation => Math.Abs((double)recommendation.Longitude - centerLongitude));
        var radiusKm = Math.Max(2, Math.Max(maxLatitudeDelta, maxLongitudeDelta) * 140);

        _map.MoveToRegion(MapSpan.FromCenterAndRadius(
            new Location(centerLatitude, centerLongitude),
            Distance.FromKilometers(radiusKm)));
    }

    private void FocusRecommendation(RecommendationDto? recommendation)
    {
        if (recommendation is null)
        {
            return;
        }

        var location = new Location((double)recommendation.Latitude, (double)recommendation.Longitude);
        _map.MoveToRegion(MapSpan.FromCenterAndRadius(
            location,
            Distance.FromKilometers(1.2)));
    }

    private void TryFocusRecommendation(RecommendationDto? recommendation)
    {
        try
        {
            FocusRecommendation(recommendation);
        }
        catch (Exception exception)
        {
            _logger.LogDebug(exception, "Could not focus the selected map recommendation.");
        }
    }

    private void UpdateSelectionIndicator()
    {
        if (_selectionIndicator is not null)
        {
            _map.MapElements.Remove(_selectionIndicator);
            _selectionIndicator = null;
        }

        var selected = _viewModel.SelectedRecommendation;
        if (selected is null)
        {
            return;
        }

        _selectionIndicator = new Circle
        {
            Center = new Location((double)selected.Latitude, (double)selected.Longitude),
            Radius = Distance.FromMeters(115),
            StrokeColor = Color.FromArgb("#C59D3E"),
            FillColor = Color.FromArgb("#35C59D3E"),
            StrokeWidth = 5
        };
        _map.MapElements.Add(_selectionIndicator);
    }

#if IOS || MACCATALYST
    private void UpdateApplePinSelection(RecommendationMapPin pin)
    {
        if (_map.Handler?.PlatformView is not MKMapView nativeMap
            || pin.MarkerId is not IMKAnnotation annotation
            || nativeMap.ViewForAnnotation(annotation) is not MKMarkerAnnotationView annotationView)
        {
            return;
        }

        annotationView.MarkerTintColor = pin.IsSelected
            ? UIColor.FromRGB(197, 157, 62)
            : UIColor.SystemRed;
    }

    private void OnMapHandlerChanged(object? sender, EventArgs e)
    {
        if (_map.Handler?.PlatformView is not MKMapView nativeMap)
        {
            return;
        }

        nativeMap.GetViewForAnnotation = CreateAnnotationView;
    }

    private MKAnnotationView? CreateAnnotationView(MKMapView mapView, IMKAnnotation annotation)
    {
        if (annotation is MKUserLocation)
        {
            return null;
        }

        var pin = _map.Pins
            .OfType<RecommendationMapPin>()
            .FirstOrDefault(candidate => ReferenceEquals(candidate.MarkerId, annotation));
        var reuseIdentifier = pin?.IsSelected == true
            ? "selectedRecommendationPin"
            : "recommendationPin";
        var annotationView = mapView.DequeueReusableAnnotation(reuseIdentifier) as MKMarkerAnnotationView
            ?? new MKMarkerAnnotationView(annotation, reuseIdentifier);

        annotationView.Annotation = annotation;
        annotationView.CanShowCallout = false;
        // MapKit normally hides colliding annotations as the user zooms out.
        // These pins are the catalog itself, so keep every marker visible and
        // prevent MapKit from grouping them under a clustering identifier.
        annotationView.DisplayPriority = MKFeatureDisplayPriority.Required;
        annotationView.ClusteringIdentifier = null;
        annotationView.MarkerTintColor = pin?.IsSelected == true
            ? UIColor.FromRGB(197, 157, 62)
            : UIColor.SystemRed;

        return annotationView;
    }
#endif
#endif

    private void OnRecommendationTapped(object? sender, TappedEventArgs e)
    {
        DismissSearchKeyboard();
        if ((sender as BindableObject)?.BindingContext is RecommendationDto recommendation)
        {
            _viewModel.SelectRecommendationCommand.Execute(recommendation);
        }
    }

    private async void OnSearchSubmitted(object? sender, EventArgs e)
    {
        try
        {
            CancelSearchDebounce();
            DismissSearchKeyboard();
            await _viewModel.SearchCommand.ExecuteAsync(null);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Map search submission failed.");
            _viewModel.StatusMessage = "No pudimos completar la búsqueda. Inténtalo nuevamente.";
        }
    }

    private async void OnSearchTextChanged(object? sender, TextChangedEventArgs e)
    {
        CancelSearchDebounce();
        if (!PlaceSearch.IsFocused)
        {
            return;
        }

        var expectedText = e.NewTextValue ?? string.Empty;
        var debounce = new CancellationTokenSource();
        _searchDebounce = debounce;
        try
        {
            await Task.Delay(250, debounce.Token);
            if (!debounce.IsCancellationRequested
                && PlaceSearch.IsFocused
                && string.Equals(PlaceSearch.Text ?? string.Empty, expectedText, StringComparison.Ordinal))
            {
                await _viewModel.SearchCommand.ExecuteAsync(null);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected while the user continues typing or leaves the page.
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Map search failed while typing.");
            _viewModel.StatusMessage = "No pudimos completar la búsqueda. Inténtalo nuevamente.";
        }
        finally
        {
            if (ReferenceEquals(_searchDebounce, debounce))
            {
                _searchDebounce = null;
            }
            debounce.Dispose();
        }
    }

    private void OnSearchFocused(object? sender, FocusEventArgs e)
    {
        _isSearchPanelExpanded = true;
        UpdateSearchPanelLayout();
    }

    private void OnSearchUnfocused(object? sender, FocusEventArgs e)
    {
        _isSearchPanelExpanded = false;
        UpdateSearchPanelLayout();
#if !WINDOWS
        if (_mapPinsRefreshPending)
        {
            _mapPinsRefreshPending = false;
            TryRefreshMapPins();
        }
#endif
    }

    private void OnPageLayoutSizeChanged(object? sender, EventArgs e)
    {
        if (_isSearchPanelExpanded)
        {
            UpdateSearchPanelLayout();
        }
    }

    private void UpdateSearchPanelLayout()
    {
        if (_isSearchPanelExpanded)
        {
            if (Grid.GetRow(RecommendationBrowser) != 0)
            {
                Grid.SetRow(RecommendationBrowser, 0);
                Grid.SetRowSpan(RecommendationBrowser, 2);
                RecommendationBrowser.VerticalOptions = LayoutOptions.Start;
            }
            var availableHeight = RootLayout.Height > 0 ? RootLayout.Height : Height;
            var desiredHeight = Math.Clamp(
                availableHeight * 0.68,
                MinimumExpandedSearchPanelHeight,
                MaximumExpandedSearchPanelHeight);
            var expandedHeight = Math.Min(
                desiredHeight,
                Math.Max(260, availableHeight - 8));
            if (Math.Abs(RecommendationBrowser.HeightRequest - expandedHeight) > 1)
            {
                RecommendationBrowser.HeightRequest = expandedHeight;
            }
            return;
        }

        if (Grid.GetRow(RecommendationBrowser) != 1)
        {
            Grid.SetRow(RecommendationBrowser, 1);
            Grid.SetRowSpan(RecommendationBrowser, 1);
            RecommendationBrowser.VerticalOptions = LayoutOptions.Fill;
        }
        if (Math.Abs(RecommendationBrowser.HeightRequest - CollapsedSearchPanelHeight) > 1)
        {
            RecommendationBrowser.HeightRequest = CollapsedSearchPanelHeight;
        }
    }

    private void CancelSearchDebounce()
    {
        var debounce = Interlocked.Exchange(ref _searchDebounce, null);
        if (debounce is null)
        {
            return;
        }

        debounce.Cancel();
    }

    private async void DismissSearchKeyboard()
    {
        try { await PlaceSearch.HideSoftInputAsync(CancellationToken.None); }
        catch (Exception exception) { _logger.LogDebug(exception, "Could not dismiss map search keyboard."); }
        finally { PlaceSearch.Unfocus(); }
    }

    protected override bool OnBackButtonPressed()
    {
        if (PlaceSearch.IsFocused)
        {
            DismissSearchKeyboard();
            return true;
        }
        return base.OnBackButtonPressed();
    }
}

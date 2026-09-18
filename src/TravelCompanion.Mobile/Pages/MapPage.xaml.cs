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
    private readonly MapViewModel _viewModel;
    private readonly ILogger<MapPage> _logger;

#if !WINDOWS
    private readonly MauiMap _map;
    private readonly Dictionary<Pin, EventHandler<PinClickedEventArgs>> _pinHandlers = new();
    private readonly Dictionary<string, RecommendationMapPin> _pinsBySelectionKey = new(StringComparer.Ordinal);
    private bool _isSubscribedToRecommendations;
    private bool _hasRenderedPins;
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
            IsShowingUser = true,
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

        if (_viewModel.HasLoaded)
        {
#if !WINDOWS
            if (!_hasRenderedPins)
            {
                RefreshMapPins();
            }
#endif
            stopwatch.Stop();
            _logger.LogInformation(
                "Map page appeared from warm state in {ElapsedMs}ms.",
                stopwatch.Elapsed.TotalMilliseconds);
            return;
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
                "Map page appeared after initial load in {ElapsedMs}ms. HasLoaded={HasLoaded}.",
                stopwatch.Elapsed.TotalMilliseconds,
                _viewModel.HasLoaded);
        }
    }

    protected override void OnDisappearing()
    {
        _viewModel.CancelLoading();
        DismissSearchKeyboard();
        base.OnDisappearing();
#if !WINDOWS
        UnsubscribeFromRecommendations();
        _hasRenderedPins = false;
#endif
    }

    private async void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MapViewModel.VisibleNearbyRecommendations))
        {
#if !WINDOWS
            RefreshMapPins();
#endif
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
        else if (e.PropertyName == nameof(MapViewModel.SelectedRecommendation))
        {
            DismissSearchKeyboard();
            try
            {
                RefreshMapPins(moveToBounds: false);
                FocusRecommendation(_viewModel.SelectedRecommendation);
            }
            catch (Exception exception)
            {
                _logger.LogDebug(exception, "Could not focus the selected map recommendation.");
            }
        }
#endif
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
        var recommendationsByKey = _viewModel.VisibleNearbyRecommendations
            .ToDictionary(recommendation => recommendation.SelectionKey, StringComparer.Ordinal);
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
                pin = new RecommendationMapPin { Type = PinType.Place };
                EventHandler<PinClickedEventArgs> handler = (_, args) =>
                {
                    args.HideInfoWindow = true;
                    void SelectPin()
                    {
                        var current = _viewModel.VisibleNearbyRecommendations
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

            pin.Label = recommendation.Title;
            pin.Address = recommendation.Neighborhood;
            pin.Location = new Location((double)recommendation.Latitude, (double)recommendation.Longitude);
            pin.IsSelected = selectionKey == _viewModel.SelectedRecommendation?.SelectionKey;
        }

        if (moveToBounds)
        {
            MoveToRecommendationBounds(_viewModel.VisibleNearbyRecommendations);
        }

        _hasRenderedPins = true;
        stopwatch.Stop();
        _logger.LogInformation(
            "Map pins refreshed in {ElapsedMs}ms. Pins={PinCount}.",
            stopwatch.Elapsed.TotalMilliseconds,
            _viewModel.VisibleNearbyRecommendations.Count);
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

#if IOS || MACCATALYST
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
        DismissSearchKeyboard();
        await _viewModel.SearchCommand.ExecuteAsync(null);
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

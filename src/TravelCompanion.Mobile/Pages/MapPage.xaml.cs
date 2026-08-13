using System;
using System.ComponentModel;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using TravelCompanion.Mobile.ViewModels;
using TravelCompanion.Shared.Dtos;

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
        else if (e.PropertyName == nameof(MapViewModel.SelectedRecommendation)
                 && _viewModel.SelectedRecommendation is { } selectedRecommendation)
        {
            try
            {
                FocusRecommendation(selectedRecommendation);
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

    private void RefreshMapPins()
    {
        var stopwatch = Stopwatch.StartNew();
        // Unsubscribe all existing pin event handlers to prevent memory leaks
        foreach (var (pin, handler) in _pinHandlers)
        {
            pin.MarkerClicked -= handler;
        }
        _pinHandlers.Clear();
        _map.Pins.Clear();

        foreach (var recommendation in _viewModel.VisibleNearbyRecommendations)
        {
            var pin = new Pin
            {
                Label = recommendation.Title,
                Address = recommendation.Neighborhood,
                Type = PinType.Place,
                Location = new Location((double)recommendation.Latitude, (double)recommendation.Longitude)
            };

            // Store handler reference to enable proper cleanup
            EventHandler<PinClickedEventArgs> handler = (_, args) =>
            {
                args.HideInfoWindow = true;
                void SelectPin() => _viewModel.SelectRecommendationCommand.Execute(recommendation);
                if (Dispatcher.IsDispatchRequired)
                {
                    Dispatcher.Dispatch(SelectPin);
                }
                else
                {
                    SelectPin();
                }
            };

            _pinHandlers[pin] = handler;
            pin.MarkerClicked += handler;
            _map.Pins.Add(pin);
        }

        MoveToRecommendationBounds(_viewModel.VisibleNearbyRecommendations);
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

    private void FocusRecommendation(RecommendationDto recommendation)
    {
        _map.MoveToRegion(MapSpan.FromCenterAndRadius(
            new Location((double)recommendation.Latitude, (double)recommendation.Longitude),
            Distance.FromKilometers(1.2)));
    }
#endif

    private async void OnRecommendationTapped(object? sender, TappedEventArgs e)
    {
        if ((sender as BindableObject)?.BindingContext is RecommendationDto recommendation)
        {
            await _viewModel.OpenRecommendationCommand.ExecuteAsync(recommendation);
        }
    }
}

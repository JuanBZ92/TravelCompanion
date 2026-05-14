using System.Collections.Specialized;
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
    private bool _loaded;

#if !WINDOWS
    private readonly MauiMap _map;
    private readonly Dictionary<Pin, EventHandler<PinClickedEventArgs>> _pinHandlers = new();
#endif

    public MapPage()
        : this(MauiProgram.Services.GetRequiredService<MapViewModel>())
    {
    }

    public MapPage(MapViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
        _viewModel = viewModel;
        _viewModel.NearbyRecommendations.CollectionChanged += OnNearbyRecommendationsChanged;

#if !WINDOWS
        _map = new MauiMap(MapSpan.FromCenterAndRadius(
            new Location(35.681236, 139.767125),
            Distance.FromKilometers(8)))
        {
            IsShowingUser = true,
            MapType = MapType.Street
        };

        MapContainer.Children.Clear();
        MapContainer.Children.Add(_map);
#endif
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        if (_loaded)
        {
            return;
        }

        _loaded = true;
        await _viewModel.LoadNearbyRecommendationsCommand.ExecuteAsync(null);
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();

        // Unsubscribe from collection changed to prevent memory leak
        _viewModel.NearbyRecommendations.CollectionChanged -= OnNearbyRecommendationsChanged;

#if !WINDOWS
        // Clean up all pin event handlers to prevent memory leaks
        foreach (var (pin, handler) in _pinHandlers)
        {
            pin.MarkerClicked -= handler;
        }
        _pinHandlers.Clear();
#endif
    }

    private void OnNearbyRecommendationsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
#if !WINDOWS
        RefreshMapPins();
#endif
    }

#if !WINDOWS
    private void RefreshMapPins()
    {
        // Unsubscribe all existing pin event handlers to prevent memory leaks
        foreach (var (pin, handler) in _pinHandlers)
        {
            pin.MarkerClicked -= handler;
        }
        _pinHandlers.Clear();
        _map.Pins.Clear();

        foreach (var recommendation in _viewModel.NearbyRecommendations)
        {
            var pin = new Pin
            {
                Label = recommendation.Title,
                Address = recommendation.Neighborhood,
                Type = PinType.Place,
                Location = new Location((double)recommendation.Latitude, (double)recommendation.Longitude)
            };

            // Store handler reference to enable proper cleanup
            EventHandler<PinClickedEventArgs> handler = async (_, args) =>
            {
                args.HideInfoWindow = false;
                await OpenRecommendationAsync(recommendation);
            };

            _pinHandlers[pin] = handler;
            pin.MarkerClicked += handler;
            _map.Pins.Add(pin);
        }

        MoveToRecommendationBounds(_viewModel.NearbyRecommendations);
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

    private Task OpenRecommendationAsync(RecommendationDto recommendation)
    {
        return _viewModel.OpenRecommendationCommand.ExecuteAsync(recommendation);
    }
#endif
}

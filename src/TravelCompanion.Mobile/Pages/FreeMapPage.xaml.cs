using TravelCompanion.Mobile.Controls;
using System.ComponentModel;
using TravelCompanion.Mobile.ViewModels;
using TravelCompanion.Shared.Dtos;

#if !WINDOWS
using MauiMap = Microsoft.Maui.Controls.Maps.Map;
using Microsoft.Maui.Controls.Maps;
using Microsoft.Maui.Maps;
#endif

namespace TravelCompanion.Mobile.Pages;

public partial class FreeMapPage : ContentPage
{
    private readonly FreeMapViewModel _viewModel;

#if !WINDOWS
    private readonly MauiMap _map;
    private readonly Dictionary<Pin, EventHandler<PinClickedEventArgs>> _pinHandlers =
        new(ReferenceEqualityComparer.Instance);
#endif

    public FreeMapPage()
        : this(MauiProgram.Services.GetRequiredService<FreeMapViewModel>())
    {
    }

    public FreeMapPage(FreeMapViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;

#if !WINDOWS
        _map = new MauiMap
        {
            MapType = MapType.Street,
            IsShowingUser = false
        };
        MapContainer.Children.Insert(0, _map);
        MapFallback.IsVisible = false;
#endif
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        await _viewModel.LoadCommand.ExecuteAsync(null);
        RefreshMap();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
#if !WINDOWS
        ClearPins();
#endif
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(FreeMapViewModel.Preview))
        {
            RefreshMap();
        }
        else if (e.PropertyName == nameof(FreeMapViewModel.SelectedMarker))
        {
            FocusSelectedMarker();
        }
    }

    private void RefreshMap()
    {
#if !WINDOWS
        var preview = _viewModel.Preview;
        if (preview is null)
        {
            return;
        }

        ClearPins();
        _map.MapElements.Clear();

        var center = new Location(
            (double)preview.City.CenterLatitude,
            (double)preview.City.CenterLongitude);
        _map.MapElements.Add(new Circle
        {
            Center = center,
            Radius = Distance.FromKilometers((double)preview.City.FreeRadiusKm),
            StrokeColor = Color.FromArgb("#8A6F3D"),
            FillColor = Color.FromArgb("#268A6F3D"),
            StrokeWidth = 4
        });

        foreach (var marker in preview.Markers)
        {
            var isUnlocked = marker.Access == FreeMapMarkerAccess.Unlocked;
            var pin = new RecommendationMapPin
            {
                Label = isUnlocked ? marker.Recommendation?.Title ?? "YUKU" : "Contenido YUKU",
                Address = isUnlocked ? marker.Recommendation?.Neighborhood ?? string.Empty : string.Empty,
                BindingContext = marker,
                IsSelected = ReferenceEquals(marker, _viewModel.SelectedMarker),
                Type = isUnlocked ? PinType.Place : PinType.Generic,
                Location = new Location((double)marker.Latitude, (double)marker.Longitude)
            };
            EventHandler<PinClickedEventArgs> handler = (_, args) =>
            {
                args.HideInfoWindow = true;
                _viewModel.SelectMarker(marker);
            };
            pin.MarkerClicked += handler;
            _pinHandlers[pin] = handler;
            _map.Pins.Add(pin);

            if (!isUnlocked)
            {
                _map.MapElements.Add(new Circle
                {
                    Center = pin.Location,
                    Radius = Distance.FromMeters(45),
                    StrokeColor = Color.FromArgb("#AD8A3A"),
                    FillColor = Color.FromArgb("#35AD8A3A"),
                    StrokeWidth = 1
                });
            }
        }

        var radiusKm = Math.Max(4.5, (double)preview.City.FreeRadiusKm * 2.5);
        foreach (var marker in preview.Markers)
            radiusKm = Math.Max(radiusKm, Location.CalculateDistance(center,
                new Location((double)marker.Latitude, (double)marker.Longitude), DistanceUnits.Kilometers) * 1.15);
        _map.MoveToRegion(MapSpan.FromCenterAndRadius(center, Distance.FromKilometers(radiusKm)));
#endif
    }

    private void FocusSelectedMarker()
    {
#if !WINDOWS
        foreach (var pin in _map.Pins.OfType<RecommendationMapPin>().ToList())
        {
            var selected = ReferenceEquals(pin.BindingContext, _viewModel.SelectedMarker);
            if (pin.IsSelected == selected) continue;
            pin.IsSelected = selected;
            pin.Handler?.UpdateValue(nameof(RecommendationMapPin.IsSelected));
            _map.Pins.Remove(pin);
            _map.Pins.Add(pin);
        }

        var marker = _viewModel.SelectedMarker;
        if (marker is null)
        {
            return;
        }

        var location = new Location((double)marker.Latitude, (double)marker.Longitude);
        _map.MoveToRegion(MapSpan.FromCenterAndRadius(location, Distance.FromKilometers(1.2)));
#endif
    }

#if !WINDOWS
    private void ClearPins()
    {
        foreach (var (pin, handler) in _pinHandlers)
        {
            pin.MarkerClicked -= handler;
        }

        _pinHandlers.Clear();
        _map.Pins.Clear();
    }
#endif
}

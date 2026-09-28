using TravelCompanion.Mobile.Controls;
using System.ComponentModel;
using TravelCompanion.Mobile.ViewModels;
using TravelCompanion.Shared.Dtos;

#if !WINDOWS
using MauiMap = TravelCompanion.Mobile.Controls.BatchedMap;
using Microsoft.Maui.Controls.Maps;
using Microsoft.Maui.Maps;
#endif

namespace TravelCompanion.Mobile.Pages;

public partial class FreeMapPage : ContentPage
{
    private readonly FreeMapViewModel _viewModel;
    private bool _isActive;
    private long _appearance;

#if !WINDOWS
    private readonly MauiMap _map;
    private FreeMapPreviewDto? _renderedPreview;
    private string? _focusedMarkerKey;
    private MapSpan? _retainedRegion;
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
        _map.HandlerChanging += (_, _) => _retainedRegion = _map.VisibleRegion ?? _retainedRegion;
        _map.HandlerChanged += (_, _) =>
        {
            var handler = _map.Handler;
            if (_retainedRegion is { } region) Dispatcher.Dispatch(() =>
            {
                if (!_isActive || handler is null || !ReferenceEquals(handler, _map.Handler)) return;
                try { _map.MoveToRegion(region); }
                catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); }
            });
        };
#endif
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        _isActive = true;
        var appearance = ++_appearance;
        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        try
        {
            var load = _viewModel.LoadCommand.ExecuteAsync(null);
            TryUpdateMap();
            await load;
            if (_isActive && appearance == _appearance) TryUpdateMap();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(ex);
            if (_isActive && appearance == _appearance) _viewModel.ErrorMessage = "No pudimos cargar el mapa. Volvé a intentarlo.";
        }
    }

    protected override void OnDisappearing()
    {
        _isActive = false;
        _appearance++;
        _viewModel.CancelLoading();
        base.OnDisappearing();
        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(FreeMapViewModel.Preview) or nameof(FreeMapViewModel.SelectedMarker))
            TryUpdateMap();
    }

    private void TryUpdateMap()
    {
        if (!_isActive) return;
        if (Dispatcher.IsDispatchRequired) { Dispatcher.Dispatch(TryUpdateMap); return; }
        try { RefreshMap(); FocusSelectedMarker(); }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(ex);
#if !WINDOWS
            _renderedPreview = null;
#endif
            _viewModel.ErrorMessage = "No pudimos mostrar el mapa. Volvé a intentarlo.";
        }
    }

    private void RefreshMap()
    {
#if !WINDOWS
        using var update = _map.BeginUpdate();
        var preview = _viewModel.Preview;
        if (ReferenceEquals(preview, _renderedPreview)) return;
        var changedCity = preview?.City.Slug != _renderedPreview?.City.Slug;
        _renderedPreview = preview;
        if (preview is null)
        {
            _focusedMarkerKey = null;
            _retainedRegion = null;
            ClearPins();
            _map.MapElements.Clear();
            return;
        }

        var keys = preview.Markers.Select(marker => marker.MarkerKey).ToHashSet();
        foreach (var pin in _pinHandlers.Keys.ToArray())
        {
            if (pin.BindingContext is FreeMapMarkerDto old && keys.Contains(old.MarkerKey)) continue;
            pin.MarkerClicked -= _pinHandlers[pin];
            _pinHandlers.Remove(pin);
            _map.Pins.Remove(pin);
        }
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

        var pinsByKey = _pinHandlers.Keys.OfType<RecommendationMapPin>()
            .ToDictionary(pin => ((FreeMapMarkerDto)pin.BindingContext).MarkerKey, StringComparer.Ordinal);
        foreach (var marker in preview.Markers)
        {
            var isUnlocked = marker.Access == FreeMapMarkerAccess.Unlocked;
            pinsByKey.TryGetValue(marker.MarkerKey, out var pin);
            if (pin is null)
            {
                pin = new RecommendationMapPin { IsSelected = marker.MarkerKey == _viewModel.SelectedMarker?.MarkerKey };
                var capturedPin = pin;
                EventHandler<PinClickedEventArgs> handler = (_, args) =>
                {
                    args.HideInfoWindow = true;
                    if (_isActive && capturedPin.BindingContext is FreeMapMarkerDto current) _viewModel.SelectMarker(current);
                };
                pin.MarkerClicked += handler;
                _pinHandlers[pin] = handler;
            }
            pin.Label = isUnlocked ? marker.Recommendation?.Title ?? "YUKU" : "Contenido YUKU";
            pin.Address = isUnlocked ? marker.Recommendation?.Neighborhood ?? string.Empty : string.Empty;
            pin.BindingContext = marker;
            pin.Type = isUnlocked ? PinType.Place : PinType.Generic;
            if (pin.Location is null || pin.Location.Latitude != (double)marker.Latitude || pin.Location.Longitude != (double)marker.Longitude)
                pin.Location = new Location((double)marker.Latitude, (double)marker.Longitude);
            if (!_map.Pins.Contains(pin)) _map.Pins.Add(pin);

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
        if (changedCity) _map.MoveToRegion(MapSpan.FromCenterAndRadius(center, Distance.FromKilometers(radiusKm)));
#endif
    }

    private void FocusSelectedMarker()
    {
#if !WINDOWS
        using var update = _map.BeginUpdate();
        foreach (var pin in _map.Pins.OfType<RecommendationMapPin>().ToList())
        {
            var selected = pin.BindingContext is FreeMapMarkerDto current
                && current.MarkerKey == _viewModel.SelectedMarker?.MarkerKey;
            if (pin.IsSelected == selected) continue;
            pin.IsSelected = selected;
            pin.Handler?.UpdateValue(nameof(RecommendationMapPin.IsSelected));
            _map.Pins.Remove(pin);
            _map.Pins.Add(pin);
        }

        var marker = _viewModel.SelectedMarker;
        if (_focusedMarkerKey == marker?.MarkerKey) return;
        _focusedMarkerKey = marker?.MarkerKey;
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

    protected override bool OnBackButtonPressed()
    {
        if (_viewModel.HasSelection)
        {
            _viewModel.CloseSelectionCommand.Execute(null);
            return true;
        }
        return base.OnBackButtonPressed();
    }
}

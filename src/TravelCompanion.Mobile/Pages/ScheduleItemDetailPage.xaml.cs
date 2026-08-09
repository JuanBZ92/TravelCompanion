using System.ComponentModel;
using TravelCompanion.Mobile.ViewModels;
using TravelCompanion.Shared.Dtos;

#if !WINDOWS
using MauiMap = Microsoft.Maui.Controls.Maps.Map;
using Microsoft.Maui.Controls.Maps;
using Microsoft.Maui.Maps;
#endif

namespace TravelCompanion.Mobile.Pages;

public partial class ScheduleItemDetailPage : ContentPage
{
    private readonly ScheduleItemDetailViewModel _viewModel;
    private int _mapRequestVersion;
    private bool _isSubscribed;

#if !WINDOWS
    private readonly MauiMap _map;
#endif

    public ScheduleItemDetailPage()
        : this(MauiProgram.Services.GetRequiredService<ScheduleItemDetailViewModel>())
    {
    }

    public ScheduleItemDetailPage(ScheduleItemDetailViewModel viewModel)
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
#endif
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        SubscribeToViewModel();
        _ = RefreshMapAsync();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        UnsubscribeFromViewModel();
        _mapRequestVersion++;
    }

    private void SubscribeToViewModel()
    {
        if (_isSubscribed)
        {
            return;
        }

        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        _isSubscribed = true;
    }

    private void UnsubscribeFromViewModel()
    {
        if (!_isSubscribed)
        {
            return;
        }

        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        _isSubscribed = false;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ScheduleItemDetailViewModel.ScheduleItem))
        {
            _ = RefreshMapAsync();
        }
    }

    private async Task RefreshMapAsync()
    {
#if !WINDOWS
        var requestVersion = ++_mapRequestVersion;
        var item = _viewModel.ScheduleItem;
        if (item is null || string.IsNullOrWhiteSpace(item.Address))
        {
            return;
        }

        try
        {
            var locations = await Geocoding.Default.GetLocationsAsync(item.Address);
            var location = locations?.FirstOrDefault();
            if (requestVersion != _mapRequestVersion || location is null)
            {
                return;
            }

            _map.Pins.Clear();
            _map.Pins.Add(CreatePin(item, location));
            _map.MoveToRegion(MapSpan.FromCenterAndRadius(location, Distance.FromKilometers(1.2)));
            MapFallback.IsVisible = false;
        }
        catch
        {
            MapFallback.IsVisible = true;
        }
#else
        await Task.CompletedTask;
#endif
    }

#if !WINDOWS
    private static Pin CreatePin(ScheduleItemDto item, Location location) =>
        new()
        {
            Label = string.IsNullOrWhiteSpace(item.LocationName) ? item.Title : item.LocationName,
            Address = item.Address,
            Type = PinType.Place,
            Location = location
        };
#endif
}

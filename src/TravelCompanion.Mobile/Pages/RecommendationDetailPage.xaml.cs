using System.ComponentModel;
using TravelCompanion.Mobile.ViewModels;
using TravelCompanion.Shared.Dtos;

#if !WINDOWS
using MauiMap = Microsoft.Maui.Controls.Maps.Map;
using Microsoft.Maui.Controls.Maps;
using Microsoft.Maui.Maps;
#endif

namespace TravelCompanion.Mobile.Pages;

public partial class RecommendationDetailPage : ContentPage
{
    private readonly RecommendationDetailViewModel _viewModel;
    private bool _isSubscribed;

#if !WINDOWS
    private readonly MauiMap _map;
#endif

    public RecommendationDetailPage()
        : this(MauiProgram.Services.GetRequiredService<RecommendationDetailViewModel>())
    {
    }

    public RecommendationDetailPage(RecommendationDetailViewModel viewModel)
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
        RefreshMap();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        UnsubscribeFromViewModel();
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
        if (e.PropertyName == nameof(RecommendationDetailViewModel.Recommendation))
        {
            RefreshMap();
        }
    }

    private void RefreshMap()
    {
#if !WINDOWS
        var recommendation = _viewModel.Recommendation;
        if (recommendation is null)
        {
            return;
        }

        var location = new Location((double)recommendation.Latitude, (double)recommendation.Longitude);
        _map.Pins.Clear();
        _map.Pins.Add(CreatePin(recommendation, location));
        _map.MoveToRegion(MapSpan.FromCenterAndRadius(location, Distance.FromKilometers(1.2)));
        MapFallback.IsVisible = false;
#endif
    }

    private async void OnBackClicked(object? sender, EventArgs e)
    {
        await Shell.Current.GoToAsync("..");
    }

#if !WINDOWS
    private static Pin CreatePin(RecommendationDto recommendation, Location location) =>
        new()
        {
            Label = recommendation.Title,
            Address = recommendation.Neighborhood,
            Type = PinType.Place,
            Location = location
        };
#endif
}

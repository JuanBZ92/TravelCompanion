using TravelCompanion.Mobile.ViewModels;

namespace TravelCompanion.Mobile.Pages;

public partial class MapPage : ContentPage
{
    private readonly MapViewModel _viewModel;
    private bool _loaded;

    public MapPage()
        : this(MauiProgram.Services.GetRequiredService<MapViewModel>())
    {
    }

    public MapPage(MapViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
        _viewModel = viewModel;
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
}

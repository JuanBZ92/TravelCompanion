using TravelCompanion.Mobile.ViewModels;

namespace TravelCompanion.Mobile.Pages;

public partial class RecommendationsPage : ContentPage
{
    private readonly RecommendationsViewModel _viewModel;
    private bool _loaded;

    public RecommendationsPage()
        : this(MauiProgram.Services.GetRequiredService<RecommendationsViewModel>())
    {
    }

    public RecommendationsPage(RecommendationsViewModel viewModel)
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
        await _viewModel.LoadRecommendationsCommand.ExecuteAsync(null);
    }
}

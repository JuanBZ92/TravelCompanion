using System;
using TravelCompanion.Mobile.ViewModels;

namespace TravelCompanion.Mobile.Pages;

public partial class RecommendationsPage : ContentPage
{
    private readonly RecommendationsViewModel _viewModel;

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

        if (_viewModel.HasLoaded)
        {
            _viewModel.RefreshFavoriteState();
            return;
        }

        try
        {
            await _viewModel.LoadRecommendationsCommand.ExecuteAsync(null);
        }
        catch (Exception ex)
        {
            _viewModel.ErrorMessage = $"Error loading recommendations: {ex.Message}";
        }
    }
}

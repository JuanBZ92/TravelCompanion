using System;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using TravelCompanion.Mobile.ViewModels;

namespace TravelCompanion.Mobile.Pages;

public partial class RecommendationsPage : ContentPage
{
    private readonly RecommendationsViewModel _viewModel;
    private readonly ILogger<RecommendationsPage> _logger;

    public RecommendationsPage()
        : this(
            MauiProgram.Services.GetRequiredService<RecommendationsViewModel>(),
            MauiProgram.Services.GetRequiredService<ILogger<RecommendationsPage>>())
    {
    }

    public RecommendationsPage(
        RecommendationsViewModel viewModel,
        ILogger<RecommendationsPage> logger)
    {
        var stopwatch = Stopwatch.StartNew();
        InitializeComponent();
        stopwatch.Stop();
        BindingContext = viewModel;
        _viewModel = viewModel;
        _logger = logger;

        _logger.LogInformation(
            "Recommendations page initialized in {ElapsedMs}ms. HasLoaded={HasLoaded}.",
            stopwatch.Elapsed.TotalMilliseconds,
            _viewModel.HasLoaded);
    }

    protected override async void OnAppearing()
    {
        var stopwatch = Stopwatch.StartNew();
        base.OnAppearing();

        if (_viewModel.HasLoaded)
        {
            _viewModel.RefreshFavoriteState();
            stopwatch.Stop();
            _logger.LogInformation(
                "Recommendations page appeared from warm state in {ElapsedMs}ms.",
                stopwatch.Elapsed.TotalMilliseconds);
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
        finally
        {
            stopwatch.Stop();
            _logger.LogInformation(
                "Recommendations page appeared after initial load in {ElapsedMs}ms. HasLoaded={HasLoaded}.",
                stopwatch.Elapsed.TotalMilliseconds,
                _viewModel.HasLoaded);
        }
    }

    private async void OnRecommendationTapped(object? sender, TappedEventArgs e)
    {
        if ((sender as BindableObject)?.BindingContext is RecommendationListItemViewModel recommendation)
        {
            await _viewModel.OpenRecommendationCommand.ExecuteAsync(recommendation);
        }
    }

    private void OnFavoriteTapped(object? sender, TappedEventArgs e)
    {
        if ((sender as BindableObject)?.BindingContext is RecommendationListItemViewModel recommendation)
        {
            _viewModel.ToggleFavoriteCommand.Execute(recommendation);
        }
    }

    private void OnCategoryFilterTapped(object? sender, TappedEventArgs e)
    {
        if ((sender as BindableObject)?.BindingContext is CategoryFilterViewModel filter)
        {
            _viewModel.SelectCategoryCommand.Execute(filter);
        }
    }
}

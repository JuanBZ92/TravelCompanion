using System;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using TravelCompanion.Mobile.ViewModels;

namespace TravelCompanion.Mobile.Pages;

public partial class PackagesPage : ContentPage
{
    private readonly PackagesViewModel _viewModel;
    private readonly ILogger<PackagesPage> _logger;

    public PackagesPage()
        : this(
            MauiProgram.Services.GetRequiredService<PackagesViewModel>(),
            MauiProgram.Services.GetRequiredService<ILogger<PackagesPage>>())
    {
    }

    public PackagesPage(
        PackagesViewModel viewModel,
        ILogger<PackagesPage> logger)
    {
        var stopwatch = Stopwatch.StartNew();
        InitializeComponent();
        stopwatch.Stop();
        BindingContext = viewModel;
        _viewModel = viewModel;
        _logger = logger;

        _logger.LogInformation(
            "Packages page initialized in {ElapsedMs}ms. HasLoaded={HasLoaded}.",
            stopwatch.Elapsed.TotalMilliseconds,
            _viewModel.HasLoaded);
    }

    protected override async void OnAppearing()
    {
        var stopwatch = Stopwatch.StartNew();
        base.OnAppearing();

        if (_viewModel.HasLoaded)
        {
            stopwatch.Stop();
            _logger.LogInformation(
                "Packages page appeared from warm state in {ElapsedMs}ms.",
                stopwatch.Elapsed.TotalMilliseconds);
            return;
        }

        try
        {
            await _viewModel.LoadPackagesCommand.ExecuteAsync(null);
        }
        catch (Exception ex)
        {
            _viewModel.ErrorMessage = $"Error loading packages: {ex.Message}";
        }
        finally
        {
            stopwatch.Stop();
            _logger.LogInformation(
                "Packages page appeared after initial load in {ElapsedMs}ms. HasLoaded={HasLoaded}.",
                stopwatch.Elapsed.TotalMilliseconds,
                _viewModel.HasLoaded);
        }
    }
}

using System;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using TravelCompanion.Mobile.ViewModels;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Mobile.Pages;

public partial class SchedulePage : ContentPage
{
    private readonly ScheduleViewModel _viewModel;
    private readonly ILogger<SchedulePage> _logger;

    public SchedulePage()
        : this(
            MauiProgram.Services.GetRequiredService<ScheduleViewModel>(),
            MauiProgram.Services.GetRequiredService<ILogger<SchedulePage>>())
    {
    }

    public SchedulePage(
        ScheduleViewModel viewModel,
        ILogger<SchedulePage> logger)
    {
        var stopwatch = Stopwatch.StartNew();
        InitializeComponent();
        stopwatch.Stop();
        BindingContext = viewModel;
        _viewModel = viewModel;
        _logger = logger;

        _logger.LogInformation(
            "Schedule page initialized in {ElapsedMs}ms. HasLoaded={HasLoaded}.",
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
                "Schedule page appeared from warm state in {ElapsedMs}ms.",
                stopwatch.Elapsed.TotalMilliseconds);
            return;
        }

        try
        {
            await _viewModel.LoadScheduleCommand.ExecuteAsync(null);
        }
        catch (Exception ex)
        {
            _viewModel.ErrorMessage = $"Error loading schedule: {ex.Message}";
        }
        finally
        {
            stopwatch.Stop();
            _logger.LogInformation(
                "Schedule page appeared after initial load in {ElapsedMs}ms. HasLoaded={HasLoaded}.",
                stopwatch.Elapsed.TotalMilliseconds,
                _viewModel.HasLoaded);
        }
    }

    private void OnTypeFilterTapped(object? sender, TappedEventArgs e)
    {
        if ((sender as BindableObject)?.BindingContext is ScheduleTypeFilterViewModel filter)
        {
            _viewModel.ToggleTypeFilterCommand.Execute(filter.Type);
        }
    }

    private void OnCityFilterTapped(object? sender, TappedEventArgs e)
    {
        if ((sender as BindableObject)?.BindingContext is CityFilterViewModel filter)
        {
            _viewModel.ToggleCityFilterCommand.Execute(filter.CityName);
        }
    }

    private void OnDayFilterTapped(object? sender, TappedEventArgs e)
    {
        if ((sender as BindableObject)?.BindingContext is ScheduleDayFilterViewModel day)
        {
            _viewModel.SelectDayCommand.Execute(day);
        }
    }

    private async void OnScheduleItemTapped(object? sender, TappedEventArgs e)
    {
        if ((sender as BindableObject)?.BindingContext is ScheduleItemDto item)
        {
            await _viewModel.OpenScheduleItemCommand.ExecuteAsync(item);
        }
    }

    private async void OnTimelineItemTapped(object? sender, TappedEventArgs e)
    {
        if ((sender as BindableObject)?.BindingContext is ScheduleTimelineItemViewModel timelineItem)
        {
            await _viewModel.OpenScheduleItemCommand.ExecuteAsync(timelineItem.Item);
        }
    }

    private async void OnTodayLocationTapped(object? sender, TappedEventArgs e)
    {
        if ((sender as BindableObject)?.BindingContext is TodayLocationViewModel location)
        {
            await _viewModel.OpenRecommendationCommand.ExecuteAsync(location.Recommendation);
        }
    }

    private async void OnTodayReservationTapped(object? sender, TappedEventArgs e)
    {
        if ((sender as BindableObject)?.BindingContext is TodayReservationViewModel reservation)
        {
            await _viewModel.OpenScheduleItemCommand.ExecuteAsync(reservation.Item);
        }
    }
}

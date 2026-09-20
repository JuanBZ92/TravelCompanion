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
    private bool _isHandlingAppearance;
    private IDispatcherTimer? _trialTimer;
    public ScheduleViewModel ViewModel => _viewModel;

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
        _viewModel = viewModel;
        _logger = logger;
        InitializeComponent();
        stopwatch.Stop();
        BindingContext = viewModel;

        _logger.LogInformation(
            "Schedule page initialized in {ElapsedMs}ms. HasLoaded={HasLoaded}.",
            stopwatch.Elapsed.TotalMilliseconds,
            _viewModel.HasLoaded);
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        if (_isHandlingAppearance)
        {
            return;
        }

        _isHandlingAppearance = true;
        StartTrialTimer();
        await Dispatcher.DispatchAsync(HandleAppearingAsync);
    }

    protected override void OnDisappearing()
    {
        _viewModel.CancelLoading();
        _trialTimer?.Stop();
        base.OnDisappearing();
    }

    private void StartTrialTimer()
    {
        _trialTimer ??= Dispatcher.CreateTimer();
        _trialTimer.Interval = TimeSpan.FromSeconds(1);
        _trialTimer.Tick -= OnTrialTimerTick;
        _trialTimer.Tick += OnTrialTimerTick;
        _viewModel.RefreshTrialCountdown();
        _trialTimer.Start();
    }

    private void OnTrialTimerTick(object? sender, EventArgs e) => _viewModel.RefreshTrialCountdown();

    private async Task HandleAppearingAsync()
    {
        var stopwatch = Stopwatch.StartNew();

        var sessionService = MauiProgram.Services.GetRequiredService<TravelCompanion.Mobile.Services.AuthSessionService>();
        try
        {
            if (sessionService.IsBuilder && sessionService.RequiresTripSetup)
            {
                // Let Shell finish selecting the tab before opening the setup route.
                await Task.Yield();
                await Shell.Current.GoToAsync(nameof(BuilderSetupPage));
                return;
            }

            if (_viewModel.HasLoaded
                && !_viewModel.ShowTodayLoading
                && _viewModel.HasFreshVisibleData)
            {
                stopwatch.Stop();
                _logger.LogInformation(
                    "Schedule page appeared from warm state in {ElapsedMs}ms.",
                    stopwatch.Elapsed.TotalMilliseconds);
                return;
            }

            await _viewModel.LoadScheduleCommand.ExecuteAsync(null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Schedule appearance flow failed.");
            _viewModel.ErrorMessage = sessionService.RequiresTripSetup
                ? "No pudimos abrir la configuración del viaje. Intenta nuevamente."
                : "No pudimos cargar Today. Intenta nuevamente.";
        }
        finally
        {
            _isHandlingAppearance = false;
            stopwatch.Stop();
            _logger.LogInformation(
                "Schedule page appeared after initial load in {ElapsedMs}ms. HasLoaded={HasLoaded}.",
                stopwatch.Elapsed.TotalMilliseconds,
                _viewModel.HasLoaded);
        }
    }

    private void OnDayFilterTapped(object? sender, TappedEventArgs e)
    {
        if ((sender as BindableObject)?.BindingContext is ScheduleDayFilterViewModel day)
        {
            _viewModel.SelectDayCommand.ExecuteAsync(day);
        }
    }

    private async void OnItineraryMenuClicked(object? sender, EventArgs e)
    {
        if (!_viewModel.HasItineraryActions)
        {
            return;
        }

        var actions = new List<string>
        {
            "Guardar para usar sin conexión",
            "Compartir itinerario"
        };
        if (_viewModel.CanManageItinerary)
        {
            actions.Insert(0, "Editar itinerario");
            actions.Add("Eliminar itinerario");
        }
        var action = await DisplayActionSheetAsync(
            "Mi itinerario",
            "Cancelar",
            null,
            actions.ToArray());
        if (action == "Editar itinerario")
        {
            await _viewModel.EditItineraryCommand.ExecuteAsync(null);
        }
        else if (action == "Eliminar itinerario")
        {
            await _viewModel.DeleteItineraryCommand.ExecuteAsync(null);
        }
        else if (action == "Guardar para usar sin conexión")
        {
            await _viewModel.DownloadOfflineCommand.ExecuteAsync(null);
        }
        else if (action == "Compartir itinerario")
        {
            await _viewModel.ShareItineraryCommand.ExecuteAsync(null);
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

    private async void OnEditPersonalItemClicked(object? sender, EventArgs e)
    {
        if ((sender as BindableObject)?.BindingContext is TodayReservationViewModel reservation)
            await _viewModel.EditPersonalItemCommand.ExecuteAsync(reservation);
    }

    private async void OnDeletePersonalItemClicked(object? sender, EventArgs e)
    {
        if ((sender as BindableObject)?.BindingContext is TodayReservationViewModel reservation)
            await _viewModel.DeletePersonalItemCommand.ExecuteAsync(reservation);
    }

    private async void OnTodayLocationVisitedClicked(object? sender, EventArgs e)
    {
        if ((sender as BindableObject)?.BindingContext is TodayLocationViewModel location)
        {
            await _viewModel.MarkLocationVisitedCommand.ExecuteAsync(location);
        }
    }

    private async void OnTodayLocationDismissClicked(object? sender, EventArgs e)
    {
        if ((sender as BindableObject)?.BindingContext is TodayLocationViewModel location)
        {
            await _viewModel.DismissLocationCommand.ExecuteAsync(location);
        }
    }

    private async void OnRemovePersonalRecommendationClicked(object? sender, EventArgs e)
    {
        if ((sender as BindableObject)?.BindingContext is TodayLocationViewModel location)
        {
            await _viewModel.RemovePersonalRecommendationCommand.ExecuteAsync(location);
        }
    }
}

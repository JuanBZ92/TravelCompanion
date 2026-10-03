using System;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using TravelCompanion.Mobile.ViewModels;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Mobile.Pages;

public partial class SchedulePage : ContentPage, IQueryAttributable
{
    private async void OnExpensesSectionClicked(object? sender, EventArgs e) => await ShowExpensesAsync();
    private void OnItinerarySectionClicked(object? sender, EventArgs e)
    {
        FolderPanelView.Deactivate(); FolderPanelView.IsVisible = false;
        FolderSectionButton.TextColor = ExpenseUi.Muted;
        ExpensesPanelView.Deactivate(); ExpensesPanelView.IsVisible = false; ItineraryContent.IsVisible = true;
        ItinerarySectionButton.TextColor = ExpenseUi.Ink; ExpensesSectionButton.TextColor = ExpenseUi.Muted;
    }
    private async Task ShowExpensesAsync()
    {
        FolderPanelView.Deactivate(); FolderPanelView.IsVisible = false;
        FolderSectionButton.TextColor = ExpenseUi.Muted;
        ItineraryContent.IsVisible = false; ExpensesPanelView.IsVisible = true;
        ItinerarySectionButton.TextColor = ExpenseUi.Muted; ExpensesSectionButton.TextColor = ExpenseUi.Ink;
        await ExpensesPanelView.ActivateAsync();
    }
    private async void OnFolderSectionClicked(object? sender, EventArgs e)
    {
        ExpensesPanelView.Deactivate(); ExpensesPanelView.IsVisible = false;
        ItineraryContent.IsVisible = false; FolderPanelView.IsVisible = true;
        ItinerarySectionButton.TextColor = ExpenseUi.Muted;
        ExpensesSectionButton.TextColor = ExpenseUi.Muted;
        FolderSectionButton.TextColor = ExpenseUi.Ink;
        await FolderPanelView.ActivateAsync();
    }
    protected override bool OnBackButtonPressed()
    {
        if (!ExpensesPanelView.IsVisible && !FolderPanelView.IsVisible) return base.OnBackButtonPressed();
        OnItinerarySectionClicked(this, EventArgs.Empty); return true;
    }
    private async void OnDocumentsClicked(object? sender, EventArgs e) => await Shell.Current.GoToAsync(nameof(DocsPage));
    private readonly ScheduleViewModel _viewModel;
    private readonly ILogger<SchedulePage> _logger;
    private bool _isHandlingAppearance;
    private DateOnly? _initialDate;
    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("InitialDate", out var value) && value is DateOnly date) _initialDate = date;
        if (query.TryGetValue("ShowExpenses", out var expenses) && expenses is true) Dispatcher.Dispatch(async () => await ShowExpensesAsync());
    }

    private IDispatcherTimer? _accessTimer;
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
#if ANDROID
        Platforms.Android.TopInsetCorrection.Observe(ScheduleRoot, ScheduleHeader);
#endif
        stopwatch.Stop();
        BindingContext = viewModel;
        ItinerarySectionButton.Text = ExpenseUi.T("Itinerario", "Itinerary");
        ExpensesSectionButton.Text = ExpenseUi.T("Gastos", "Expenses");
        FolderSectionButton.Text = ExpenseUi.T("Carpeta", "Folder");

        _logger.LogInformation(
            "Schedule page initialized in {ElapsedMs}ms. HasLoaded={HasLoaded}.",
            stopwatch.Elapsed.TotalMilliseconds,
            _viewModel.HasLoaded);
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        _viewModel.RefreshDayConfirmation();
        StartAccessTimer();
        if (ExpensesPanelView.IsVisible) await ExpensesPanelView.ActivateAsync();
        if (FolderPanelView.IsVisible) { await FolderPanelView.ActivateAsync(); return; }

        if (_isHandlingAppearance)
        {
            return;
        }

        _isHandlingAppearance = true;
        StartAccessTimer();
        await Dispatcher.DispatchAsync(HandleAppearingAsync);
    }

    protected override void OnDisappearing()
    {
        ExpensesPanelView.Deactivate();
        FolderPanelView.Deactivate();
        _viewModel.CancelLoading();
        _accessTimer?.Stop();
        base.OnDisappearing();
    }

    private void StartAccessTimer()
    {
        _accessTimer ??= Dispatcher.CreateTimer();
        _accessTimer.Interval = TimeSpan.FromSeconds(1);
        _accessTimer.Tick -= OnAccessTimerTick;
        _accessTimer.Tick += OnAccessTimerTick;
        _viewModel.RefreshAccessState();
        _accessTimer.Start();
    }

    private void OnAccessTimerTick(object? sender, EventArgs e) => _viewModel.RefreshAccessState();

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
                await TravelCompanion.Mobile.Services.BuilderSetupNavigation.OpenAsync();
                return;
            }

            if (_initialDate is null && _viewModel.HasLoaded
                && !_viewModel.ShowTodayLoading
                && _viewModel.HasFreshVisibleData)
            {
                await _viewModel.RefreshFocusDocumentAsync();
                stopwatch.Stop();
                _logger.LogInformation(
                    "Schedule page appeared from warm state in {ElapsedMs}ms.",
                    stopwatch.Elapsed.TotalMilliseconds);
                return;
            }

            await _viewModel.LoadScheduleCommand.ExecuteAsync(null);
            if (_initialDate is { } date)
            {
                _initialDate = null;
                await _viewModel.SelectInitialDateAsync(date);
            }
            if (sessionService.IsBuilder && !sessionService.IsFreeMapPreview)
                await MauiProgram.Services.GetRequiredService<TravelCompanion.Mobile.Services.ProductAnalyticsTracker>().TrackAsync("paid_trip_opened", "today");
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
            try { await _viewModel.UpdateOfflineStatusAsync(); } catch { /* Optional download status must not block the itinerary. */ }
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
        var actions = new List<string>
        {
            "Documentos del viaje",
            "Revisar este día",
            "Revisar mi viaje",
            "Guardar para usar sin conexión",
            "Compartir itinerario"
        };
        var remindersLabel = System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "en"
            ? "Reservation reminders" : "Recordatorios de reservas";
        actions.Add(remindersLabel);
        var preparationLabel = TravelCompanion.Mobile.Services.LocalizationResourceManager.Instance["PreparationTitle"];
        actions.Insert(0, preparationLabel);
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
        if (action == preparationLabel)
            await Shell.Current.GoToAsync(nameof(TripPreparationPage));
        else if (action == "Documentos del viaje")
            await Shell.Current.GoToAsync(nameof(DocsPage));
        else if (action == "Revisar este día")
            await _viewModel.ReviewSelectedDayCommand.ExecuteAsync(null);
        else if (action == "Revisar mi viaje")
            await _viewModel.ReviewTripCommand.ExecuteAsync(null);
        else if (action == "Editar itinerario")
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
        else if (action == remindersLabel)
            await MauiProgram.Services.GetRequiredService<TravelCompanion.Mobile.Services.ReservationReminderService>().ConfigureAsync();
    }

    private async void OnTodayLocationTapped(object? sender, TappedEventArgs e)
    {
        if ((sender as BindableObject)?.BindingContext is TodayLocationViewModel location)
        {
            if (location.AssignedItem is { } item)
                await Shell.Current.GoToAsync(nameof(ScheduleItemDetailPage), new Dictionary<string, object> { ["ScheduleItem"] = item });
            else
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
        else if ((sender as BindableObject)?.BindingContext is TodayLocationViewModel { AssignedItem: { } item })
            await _viewModel.EditPersonalItemCommand.ExecuteAsync(new TodayReservationViewModel(item));
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

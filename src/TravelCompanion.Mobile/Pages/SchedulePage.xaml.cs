using System;
using System.ComponentModel;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using TravelCompanion.Mobile.Services;
using TravelCompanion.Mobile.ViewModels;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Mobile.Pages;

public partial class SchedulePage : ContentPage, IQueryAttributable
{
    private bool _openingTodayDetail;
    private bool _openingItineraryMenu;
    private int _appearanceVersion;
    private async void OnExpensesSectionClicked(object? sender, EventArgs e) => await ShowExpensesAsync();
    private async void OnItinerarySectionClicked(object? sender, EventArgs e)
    {
        FolderPanelView.Deactivate(); FolderPanelView.IsVisible = false;
        ExpensesPanelView.Deactivate(); ExpensesPanelView.IsVisible = false; ItineraryContent.IsVisible = true;
        UpdateSectionButtons();
        // A panel can stay selected while session reset clears the itinerary behind it.
        if (!_viewModel.HasLoaded && !_isHandlingAppearance)
        {
            _isHandlingAppearance = true;
            await Dispatcher.DispatchAsync(HandleAppearingAsync);
        }
    }
    private async Task ShowExpensesAsync()
    {
        FolderPanelView.Deactivate(); FolderPanelView.IsVisible = false;
        ItineraryContent.IsVisible = false; ExpensesPanelView.IsVisible = true;
        UpdateSectionButtons();
        await ExpensesPanelView.ActivateAsync();
    }
    private async void OnFolderSectionClicked(object? sender, EventArgs e)
    {
        ExpensesPanelView.Deactivate(); ExpensesPanelView.IsVisible = false;
        ItineraryContent.IsVisible = false; FolderPanelView.IsVisible = true;
        UpdateSectionButtons();
        await FolderPanelView.ActivateAsync();
    }
    private void UpdateSectionButtons()
    {
        var sections = new[]
        {
            (ItinerarySectionButton, ItineraryContent.IsVisible),
            (FolderSectionButton, FolderPanelView.IsVisible),
            (ExpensesSectionButton, ExpensesPanelView.IsVisible)
        };
        foreach (var (button, selected) in sections)
        {
            button.BackgroundColor = selected ? EditorialUi.Surface : Colors.Transparent;
            button.TextColor = selected ? EditorialUi.Ink : EditorialUi.Muted;
            button.FontAttributes = selected ? FontAttributes.Bold : FontAttributes.None;
            SemanticProperties.SetHint(button, selected ? EditorialUi.TextResource("UxCurrentSection") : null);
        }
    }
    protected override bool OnBackButtonPressed()
    {
        if (!ExpensesPanelView.IsVisible && !FolderPanelView.IsVisible) return base.OnBackButtonPressed();
        OnItinerarySectionClicked(this, EventArgs.Empty); return true;
    }
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
    private ScheduleDayFilterViewModel? _centeredDay;
    private bool _dayStripActive;
    public double DayTileHeight { get; private set; } = 64;
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
        UpdateSectionButtons();
#if ANDROID
        Platforms.Android.TopInsetCorrection.Observe(ScheduleRoot, ScheduleHeader);
#endif
        stopwatch.Stop();
        BindingContext = viewModel;

        _logger.LogInformation(
            "Schedule page initialized in {ElapsedMs}ms. HasLoaded={HasLoaded}.",
            stopwatch.Elapsed.TotalMilliseconds,
            _viewModel.HasLoaded);
    }

    protected override async void OnAppearing()
    {
        _appearanceVersion++;
        base.OnAppearing();
        UpdateSectionButtons();
        _dayStripActive = true;
        UpdateDayStripMetrics();
        _viewModel.PropertyChanged -= OnSchedulePropertyChanged;
        _viewModel.PropertyChanged += OnSchedulePropertyChanged;
        CenterSelectedDay(animate: false);
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
        _appearanceVersion++;
        _dayStripActive = false;
        _viewModel.PropertyChanged -= OnSchedulePropertyChanged;
        _centeredDay = null;
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

    private void OnAccessTimerTick(object? sender, EventArgs e)
    {
        if (!MobileDiagnosticsSettings.IsEnabled) { _viewModel.RefreshAccessState(); return; }
        var start = Stopwatch.GetTimestamp(); var allocated = GC.GetAllocatedBytesForCurrentThread();
        _viewModel.RefreshAccessState();
        ClientDiagnostics.Record("schedule_timer_measured", new() { ElapsedTicks = Stopwatch.GetTimestamp() - start,
            TickFrequency = Stopwatch.Frequency,
            ElapsedMs = (long)Stopwatch.GetElapsedTime(start).TotalMilliseconds,
            AllocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocated });
    }

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
                ? LocalizationResourceManager.Instance["UXAuditTripSetupError"]
                : LocalizationResourceManager.Instance["UXAuditScheduleLoadError"];
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

    private void OnDayStripLoaded(object? sender, EventArgs e)
    {
        UpdateDayStripMetrics();
        CenterSelectedDay(animate: false);
    }

    private void UpdateDayStripMetrics()
    {
#if ANDROID
        var textScale = Microsoft.Maui.ApplicationModel.Platform.CurrentActivity?.Resources?.Configuration?.FontScale ?? 1f;
        DayTileHeight = Math.Ceiling(64 * Math.Max(1, textScale));
        OnPropertyChanged(nameof(DayTileHeight));
        DayStrip.HeightRequest = DayTileHeight + 16;
        foreach (var day in _viewModel.DayFilters) day.UpdateTextScale(textScale);
#endif
    }

    private void OnSchedulePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ScheduleViewModel.SelectedDayFilter))
        {
            UpdateDayStripMetrics();
            CenterSelectedDay(animate: _centeredDay is not null);
        }
    }

    private void CenterSelectedDay(bool animate)
    {
        var day = _viewModel.SelectedDayFilter;
        if (day is null || ReferenceEquals(day, _centeredDay)) return;
        var contextVersion = MauiProgram.Services.GetRequiredService<AuthSessionService>().ContextVersion;
        // Scroll is purely browsing. Only an explicit button activation selects
        // a day; neither the drag nor its momentum issues itinerary requests.
        Dispatcher.Dispatch(() =>
        {
            if (!_dayStripActive || !DayStrip.IsLoaded || !ReferenceEquals(day, _viewModel.SelectedDayFilter)
                || contextVersion != MauiProgram.Services.GetRequiredService<AuthSessionService>().ContextVersion)
                return;
            _centeredDay = day;
            DayStrip.ScrollTo(day, position: ScrollToPosition.Center, animate: animate);
        });
    }

    private async void OnDayClicked(object? sender, EventArgs e)
    {
        if (sender is not Button { BindingContext: ScheduleDayFilterViewModel day }
            || ScheduleDayNavigation.ResolveChoice(_viewModel.DayFilters, day) is null) return;
        var session = MauiProgram.Services.GetRequiredService<AuthSessionService>();
        var contextVersion = session.ContextVersion;
        try
        {
            if (ReferenceEquals(day, _viewModel.SelectedDayFilter))
            {
                if (_viewModel.CanOpenStayMap) await _viewModel.OpenStayMapCommand.ExecuteAsync(null);
                return;
            }
            await _viewModel.SelectDayCommand.ExecuteAsync(day);
        }
        catch (OperationCanceledException)
        {
            // Another day selection or leaving the page cancels the old request.
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not change the selected itinerary day.");
            if (contextVersion == session.ContextVersion)
                _viewModel.ErrorMessage = LocalizationResourceManager.Instance["UXAuditScheduleLoadError"];
        }
    }

    private async void OnItineraryMenuClicked(object? sender, EventArgs e)
    {
        if (_openingItineraryMenu || !_dayStripActive) return;
        var sessions = MauiProgram.Services.GetRequiredService<AuthSessionService>();
        var contextVersion = sessions.ContextVersion;
        var appearanceVersion = _appearanceVersion;
        _openingItineraryMenu = true;
        try
        {
            var resources = TravelCompanion.Mobile.Services.LocalizationResourceManager.Instance;
            var reviewDayLabel = resources["UXAuditReviewDay"];
            var reviewTripLabel = resources["ReviewTrip"];
            var downloadLabel = resources["DownloadTrip"];
            var shareLabel = resources["UXAuditShareItinerary"];
            var editLabel = resources["UXAuditEditItinerary"];
            var deleteLabel = resources["DeleteTripTitle"];
            var remindersLabel = resources["UXAuditReservationReminders"];
            var searchLabel = resources["TripSearchTitle"];
            var actions = new List<string>
            {
                searchLabel,
                reviewDayLabel,
                reviewTripLabel,
                downloadLabel,
                shareLabel
            };
            actions.Add(remindersLabel);
            if (_viewModel.CanManageItinerary)
            {
                actions.Insert(0, editLabel);
                actions.Add(deleteLabel);
            }
            var action = await DisplayActionSheetAsync(
                resources["UXAuditItineraryMenuTitle"],
                resources["CommonCancel"],
                null,
                actions.ToArray());
            if (!sessions.HasSession || contextVersion != sessions.ContextVersion
                || appearanceVersion != _appearanceVersion || Shell.Current.CurrentPage != this) return;
            if ((action == editLabel || action == deleteLabel) && !_viewModel.CanManageItinerary) return;
            if (action == searchLabel)
                await Navigation.PushModalAsync(new TripSearchPage());
            else if (action == reviewDayLabel)
                await _viewModel.ReviewSelectedDayCommand.ExecuteAsync(null);
            else if (action == reviewTripLabel)
                await _viewModel.ReviewTripCommand.ExecuteAsync(null);
            else if (action == editLabel)
            {
                await _viewModel.EditItineraryCommand.ExecuteAsync(null);
            }
            else if (action == deleteLabel)
            {
                await _viewModel.DeleteItineraryCommand.ExecuteAsync(null);
            }
            else if (action == downloadLabel)
            {
                await _viewModel.DownloadOfflineCommand.ExecuteAsync(null);
            }
            else if (action == shareLabel)
            {
                await _viewModel.ShareItineraryCommand.ExecuteAsync(null);
            }
            else if (action == remindersLabel)
                await MauiProgram.Services.GetRequiredService<TravelCompanion.Mobile.Services.ReservationReminderService>().ConfigureAsync();
        }
        finally { _openingItineraryMenu = false; }
    }

    private async void OnTodayLocationTapped(object? sender, TappedEventArgs e) => await OpenTodayLocationAsync(sender);
    private async void OnTodayLocationOpenClicked(object? sender, EventArgs e) => await OpenTodayLocationAsync(sender);

    private async Task OpenTodayLocationAsync(object? sender)
    {
        if (_openingTodayDetail || (sender as BindableObject)?.BindingContext is not TodayLocationViewModel location) return;
        _openingTodayDetail = true;
        try
        {
            if (location.AssignedItem is { } item)
                await Shell.Current.GoToAsync(nameof(ScheduleItemDetailPage), new Dictionary<string, object> { ["ScheduleItem"] = item });
            else
                await _viewModel.OpenRecommendationCommand.ExecuteAsync(location.Recommendation);
        }
        finally { _openingTodayDetail = false; }
    }

    private async void OnTodayReservationTapped(object? sender, TappedEventArgs e) => await OpenTodayReservationAsync(sender);
    private async void OnTodayReservationOpenClicked(object? sender, EventArgs e) => await OpenTodayReservationAsync(sender);

    private async Task OpenTodayReservationAsync(object? sender)
    {
        if (_openingTodayDetail || (sender as BindableObject)?.BindingContext is not TodayReservationViewModel reservation) return;
        _openingTodayDetail = true;
        try { await _viewModel.OpenScheduleItemCommand.ExecuteAsync(reservation.Item); }
        finally { _openingTodayDetail = false; }
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

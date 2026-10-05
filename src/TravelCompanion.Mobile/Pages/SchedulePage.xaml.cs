using System;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using TravelCompanion.Mobile.Services;
using TravelCompanion.Mobile.ViewModels;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Mobile.Pages;

public partial class SchedulePage : ContentPage, IQueryAttributable
{
    private bool _openingTodayDetail;
    private async void OnExpensesSectionClicked(object? sender, EventArgs e) => await ShowExpensesAsync();
    private async void OnItinerarySectionClicked(object? sender, EventArgs e)
    {
        FolderPanelView.Deactivate(); FolderPanelView.IsVisible = false;
        FolderSectionButton.TextColor = ExpenseUi.Muted;
        ExpensesPanelView.Deactivate(); ExpensesPanelView.IsVisible = false; ItineraryContent.IsVisible = true;
        ItinerarySectionButton.TextColor = ExpenseUi.Ink; ExpensesSectionButton.TextColor = ExpenseUi.Muted;
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
        // Keep native Button activation for accessibility and keyboard use. A
        // separate touch surface owns physical taps/swipes so Android cannot
        // dispatch both Tapped and Clicked for the same interaction.
        PreviousDayTouchSurface.IsVisible = true;
        SelectedDayTouchSurface.IsVisible = true;
        NextDayTouchSurface.IsVisible = true;
        var previousDayTap = new TapGestureRecognizer();
        previousDayTap.Tapped += OnPreviousDayClicked;
        PreviousDayTouchSurface.GestureRecognizers.Add(previousDayTap);
        var nextDayTap = new TapGestureRecognizer();
        nextDayTap.Tapped += OnNextDayClicked;
        NextDayTouchSurface.GestureRecognizers.Add(nextDayTap);
        SelectedDayTouchSurface.GestureRecognizers.Add(new TapGestureRecognizer
        {
            Command = _viewModel.OpenStayMapCommand
        });
        View[] swipeTargets = [PreviousDayTouchSurface, SelectedDayTouchSurface, NextDayTouchSurface];
#else
        View[] swipeTargets = [PreviousDayButton, SelectedDayCaption, StayMapButton, NextDayButton];
#endif
        foreach (var view in swipeTargets)
        {
            foreach (var direction in new[] { SwipeDirection.Left, SwipeDirection.Right })
            {
                var swipe = new SwipeGestureRecognizer { Direction = direction };
                swipe.Swiped += OnDaySelectorSwiped;
                view.GestureRecognizers.Add(swipe);
            }
        }
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

    private async void OnPreviousDayClicked(object? sender, EventArgs e) => await ChangeSelectedDayAsync(-1);
    private async void OnNextDayClicked(object? sender, EventArgs e) => await ChangeSelectedDayAsync(1);
    private async void OnDaySelectorSwiped(object? sender, SwipedEventArgs e)
    {
        var offset = e.Direction switch { SwipeDirection.Left => 1, SwipeDirection.Right => -1, _ => 0 };
        await ChangeSelectedDayAsync(offset);
    }

    private async Task ChangeSelectedDayAsync(int offset)
    {
        var session = MauiProgram.Services.GetRequiredService<AuthSessionService>();
        var contextVersion = session.ContextVersion;
        try
        {
            await _viewModel.MoveSelectedDayAsync(offset);
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
        var resources = TravelCompanion.Mobile.Services.LocalizationResourceManager.Instance;
        var documentsLabel = resources["UXAuditTripDocuments"];
        var reviewDayLabel = resources["UXAuditReviewDay"];
        var reviewTripLabel = resources["ReviewTrip"];
        var downloadLabel = resources["DownloadTrip"];
        var shareLabel = resources["UXAuditShareItinerary"];
        var editLabel = resources["UXAuditEditItinerary"];
        var deleteLabel = resources["DeleteTripTitle"];
        var remindersLabel = resources["UXAuditReservationReminders"];
        var preparationLabel = resources["PreparationTitle"];
        var actions = new List<string>
        {
            documentsLabel,
            reviewDayLabel,
            reviewTripLabel,
            downloadLabel,
            shareLabel
        };
        actions.Add(remindersLabel);
        actions.Insert(0, preparationLabel);
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
        if (action == preparationLabel)
            await Shell.Current.GoToAsync(nameof(TripPreparationPage));
        else if (action == documentsLabel)
            await Shell.Current.GoToAsync(nameof(DocsPage));
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

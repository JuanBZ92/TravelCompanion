using TravelCompanion.Mobile.Pages;
using TravelCompanion.Mobile.Services;

namespace TravelCompanion.Mobile;

public partial class AppShell : Shell
{
    private bool _logoutInProgress;
    private bool _paywallInProgress;
    private int _pendingMutationCount;
    private readonly NavigationTrail _tabHistory = new();
    private bool _returningToTab;

    protected override void OnNavigated(ShellNavigatedEventArgs args)
    {
        base.OnNavigated(args);
        var route = CurrentItem?.CurrentItem?.CurrentItem?.Route;
        if (CurrentItem?.Route != "main") { _tabHistory.Clear(); return; }
        if (!_returningToTab && route is not null && route != "logout") _tabHistory.Visit(route);
    }

    public async Task<bool> TryPreviousTabAsync()
    {
        var previous = _tabHistory.Previous;
        if (previous is null) return false;
        _returningToTab = true;
        try
        {
            await GoToAsync($"//main/{previous}");
            _tabHistory.Pop();
            return true;
        }
        finally { _returningToTab = false; }
    }

    public AppShell()
    {
        InitializeComponent();
        ApplyLocalizedTitles();
        LocalizationResourceManager.Instance.CultureChanged += OnCultureChanged;
        Routing.RegisterRoute(nameof(RecommendationDetailPage), typeof(RecommendationDetailPage));
        Routing.RegisterRoute(nameof(ScheduleItemDetailPage), typeof(ScheduleItemDetailPage));
        Routing.RegisterRoute(nameof(BuilderSetupPage), typeof(BuilderSetupPage));
        Routing.RegisterRoute(nameof(ItineraryItemEditorPage), typeof(ItineraryItemEditorPage));
        Routing.RegisterRoute(nameof(AccountPage), typeof(AccountPage));
        Routing.RegisterRoute(nameof(DocsPage), typeof(DocsPage));
        Routing.RegisterRoute(nameof(PaywallPage), typeof(PaywallPage));
        Routing.RegisterRoute(nameof(TripReviewPage), typeof(TripReviewPage));
        Routing.RegisterRoute(nameof(DayPersonalizationPage), typeof(DayPersonalizationPage));

        var sessionService = MauiProgram.Services.GetRequiredService<AuthSessionService>();
        sessionService.StateChanged += OnSessionStateChanged;
        MauiProgram.Services.GetRequiredService<OfflineSyncCoordinator>().PendingCountChanged += OnPendingCountChanged;
        if (sessionService.IsFreeMapPreview)
        {
            FreeMapTab.Content ??= MauiProgram.Services.GetRequiredService<FreeMapPage>();
        }
        ApplySessionTabs(sessionService);

        if (sessionService.HasSession)
        {
            var route = sessionService.MustChangePassword
                ? "//change-password"
                : sessionService.IsBiometricEnabled
                    ? "//biometric-unlock"
                    : GetAuthenticatedLandingRoute(sessionService);

            Dispatcher.Dispatch(async () => await GoToAsync(route));
        }
    }

    public void ApplySessionTabs(AuthSessionService sessionService)
    {
        var usesMainTabs = sessionService.HasSession;
        // Android may still be rendering the previous tab after GoToAsync("//login").
        // Removing its ShellContent during logout crashes ShellSectionRenderer.
        if (_logoutInProgress && !usesMainTabs) return;
        FreeMapTab.IsVisible = false;
        MapTab.IsVisible = usesMainTabs;
        if (usesMainTabs)
        {
            // Free builders keep the preview map, including redacted paid markers.
            if (sessionService.IsFreeMapPreview && MapTab.Content is not FreeMapPage)
            {
                MapTab.ContentTemplate = null;
                MapTab.Content = new FreeMapPage();
            }
            else if (!sessionService.IsFreeMapPreview && MapTab.Content is FreeMapPage)
            {
                MapTab.ContentTemplate = null;
                MapTab.Content = MauiProgram.Services.GetRequiredService<MapPage>();
            }
        }
        ScheduleTab.IsVisible = usesMainTabs && (!sessionService.IsFreeMapPreview || sessionService.IsBuilder);
        JournalTab.IsVisible = ScheduleTab.IsVisible && sessionService.CurrentTripId.HasValue;
        AssistantTab.IsVisible = sessionService.IsBuilder;
        AccountTab.IsVisible = sessionService.HasSession;
    }

    public static string GetAuthenticatedLandingRoute(AuthSessionService sessionService)
    {
        if (sessionService.IsFreeMapPreview && !sessionService.IsBuilder)
        {
            return "//main/map";
        }

        return sessionService.RequiresTripSetup || !sessionService.CurrentTripId.HasValue
            ? "//main/map"
            : "//main/schedule";
    }

    protected override void OnNavigating(ShellNavigatingEventArgs args)
    {
        base.OnNavigating(args);
        var target = args.Target.Location.OriginalString.Split('?')[0].TrimEnd('/');
        if (target.EndsWith("/assistant", StringComparison.OrdinalIgnoreCase)
            && !MauiProgram.Services.GetRequiredService<AuthSessionService>().CanEditItinerary
            && args.CanCancel)
        {
            args.Cancel();
            if (!_paywallInProgress)
            {
                _paywallInProgress = true;
                Dispatcher.Dispatch(async () =>
                {
                    try { await PaywallNavigation.OpenAsync(TravelCompanion.Shared.Dtos.PaywallEntryPoint.Today, limitReached: true); }
                    finally { _paywallInProgress = false; }
                });
            }
            return;
        }
        if (!args.Target.Location.OriginalString.TrimEnd('/').EndsWith("/logout", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        args.Cancel();
        if (_logoutInProgress)
        {
            return;
        }

        _logoutInProgress = true;
        Dispatcher.Dispatch(async () => await LogoutFromTabAsync());
    }

    public Task SignOutAsync()
    {
        if (_logoutInProgress) return Task.CompletedTask;
        _logoutInProgress = true;
        return LogoutFromTabAsync();
    }

    private async Task LogoutFromTabAsync()
    {
        IsEnabled = false;
        try
        {
            var logoutService = MauiProgram.Services.GetRequiredService<SessionLogoutService>();
            // Leave the native map before resetting its bindings or removing active tabs.
            await logoutService.LogoutAsync(() => GoToAsync("//login"));
            ApplySessionTabs(MauiProgram.Services.GetRequiredService<AuthSessionService>());
        }
        catch (Exception exception)
        {
            System.Diagnostics.Trace.TraceError($"Logout failed: {exception}");
            await DisplayAlertAsync("Salir", "No pudimos completar el cierre de sesión. Intentá nuevamente.", "OK");
        }
        finally
        {
            IsEnabled = true;
            _logoutInProgress = false;
        }
    }

    private void OnCultureChanged(object? sender, EventArgs e)
    {
        ApplyLocalizedTitles();
    }

    private void OnPendingCountChanged(object? sender, int pendingCount)
    {
        Dispatcher.Dispatch(() =>
        {
            _pendingMutationCount = pendingCount;
            ApplyLocalizedTitles();
        });
    }

    private void OnSessionStateChanged(object? sender, EventArgs e)
    {
        Dispatcher.Dispatch(() =>
        {
            if (!_logoutInProgress)
                ApplySessionTabs(MauiProgram.Services.GetRequiredService<AuthSessionService>());
        });
    }

    private void ApplyLocalizedTitles()
    {
        var resources = LocalizationResourceManager.Instance;
        LoginTab.Title = resources["TabLogin"];
        BiometricUnlockTab.Title = resources["TabBiometricUnlock"];
        ChangePasswordTab.Title = resources["TabChangePassword"];
        FreeMapTab.Title = resources["TabMap"];
        MapTab.Title = resources["TabMap"];
        ScheduleTab.Title = System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "es" ? "Viaje" : "Trip";
        AssistantTab.Title = _pendingMutationCount > 0
            ? $"{resources["TabAssistant"]} ({_pendingMutationCount})"
            : resources["TabAssistant"];
        AccountTab.Title = resources["TabAccount"];
    }
}

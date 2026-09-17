using TravelCompanion.Mobile.Pages;
using TravelCompanion.Mobile.Services;

namespace TravelCompanion.Mobile;

public partial class AppShell : Shell
{
    private bool _logoutInProgress;
    private int _pendingMutationCount;

    public AppShell()
    {
        InitializeComponent();
        ApplyLocalizedTitles();
        LocalizationResourceManager.Instance.CultureChanged += OnCultureChanged;
        Routing.RegisterRoute(nameof(RecommendationDetailPage), typeof(RecommendationDetailPage));
        Routing.RegisterRoute(nameof(ScheduleItemDetailPage), typeof(ScheduleItemDetailPage));
        Routing.RegisterRoute(nameof(BuilderSetupPage), typeof(BuilderSetupPage));
        Routing.RegisterRoute(nameof(ItineraryItemEditorPage), typeof(ItineraryItemEditorPage));

        var sessionService = MauiProgram.Services.GetRequiredService<AuthSessionService>();
        MauiProgram.Services.GetRequiredService<OfflineSyncCoordinator>().PendingCountChanged += OnPendingCountChanged;
        if (sessionService.IsFreeMapPreview)
        {
            FreeMapTab.Content ??= MauiProgram.Services.GetRequiredService<FreeMapPage>();
        }
        else
        {
            ApplySessionTabs(sessionService);
        }

        if (sessionService.HasSession)
        {
            var route = sessionService.IsFreeMapPreview
                ? "//free-map"
                : sessionService.MustChangePassword
                ? "//change-password"
                : sessionService.IsBiometricEnabled
                    ? "//biometric-unlock"
                    : "//main/schedule";

            Dispatcher.Dispatch(async () => await GoToAsync(route));
        }
    }

    public void ApplySessionTabs(AuthSessionService sessionService)
    {
        MapTab.IsVisible = !sessionService.IsFreeMapPreview;
        ScheduleTab.IsVisible = !sessionService.IsFreeMapPreview;
        AssistantTab.IsVisible = !sessionService.IsFreeMapPreview;
        DocsTab.IsVisible = sessionService.HasCuratedDocs;
        LogoutTab.IsVisible = sessionService.HasSession && !sessionService.IsFreeMapPreview;
    }

    protected override void OnNavigating(ShellNavigatingEventArgs args)
    {
        base.OnNavigating(args);
        if (!args.Target.Location.OriginalString.TrimEnd('/').EndsWith("/logout", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        args.Cancel();
        if (_logoutInProgress)
        {
            return;
        }

        Dispatcher.Dispatch(async () => await LogoutFromTabAsync());
    }

    private async Task LogoutFromTabAsync()
    {
        _logoutInProgress = true;
        try
        {
            var logoutService = MauiProgram.Services.GetRequiredService<SessionLogoutService>();
            await logoutService.LogoutAsync();
            ApplySessionTabs(MauiProgram.Services.GetRequiredService<AuthSessionService>());
            await GoToAsync("//login");
        }
        finally
        {
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

    private void ApplyLocalizedTitles()
    {
        var resources = LocalizationResourceManager.Instance;
        LoginTab.Title = resources["TabLogin"];
        BiometricUnlockTab.Title = resources["TabBiometricUnlock"];
        ChangePasswordTab.Title = resources["TabChangePassword"];
        FreeMapTab.Title = resources["TabMap"];
        MapTab.Title = resources["TabMap"];
        ScheduleTab.Title = resources["TabToday"];
        AssistantTab.Title = _pendingMutationCount > 0
            ? $"{resources["TabAssistant"]} ({_pendingMutationCount})"
            : resources["TabAssistant"];
        DocsTab.Title = resources["TabDocs"];
        LogoutTab.Title = resources["TabLogout"];
    }
}

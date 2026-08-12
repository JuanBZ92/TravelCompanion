using TravelCompanion.Mobile.Pages;
using TravelCompanion.Mobile.Services;

namespace TravelCompanion.Mobile;

public partial class AppShell : Shell
{
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
        if (sessionService.IsFreeMapPreview)
        {
            FreeMapTab.Content ??= MauiProgram.Services.GetRequiredService<FreeMapPage>();
        }
        else
        {
            WarmMainTabPages();
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
                    : "//main/map";

            Dispatcher.Dispatch(async () => await GoToAsync(route));
        }
    }

    private void WarmMainTabPages()
    {
        MapTab.Content ??= MauiProgram.Services.GetRequiredService<MapPage>();
        ScheduleTab.Content ??= MauiProgram.Services.GetRequiredService<SchedulePage>();
        AssistantTab.Content ??= MauiProgram.Services.GetRequiredService<TravelChatPage>();
        DocsTab.Content ??= MauiProgram.Services.GetRequiredService<DocsPage>();
    }

    public void ApplySessionTabs(AuthSessionService sessionService)
    {
        MapTab.IsVisible = !sessionService.IsFreeMapPreview;
        ScheduleTab.IsVisible = !sessionService.IsFreeMapPreview;
        AssistantTab.IsVisible = !sessionService.IsFreeMapPreview;
        DocsTab.IsVisible = sessionService.HasCuratedDocs;
    }

    private void OnCultureChanged(object? sender, EventArgs e)
    {
        ApplyLocalizedTitles();
    }

    private void ApplyLocalizedTitles()
    {
        var resources = LocalizationResourceManager.Instance;
        LoginTab.Title = resources["TabLogin"];
        BiometricUnlockTab.Title = resources["TabBiometricUnlock"];
        ChangePasswordTab.Title = resources["TabChangePassword"];
        FreeMapTab.Title = "Map";
        MapTab.Title = "Map";
        ScheduleTab.Title = resources["TabToday"];
        AssistantTab.Title = resources["TabAssistant"];
        DocsTab.Title = resources["TabDocs"];
    }
}

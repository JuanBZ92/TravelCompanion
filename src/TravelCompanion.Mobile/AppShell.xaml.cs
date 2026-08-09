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
        WarmMainTabPages();

        var sessionService = MauiProgram.Services.GetRequiredService<AuthSessionService>();
        if (sessionService.HasSession)
        {
            var route = sessionService.MustChangePassword
                ? "//change-password"
                : sessionService.IsBiometricEnabled
                    ? "//biometric-unlock"
                    : "//main/recommendations";

            Dispatcher.Dispatch(async () => await GoToAsync(route));
        }
    }

    private void WarmMainTabPages()
    {
        RecommendationsTab.Content ??= MauiProgram.Services.GetRequiredService<RecommendationsPage>();
        ScheduleTab.Content ??= MauiProgram.Services.GetRequiredService<SchedulePage>();
        AssistantTab.Content ??= MauiProgram.Services.GetRequiredService<TravelChatPage>();
        DocsTab.Content ??= MauiProgram.Services.GetRequiredService<DocsPage>();
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
        RecommendationsTab.Title = resources["TabDiscover"];
        ScheduleTab.Title = resources["TabToday"];
        AssistantTab.Title = resources["TabAssistant"];
        DocsTab.Title = resources["TabDocs"];
    }
}

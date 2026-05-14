using Microsoft.Extensions.Logging;
#if !WINDOWS
using Microsoft.Maui.Controls.Maps;
#endif
using Maui.Biometric;
using TravelCompanion.Mobile.Pages;
using TravelCompanion.Mobile.Services;
using TravelCompanion.Mobile.ViewModels;

namespace TravelCompanion.Mobile;

public static class MauiProgram
{
    public static IServiceProvider Services { get; private set; } = null!;

    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .UseBiometricAuthentication()
#if !WINDOWS
            .UseMauiMaps()
#endif
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
            });

#if DEBUG
        builder.Logging.AddDebug();
#endif

        var apiBaseUri = ApiEndpointResolver.Resolve();
        builder.Services.AddSingleton(new HttpClient
        {
            BaseAddress = apiBaseUri
        });
        builder.Services.AddSingleton<TravelCompanionApiClient>();
        builder.Services.AddSingleton<AuthSessionService>();
        builder.Services.AddSingleton<BiometricUnlockService>();
        builder.Services.AddSingleton<OfflineCacheService>();
        builder.Services.AddSingleton<MobileBootstrapStore>();
        builder.Services.AddSingleton<FavoritesService>();

        builder.Services.AddTransient<LoginViewModel>();
        builder.Services.AddTransient<BiometricUnlockViewModel>();
        builder.Services.AddTransient<ChangePasswordViewModel>();
        builder.Services.AddTransient<RecommendationsViewModel>();
        builder.Services.AddTransient<MapViewModel>();
        builder.Services.AddTransient<ScheduleViewModel>();
        builder.Services.AddTransient<ScheduleItemDetailViewModel>();
        builder.Services.AddTransient<PackagesViewModel>();
        builder.Services.AddTransient<RecommendationDetailViewModel>();
        builder.Services.AddTransient<SupportViewModel>();

        builder.Services.AddTransient<LoginPage>();
        builder.Services.AddTransient<BiometricUnlockPage>();
        builder.Services.AddTransient<ChangePasswordPage>();
        builder.Services.AddTransient<RecommendationsPage>();
        builder.Services.AddTransient<MapPage>();
        builder.Services.AddTransient<SchedulePage>();
        builder.Services.AddTransient<ScheduleItemDetailPage>();
        builder.Services.AddTransient<PackagesPage>();
        builder.Services.AddTransient<SupportPage>();
        builder.Services.AddTransient<RecommendationDetailPage>();

        var app = builder.Build();
        Services = app.Services;

        return app;
    }
}

using Microsoft.Extensions.Logging;
#if ANDROID
using Android.OS;
#endif
#if !WINDOWS
using Microsoft.Maui.Controls.Maps;
#endif
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

        builder.Services.AddSingleton(new HttpClient
        {
            BaseAddress = new Uri(ApiBaseUrl)
        });
        builder.Services.AddSingleton<TravelCompanionApiClient>();
        builder.Services.AddSingleton<FavoritesService>();

        builder.Services.AddTransient<RecommendationsViewModel>();
        builder.Services.AddTransient<MapViewModel>();
        builder.Services.AddTransient<ScheduleViewModel>();
        builder.Services.AddTransient<ScheduleItemDetailViewModel>();
        builder.Services.AddTransient<PackagesViewModel>();
        builder.Services.AddTransient<RecommendationDetailViewModel>();

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

#if ANDROID
    private static string ApiBaseUrl => IsAndroidEmulator()
        ? "http://10.0.2.2:5289"
        : "http://127.0.0.1:5289";

    private static bool IsAndroidEmulator()
    {
        return (Build.Fingerprint?.Contains("generic", StringComparison.OrdinalIgnoreCase) ?? false)
            || (Build.Model?.Contains("Emulator", StringComparison.OrdinalIgnoreCase) ?? false)
            || (Build.Manufacturer?.Contains("Genymotion", StringComparison.OrdinalIgnoreCase) ?? false)
            || (Build.Brand?.StartsWith("generic", StringComparison.OrdinalIgnoreCase) ?? false)
            || (Build.Device?.StartsWith("generic", StringComparison.OrdinalIgnoreCase) ?? false)
            || (Build.Product?.Contains("sdk", StringComparison.OrdinalIgnoreCase) ?? false);
    }
#else
    private const string ApiBaseUrl = "http://localhost:5289";
#endif
}

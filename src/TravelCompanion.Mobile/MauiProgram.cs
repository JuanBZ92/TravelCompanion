using Microsoft.Extensions.Logging;
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

        builder.Services.AddTransient<RecommendationsViewModel>();
        builder.Services.AddTransient<MapViewModel>();
        builder.Services.AddTransient<ScheduleViewModel>();
        builder.Services.AddTransient<PackagesViewModel>();
        builder.Services.AddTransient<RecommendationDetailViewModel>();

        builder.Services.AddTransient<RecommendationsPage>();
        builder.Services.AddTransient<MapPage>();
        builder.Services.AddTransient<SchedulePage>();
        builder.Services.AddTransient<PackagesPage>();
        builder.Services.AddTransient<SupportPage>();
        builder.Services.AddTransient<RecommendationDetailPage>();

        var app = builder.Build();
        Services = app.Services;

        return app;
    }

#if ANDROID
    private const string ApiBaseUrl = "http://10.0.2.2:5289";
#else
    private const string ApiBaseUrl = "http://localhost:5289";
#endif
}

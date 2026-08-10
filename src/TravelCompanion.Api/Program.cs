using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.ApplicationInsights.AspNetCore.Extensions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using TravelCompanion.Api.Middleware;
using TravelCompanion.Api.Data;
using TravelCompanion.Api.Models;
using TravelCompanion.Api.Options;
using TravelCompanion.Api.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    });
builder.Services
    .AddRazorPages(options =>
    {
        options.Conventions.AuthorizeFolder("/Admin", "AdminOnly");
    });
builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();
builder.Services.AddApplicationInsightsTelemetry(new ApplicationInsightsServiceOptions
{
    EnableAdaptiveSampling = true
});
builder.Services.Configure<ApiBehaviorOptions>(options =>
{
    options.InvalidModelStateResponseFactory = context =>
    {
        var problemDetails = new ValidationProblemDetails(context.ModelState)
        {
            Status = StatusCodes.Status400BadRequest,
            Title = "One or more validation errors occurred.",
            Type = "https://httpstatuses.com/400"
        };

        problemDetails.Extensions["traceId"] = context.HttpContext.TraceIdentifier;
        return new BadRequestObjectResult(problemDetails);
    };
});
builder.Services.AddResponseCompression(options =>
{
    options.EnableForHttps = true;
    options.Providers.Add<BrotliCompressionProvider>();
    options.Providers.Add<GzipCompressionProvider>();
});
builder.Services.AddRateLimiter(options =>
{
    options.AddFixedWindowLimiter("PinLogin", limiterOptions =>
    {
        limiterOptions.PermitLimit = 8;
        limiterOptions.Window = TimeSpan.FromMinutes(1);
        limiterOptions.QueueLimit = 0;
        limiterOptions.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
    });
});
builder.Services.Configure<AdminAuthOptions>(
    builder.Configuration.GetSection(AdminAuthOptions.SectionName));
builder.Services.Configure<ObservabilityOptions>(
    builder.Configuration.GetSection(ObservabilityOptions.SectionName));
builder.Services.Configure<OpenAiTravelOptions>(
    builder.Configuration.GetSection(OpenAiTravelOptions.SectionName));
builder.Services
    .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = "TravelCompanion.Admin";
        options.LoginPath = "/login";
        options.LogoutPath = "/logout";
        options.AccessDeniedPath = "/login";
    });
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("AdminOnly", policy => policy.RequireAuthenticatedUser());
});
builder.Services.AddScoped<IPasswordHasher<AppUser>, PasswordHasher<AppUser>>();
builder.Services.AddScoped<IPasswordHasher<Trip>, PasswordHasher<Trip>>();
builder.Services.AddScoped<UserSessionService>();
builder.Services.AddScoped<IUserInvitationSender, LoggingUserInvitationSender>();
builder.Services.AddScoped<IUserProfileService, UserProfileService>();
builder.Services.AddScoped<IItineraryService, ItineraryService>();
builder.Services.AddScoped<IRecommendationRanker, DeterministicRecommendationRanker>();
builder.Services.AddScoped<ITravelRecommendationPlanningService, TravelRecommendationPlanningService>();
builder.Services.AddScoped<IRecommendationTagCatalogService, RecommendationTagCatalogService>();
builder.Services.AddScoped<ITravelPreferenceCommandParser, TravelPreferenceCommandParser>();
builder.Services.AddScoped<ITravelAssistantActionPlanner, TravelAssistantActionPlanner>();
builder.Services.AddSingleton<ITravelAssistantTextProvider, TravelAssistantTextProvider>();
builder.Services.AddSingleton<ITravelPromptTemplateProvider, TravelPromptTemplateProvider>();
builder.Services.AddScoped<ITravelChatResponseComposer, TravelChatResponseComposer>();
builder.Services.AddScoped<ITravelAssistantConversationStateService, TravelAssistantConversationStateService>();
builder.Services.AddScoped<ITravelAssistantFeedbackService, TravelAssistantFeedbackService>();
builder.Services.AddScoped<YukuJapanRecommendationImportService>();
builder.Services.AddSingleton<TravelAssistantTelemetry>();
builder.Services.AddSingleton<ITravelChatIntentClassifier, TravelChatIntentClassifier>();
builder.Services.AddSingleton<ITravelAiModelClient, OpenAiTravelModelClient>();
builder.Services.AddScoped<ITravelChatService, TravelChatService>();
builder.Services.AddSingleton<SlowDbCommandLoggingInterceptor>();
builder.Services.AddDbContext<TravelCompanionDbContext>((serviceProvider, options) =>
{
    options.UseNpgsql(
        ResolvePostgresConnectionString(builder.Configuration),
        npgsqlOptions => npgsqlOptions.EnableRetryOnFailure());
    var interceptor = serviceProvider.GetRequiredService<SlowDbCommandLoggingInterceptor>();
    options.AddInterceptors(interceptor);
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}

app.UseStatusCodePages();
app.UseResponseCompression();
app.UseRateLimiter();
app.UseStaticFiles();
app.UseMiddleware<RequestObservabilityMiddleware>();

app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/health", () => Results.Ok(new
{
    status = "ok",
    service = "TravelCompanion.Api"
}));
app.MapControllers();
app.MapRazorPages();

if (!app.Environment.IsEnvironment("Testing"))
{
    await InitializeDatabaseAsync(app);
}

app.Run();

static async Task InitializeDatabaseAsync(WebApplication app)
{
    const int maxAttempts = 10;

    for (var attempt = 1; attempt <= maxAttempts; attempt++)
    {
        try
        {
            using var scope = app.Services.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<TravelCompanionDbContext>();
            var passwordHasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher<AppUser>>();
            await dbContext.Database.MigrateAsync();
            await DatabaseSeeder.SeedAsync(dbContext, passwordHasher);
            return;
        }
        catch (Exception) when (app.Environment.IsDevelopment() && attempt < maxAttempts)
        {
            await Task.Delay(TimeSpan.FromSeconds(2));
        }
    }
}

static string ResolvePostgresConnectionString(IConfiguration configuration)
{
    var databaseUrl = configuration["DATABASE_URL"];
    if (!string.IsNullOrWhiteSpace(databaseUrl))
    {
        return NormalizePostgresConnectionString(databaseUrl);
    }

    var configuredConnectionString = configuration.GetConnectionString("TravelCompanionDb");
    if (!string.IsNullOrWhiteSpace(configuredConnectionString))
    {
        return configuredConnectionString;
    }

    throw new InvalidOperationException(
        "Configure ConnectionStrings:TravelCompanionDb or DATABASE_URL before starting the API.");
}

static string NormalizePostgresConnectionString(string value)
{
    if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
        || (uri.Scheme != "postgres" && uri.Scheme != "postgresql"))
    {
        return value;
    }

    var userInfo = uri.UserInfo.Split(':', 2);
    var builder = new NpgsqlConnectionStringBuilder
    {
        Host = uri.Host,
        Port = uri.Port > 0 ? uri.Port : 5432,
        Database = Uri.UnescapeDataString(uri.AbsolutePath.TrimStart('/')),
        Username = userInfo.Length > 0 ? Uri.UnescapeDataString(userInfo[0]) : string.Empty,
        Password = userInfo.Length > 1 ? Uri.UnescapeDataString(userInfo[1]) : string.Empty,
        SslMode = SslMode.Require
    };

    return builder.ConnectionString;
}

public partial class Program;

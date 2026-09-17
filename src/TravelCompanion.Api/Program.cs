using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.AspNetCore.HttpOverrides;
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
var migrationOnly = args.Contains("--migrate", StringComparer.OrdinalIgnoreCase);
builder.Services.Configure<RequestLocalizationOptions>(options =>
{
    options.SetDefaultCulture("en-US").AddSupportedCultures("en-US")
        .AddSupportedUICultures("es", "es-ES", "en", "en-US");
    options.DefaultRequestCulture = new Microsoft.AspNetCore.Localization.RequestCulture("en-US", "es");
    options.RequestCultureProviders = [new Microsoft.AspNetCore.Localization.AcceptLanguageHeaderRequestCultureProvider()];
});

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
builder.Services.AddProblemDetails(options =>
{
    options.CustomizeProblemDetails = context =>
        context.ProblemDetails.Extensions["traceId"] = context.HttpContext.TraceIdentifier;
});
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
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = async (context, cancellationToken) =>
    {
        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
        {
            context.HttpContext.Response.Headers.RetryAfter =
                Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds)).ToString();
        }

        var problem = new ProblemDetails
        {
            Status = StatusCodes.Status429TooManyRequests,
            Title = "Too many requests.",
            Detail = "Wait before trying again."
        };
        problem.Extensions["traceId"] = context.HttpContext.TraceIdentifier;
        await context.HttpContext.Response.WriteAsJsonAsync(problem, cancellationToken);
    };
    options.GlobalLimiter = PartitionedRateLimiter.CreateChained(
        PartitionedRateLimiter.Create<HttpContext, string>(_ =>
            CreateNamedFixedWindowPartition("application:global", 4000, TimeSpan.FromMinutes(1))),
        PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
            CreateFixedWindowPartition(httpContext, "global", 240, TimeSpan.FromMinutes(1))));
    options.AddPolicy("ItineraryRoutes", httpContext =>
        CreateFixedWindowPartition(httpContext, "routes", 60, TimeSpan.FromMinutes(1)));
    options.AddPolicy("PinLogin", httpContext =>
        CreateFixedWindowPartition(httpContext, "pin", 8, TimeSpan.FromMinutes(1)));
    options.AddPolicy("PasswordLogin", httpContext =>
        CreateFixedWindowPartition(httpContext, "password", 8, TimeSpan.FromMinutes(1)));
    options.AddPolicy("AdminLogin", httpContext =>
        CreateFixedWindowPartition(httpContext, "admin", 6, TimeSpan.FromMinutes(5)));
});
builder.Services.Configure<AdminAuthOptions>(
    builder.Configuration.GetSection(AdminAuthOptions.SectionName));
builder.Services.Configure<ObservabilityOptions>(
    builder.Configuration.GetSection(ObservabilityOptions.SectionName));
builder.Services.Configure<OpenAiTravelOptions>(
    builder.Configuration.GetSection(OpenAiTravelOptions.SectionName));
builder.Services.Configure<FreePreviewOptions>(
    builder.Configuration.GetSection(FreePreviewOptions.SectionName));
builder.Services.Configure<GooglePlacesOptions>(builder.Configuration.GetSection(GooglePlacesOptions.SectionName));
builder.Services.Configure<GoogleRoutesOptions>(builder.Configuration.GetSection("GoogleRoutes"));
builder.Services.Configure<BuilderDemoOptions>(builder.Configuration.GetSection(BuilderDemoOptions.SectionName));
builder.Services
    .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = "TravelCompanion.Admin";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Strict;
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        options.LoginPath = "/login";
        options.LogoutPath = "/logout";
        options.AccessDeniedPath = "/login";
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = false;
    });
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("AdminOnly", policy => policy.RequireRole("Admin"));
});
builder.Services.AddScoped<IPasswordHasher<AppUser>, PasswordHasher<AppUser>>();
builder.Services.AddScoped<IPasswordHasher<Trip>, PasswordHasher<Trip>>();
builder.Services.AddScoped<IPasswordHasher<BuilderAccessGrant>, PasswordHasher<BuilderAccessGrant>>();
builder.Services.AddScoped<UserSessionService>();
builder.Services.AddScoped<TravelerAccessService>();
builder.Services.AddScoped<BuilderTripService>();
builder.Services.AddScoped<TravelerItineraryService>();
builder.Services.AddHttpClient();
builder.Services.AddSingleton<IGooglePlacesService, GooglePlacesService>();
builder.Services.AddSingleton<IGoogleRoutesService, GoogleRoutesService>();
builder.Services.AddHttpClient("GoogleRoutes").RemoveAllLoggers();
builder.Services.AddScoped<FreePreviewAccountService>();
builder.Services.AddScoped<FreeMapPreviewService>();
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
builder.Services.AddScoped<ITodayRecommendationService, TodayRecommendationService>();
builder.Services.AddScoped<YukuJapanRecommendationImportService>();
builder.Services.AddScoped<TripWorkbookImportService>();
builder.Services.AddScoped<TripPlanEditorService>();
builder.Services.AddScoped<ExternalPlaceInsightsService>();
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

if (!migrationOnly)
{
    ValidateProductionAdminCredentials(app);
}

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseForwardedHeaders();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler();
    app.UseHttpsRedirection();
}

app.UseStatusCodePages();
app.UseResponseCompression();
app.UseRateLimiter();
app.UseStaticFiles();
app.UseMiddleware<RequestObservabilityMiddleware>();
app.UseMiddleware<FreePreviewSessionGuardMiddleware>();

app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/health", () => Results.Ok(new
{
    status = "ok",
    service = "TravelCompanion.Api"
}));
app.MapGet("/health/ready", async (TravelCompanionDbContext dbContext, CancellationToken cancellationToken) =>
{
    try
    {
        return await dbContext.Database.CanConnectAsync(cancellationToken)
            ? Results.Ok(new { status = "ready", database = "ok" })
            : Results.Json(new { status = "not-ready", database = "unavailable" }, statusCode: StatusCodes.Status503ServiceUnavailable);
    }
    catch
    {
        return Results.Json(new { status = "not-ready", database = "unavailable" }, statusCode: StatusCodes.Status503ServiceUnavailable);
    }
});
app.UseRequestLocalization();
app.MapControllers();
app.MapRazorPages();

var applyMigrationsOnStartup = app.Configuration.GetValue<bool>("Database:ApplyMigrationsOnStartup");
if (!app.Environment.IsEnvironment("Testing") && (migrationOnly || applyMigrationsOnStartup))
{
    await InitializeDatabaseAsync(app);
}

if (migrationOnly)
{
    return;
}

app.Run();

static RateLimitPartition<string> CreateFixedWindowPartition(
    HttpContext httpContext,
    string policy,
    int permitLimit,
    TimeSpan window)
{
    var origin = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    return CreateNamedFixedWindowPartition($"{policy}:{origin}", permitLimit, window);
}

static RateLimitPartition<string> CreateNamedFixedWindowPartition(
    string key,
    int permitLimit,
    TimeSpan window)
{
    return RateLimitPartition.GetFixedWindowLimiter(
        key,
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = permitLimit,
            Window = window,
            QueueLimit = 0,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            AutoReplenishment = true
        });
}

static void ValidateProductionAdminCredentials(WebApplication app)
{
    if (!app.Environment.IsProduction())
    {
        return;
    }

    var options = app.Configuration.GetSection(AdminAuthOptions.SectionName).Get<AdminAuthOptions>();
    if (options is null
        || string.IsNullOrWhiteSpace(options.Username)
        || string.IsNullOrWhiteSpace(options.Password)
        || string.Equals(options.Password, AdminAuthOptions.DevelopmentPassword, StringComparison.Ordinal))
    {
        throw new InvalidOperationException(
            "Production requires explicit AdminAuth__Username and AdminAuth__Password values, and the development password is not allowed.");
    }
}

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
            var builderPinHasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher<BuilderAccessGrant>>();
            var builderDemo = app.Configuration.GetSection(BuilderDemoOptions.SectionName).Get<BuilderDemoOptions>();
            await dbContext.Database.MigrateAsync();
            await DatabaseSeeder.SeedAsync(
                dbContext,
                passwordHasher,
                builderDemo?.Enabled == true ? builderPinHasher : null,
                builderDemo?.Enabled == true ? builderDemo.Pin : null,
                builderDemo?.CustomerName);
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

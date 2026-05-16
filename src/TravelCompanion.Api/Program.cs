using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.ApplicationInsights.AspNetCore.Extensions;
using Microsoft.EntityFrameworkCore;
using System.Text.Json.Serialization;
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
builder.Services.Configure<AdminAuthOptions>(
    builder.Configuration.GetSection(AdminAuthOptions.SectionName));
builder.Services.Configure<ObservabilityOptions>(
    builder.Configuration.GetSection(ObservabilityOptions.SectionName));
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
builder.Services.AddScoped<UserSessionService>();
builder.Services.AddScoped<IUserInvitationSender, LoggingUserInvitationSender>();
builder.Services.AddScoped<IRecommendationRanker, DeterministicRecommendationRanker>();
builder.Services.AddScoped<ITravelChatService, TravelChatService>();
builder.Services.AddSingleton<SlowDbCommandLoggingInterceptor>();
builder.Services.AddDbContext<TravelCompanionDbContext>((serviceProvider, options) =>
{
    options.UseNpgsql(
        builder.Configuration.GetConnectionString("TravelCompanionDb"),
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
app.UseStaticFiles();
app.UseMiddleware<RequestObservabilityMiddleware>();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapRazorPages();

await InitializeDatabaseAsync(app);

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

using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using System.Text.Json.Serialization;
using TravelCompanion.Api.Data;
using TravelCompanion.Api.Options;

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
builder.Services.Configure<AdminAuthOptions>(
    builder.Configuration.GetSection(AdminAuthOptions.SectionName));
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
builder.Services.AddDbContext<TravelCompanionDbContext>(options =>
    options.UseNpgsql(
        builder.Configuration.GetConnectionString("TravelCompanionDb"),
        npgsqlOptions => npgsqlOptions.EnableRetryOnFailure()));

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}

app.UseStaticFiles();

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
            await dbContext.Database.MigrateAsync();
            await DatabaseSeeder.SeedAsync(dbContext);
            return;
        }
        catch (Exception) when (app.Environment.IsDevelopment() && attempt < maxAttempts)
        {
            await Task.Delay(TimeSpan.FromSeconds(2));
        }
    }
}

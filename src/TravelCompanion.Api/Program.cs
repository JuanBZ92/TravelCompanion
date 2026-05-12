using Microsoft.EntityFrameworkCore;
using TravelCompanion.Api.Data;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddOpenApi();
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

app.UseAuthorization();

app.MapControllers();

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
            await dbContext.Database.EnsureCreatedAsync();
            await DatabaseSeeder.SeedAsync(dbContext);
            return;
        }
        catch (Exception) when (app.Environment.IsDevelopment() && attempt < maxAttempts)
        {
            await Task.Delay(TimeSpan.FromSeconds(2));
        }
    }
}

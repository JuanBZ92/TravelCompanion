using Microsoft.EntityFrameworkCore;
using TravelCompanion.Api.Data;
using TravelCompanion.Notifications.Worker;
using TravelCompanion.Notifications.Worker.Options;
using TravelCompanion.Notifications.Worker.Services;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.Configure<NotificationWorkerOptions>(
    builder.Configuration.GetSection(NotificationWorkerOptions.SectionName));
builder.Services.AddDbContext<TravelCompanionDbContext>(options =>
{
    options.UseNpgsql(
        builder.Configuration.GetConnectionString("TravelCompanionDb"),
        npgsqlOptions => npgsqlOptions.EnableRetryOnFailure());
});
builder.Services.AddScoped<TravelNotificationScheduler>();
builder.Services.AddSingleton<INotificationSender, LoggingNotificationSender>();
builder.Services.AddHostedService<NotificationWorker>();

var host = builder.Build();
host.Run();

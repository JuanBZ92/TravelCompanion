using Android.App;
using Android.Content;
using Android.OS;
using AndroidX.Core.App;
using System.Text.Json;
using TravelCompanion.Mobile.Services;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Mobile.Platforms.Android;

public sealed class ReservationNotifications : ILocalReservationNotifications
{
    internal const string Channel = "reservation-reminders";
    private const string StoreName = "reservation-reminders";
    private static readonly object Gate = new();
    private static Context Context => global::Android.App.Application.Context;

    public async Task<bool> RequestPermissionAsync()
    {
        if (OperatingSystem.IsAndroidVersionAtLeast(33)
            && await Permissions.RequestAsync<ReminderPermission>() != PermissionStatus.Granted) return false;
        EnsureChannel();
        // Exact alarm access is optional: denied access uses the OS inexact fallback.
        var alarms = (AlarmManager)Context.GetSystemService(Context.AlarmService)!;
        if (OperatingSystem.IsAndroidVersionAtLeast(31) && !alarms.CanScheduleExactAlarms()
            && !Preferences.Default.Get("reminder-exact-permission-offered", false))
        {
            Preferences.Default.Set("reminder-exact-permission-offered", true);
            var english = System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "en";
            if (Shell.Current is { } shell && await shell.DisplayAlertAsync(
                english ? "Reservation reminders" : "Recordatorios de reservas",
                english ? "Allow alarms to receive reminders 3 hours and 45 minutes before. Without this permission Android may delay them."
                    : "Permití alarmas para recibir avisos 3 horas y 45 minutos antes. Sin este permiso Android puede demorarlos.",
                english ? "Settings" : "Configurar", english ? "Later" : "Más tarde"))
            {
                var intent = new Intent("android.settings.REQUEST_SCHEDULE_EXACT_ALARM",
                    global::Android.Net.Uri.Parse($"package:{Context.PackageName}"));
                intent.AddFlags(ActivityFlags.NewTask);
                Context.StartActivity(intent);
            }
        }
        return NotificationManagerCompat.From(Context)!.AreNotificationsEnabled();
    }

    public Task ReplaceAsync(IReadOnlyList<ReservationReminderDto> reminders)
    {
        lock (Gate)
        {
            var previous = Read();
            var upcoming = reminders.Where(item => item.NotifyAtUtc > DateTimeOffset.UtcNow).Take(60).ToList();
            // Persist first; a delivered alarm validates its version against this snapshot.
            using var prefs = Context.GetSharedPreferences(StoreName, FileCreationMode.Private)!;
            using var edit = prefs.Edit()!;
            edit.PutString("pending", JsonSerializer.Serialize(upcoming));
            if (!edit.Commit()) throw new IOException("Unable to persist reminders.");
            var alarms = (AlarmManager)Context.GetSystemService(Context.AlarmService)!;
            foreach (var old in previous)
            {
                using var pending = Pending(old);
                alarms.Cancel(pending);
                NotificationManagerCompat.From(Context)!.Cancel(old.Id, 0);
            }
            EnsureChannel();
            foreach (var item in upcoming) Schedule(item);
        }
        return Task.CompletedTask;
    }

    internal static void Restore()
    {
        lock (Gate)
        {
            EnsureChannel();
            foreach (var item in Read().Where(item => item.NotifyAtUtc > DateTimeOffset.UtcNow)) Schedule(item);
        }
    }

    private static List<ReservationReminderDto> Read()
    {
        using var prefs = Context.GetSharedPreferences(StoreName, FileCreationMode.Private)!;
        try { return JsonSerializer.Deserialize<List<ReservationReminderDto>>(prefs.GetString("pending", "[]")!) ?? []; }
        catch (JsonException) { return []; }
    }

    private static PendingIntent Pending(ReservationReminderDto reminder)
    {
        using var intent = new Intent(Context, typeof(ReservationAlarmReceiver));
        intent.SetData(global::Android.Net.Uri.Parse($"yuku-reminder:///{reminder.Id}"));
        intent.PutExtra("id", reminder.Id);
        intent.PutExtra("at", reminder.NotifyAtUtc.ToUnixTimeMilliseconds());
        return PendingIntent.GetBroadcast(Context, 0, intent, PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable)!;
    }

    private static void Schedule(ReservationReminderDto reminder)
    {
        var alarms = (AlarmManager)Context.GetSystemService(Context.AlarmService)!;
        using var pending = Pending(reminder);
        var at = reminder.NotifyAtUtc.ToUnixTimeMilliseconds();
        try
        {
            if (!OperatingSystem.IsAndroidVersionAtLeast(31) || alarms.CanScheduleExactAlarms())
                alarms.SetExactAndAllowWhileIdle(AlarmType.RtcWakeup, at, pending);
            else alarms.SetAndAllowWhileIdle(AlarmType.RtcWakeup, at, pending);
        }
        catch (Java.Lang.SecurityException)
        {
            // Access can be revoked between checking and scheduling.
            alarms.SetAndAllowWhileIdle(AlarmType.RtcWakeup, at, pending);
        }
    }

    internal static void Deliver(string? id, long at)
    {
        lock (Gate)
        {
            var reminder = Read().FirstOrDefault(item => item.Id == id && item.NotifyAtUtc.ToUnixTimeMilliseconds() == at);
            if (reminder is null || DateTimeOffset.UtcNow > reminder.NotifyAtUtc.AddMinutes(30)) return;
            EnsureChannel();
            using var launch = new Intent(Context, typeof(MainActivity));
            launch.AddFlags(ActivityFlags.SingleTop | ActivityFlags.ClearTop);
            using var tap = PendingIntent.GetActivity(Context, 0, launch, PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable);
            using var builder = new NotificationCompat.Builder(Context, Channel);
            builder.SetSmallIcon(Resource.Drawable.reminder_notification);
            builder.SetContentTitle(reminder.Title);
            builder.SetContentText(reminder.Body);
            using var style = new NotificationCompat.BigTextStyle();
            style.BigText(reminder.Body);
            builder.SetStyle(style);
            builder.SetContentIntent(tap);
            builder.SetAutoCancel(true);
            builder.SetVisibility(NotificationCompat.VisibilityPrivate);
            using var notification = builder.Build();
            if (NotificationManagerCompat.From(Context)!.AreNotificationsEnabled())
                NotificationManagerCompat.From(Context)!.Notify(reminder.Id, 0, notification);
        }
    }

    private static void EnsureChannel()
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(26)) return;
        var english = System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "en";
        using var channel = new NotificationChannel(Channel,
            english ? "Reservation reminders" : "Recordatorios de reservas", NotificationImportance.Default);
        ((NotificationManager)Context.GetSystemService(Context.NotificationService)!).CreateNotificationChannel(channel);
    }
}

public sealed class ReminderPermission : Permissions.BasePlatformPermission
{
    public override (string androidPermission, bool isRuntime)[] RequiredPermissions =>
        OperatingSystem.IsAndroidVersionAtLeast(33) ? [(global::Android.Manifest.Permission.PostNotifications, true)] : [];
}

[BroadcastReceiver(Enabled = true, Exported = false)]
public sealed class ReservationAlarmReceiver : BroadcastReceiver
{
    public override void OnReceive(Context? context, Intent? intent)
    {
        try { ReservationNotifications.Deliver(intent?.GetStringExtra("id"), intent?.GetLongExtra("at", 0) ?? 0); }
        catch (Exception exception) { System.Diagnostics.Trace.TraceError($"Reminder delivery failed: {exception.GetType().Name}"); }
    }
}

[BroadcastReceiver(Enabled = true, Exported = true)]
[IntentFilter([Intent.ActionBootCompleted, Intent.ActionMyPackageReplaced, "android.app.action.SCHEDULE_EXACT_ALARM_PERMISSION_STATE_CHANGED"])]
public sealed class ReservationBootReceiver : BroadcastReceiver
{
    public override void OnReceive(Context? context, Intent? intent)
    {
        if (intent?.Action is not (Intent.ActionBootCompleted or Intent.ActionMyPackageReplaced
            or "android.app.action.SCHEDULE_EXACT_ALARM_PERMISSION_STATE_CHANGED")) return;
        try { ReservationNotifications.Restore(); }
        catch (Exception exception) { System.Diagnostics.Trace.TraceError($"Reminder restore failed: {exception.GetType().Name}"); }
    }
}

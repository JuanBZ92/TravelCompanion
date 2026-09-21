using Microsoft.Extensions.Logging;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Mobile.Services;

public interface ILocalReservationNotifications
{
    Task<bool> RequestPermissionAsync();
    Task ReplaceAsync(IReadOnlyList<ReservationReminderDto> reminders);
    Task ShowTestAsync(string title, string body);
}

public sealed class ReservationReminderService(AuthSessionService session, TravelCompanionApiClient api,
    MobileBootstrapStore bootstrap, ILocalReservationNotifications notifications, ILogger<ReservationReminderService> logger)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private long _generation;
    private bool _started;

    public void Start()
    {
        if (_started) return;
        _started = true;
        session.StateChanged += (_, _) => Refresh();
        bootstrap.ScheduleUpdated += (_, _) => Refresh();
        api.ItineraryChanged += RefreshSafelyAsync;
        Connectivity.Current.ConnectivityChanged += (_, _) => Refresh();
        LocalizationResourceManager.Instance.CultureChanged += (_, _) => Refresh();
    }

    public void Refresh() => _ = RefreshSafelyAsync();

    public async Task ConfigureAsync()
    {
        try
        {
            Preferences.Default.Set("reminder-exact-permission-offered", false);
            var allowed = await notifications.RequestPermissionAsync();
            if (allowed) await RefreshSafelyAsync();
            else
            {
                var english = System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "en";
                if (await Shell.Current.DisplayAlertAsync(english ? "Reminders" : "Recordatorios",
                    english ? "Enable notifications in your phone settings to receive reservation reminders."
                        : "Activá las notificaciones en los ajustes del teléfono para recibir los recordatorios.",
                    english ? "Settings" : "Ajustes", english ? "Cancel" : "Cancelar")) AppInfo.ShowSettingsUI();
            }
        }
        catch (Exception exception) { logger.LogWarning(exception, "Unable to configure reminders."); }
    }

    public async Task ClearAsync()
    {
        Interlocked.Increment(ref _generation);
        await _gate.WaitAsync();
        try { await notifications.ReplaceAsync([]); }
        finally { _gate.Release(); }
    }

    private async Task RefreshSafelyAsync()
    {
        var generation = Interlocked.Read(ref _generation);
        var user = session.CurrentUserId;
        var trip = session.CurrentTripId;
        await _gate.WaitAsync();
        try
        {
            if (generation != Interlocked.Read(ref _generation) || user != session.CurrentUserId || trip != session.CurrentTripId) return;
            var scope = $"{user}:{trip}";
            if (Preferences.Default.Get("reminder-scope", "") != scope || !session.HasSession)
            {
                await notifications.ReplaceAsync([]);
                Preferences.Default.Set("reminder-scope", scope);
            }
            if (!session.HasSession || !trip.HasValue) return;
            if (Connectivity.Current.NetworkAccess != NetworkAccess.Internet) return;
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var token = await session.GetTokenAsync();
            if (string.IsNullOrEmpty(token)) return;
            var reminders = await api.GetReservationRemindersAsync(token, timeout.Token);
            if (generation != Interlocked.Read(ref _generation) || user != session.CurrentUserId || trip != session.CurrentTripId) return;
            if (reminders.Count > 0 && !await MainThread.InvokeOnMainThreadAsync(notifications.RequestPermissionAsync)) return;
            if (generation != Interlocked.Read(ref _generation) || user != session.CurrentUserId || trip != session.CurrentTripId) return;
            await notifications.ReplaceAsync(reminders);
        }
        catch (Exception exception)
        {
            // Keep the last scheduled snapshot if offline; never crash login or itinerary edits.
            logger.LogWarning(exception, "Unable to synchronize reservation reminders.");
        }
        finally { _gate.Release(); }
    }
}

public sealed class UnsupportedReservationNotifications : ILocalReservationNotifications
{
    public Task<bool> RequestPermissionAsync() => Task.FromResult(false);
    public Task ReplaceAsync(IReadOnlyList<ReservationReminderDto> reminders) => Task.CompletedTask;
    public Task ShowTestAsync(string title, string body) => Task.CompletedTask;
}

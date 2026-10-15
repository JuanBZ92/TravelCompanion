using Microsoft.Extensions.DependencyInjection;
using TravelCompanion.Mobile.Services;
using TravelCompanion.Mobile.ViewModels;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Mobile.Platforms.Android;

// Reproducible operation measurements on real Android. These are not frame/render timings.
// Only an explicitly instrumented, unlocked review app pointing to loopback can run it.
internal static class ReviewPerformanceRunner
{
    public const string ScenarioExtra = "yuku_performance_scenario";
    private static CancellationTokenSource? active;

    public static void Cancel() => active?.Cancel();

    public static async Task RunAsync(string? scenario)
    {
        if (!ReviewPerformancePolicy.IsScenario(scenario) || active is not null) return;
        var services = IPlatformApplication.Current?.Services;
        if (services is null) return;
        var session = services.GetRequiredService<AuthSessionService>();
        var api = services.GetRequiredService<TravelCompanionApiClient>();
        if (!ReviewPerformancePolicy.CanRun(MobileDiagnosticsSettings.IsEnabled, AppInfo.Current.PackageName,
                api.BaseAddress, session.HasSession, session.CurrentTripId, Shell.Current?.CurrentState?.Location.OriginalString)) return;

        using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        active = cancellation;
        var window = Platform.CurrentActivity?.Window;
        var alreadyKeptAwake = ((window?.Attributes?.Flags ?? 0) & global::Android.Views.WindowManagerFlags.KeepScreenOn) != 0;
        window?.AddFlags(global::Android.Views.WindowManagerFlags.KeepScreenOn);
        var context = session.ContextVersion;
        var userId = session.CurrentUserId;
        var tripId = session.CurrentTripId;
        EventHandler changed = (_, _) => { if (!ContextIsCurrent()) cancellation.Cancel(); };
        session.StateChanged += changed;
        using var canceledOperation = cancellation.Token.Register(() => MainThread.BeginInvokeOnMainThread(() =>
        {
            if (scenario == "trip") services.GetRequiredService<ScheduleViewModel>().CancelLoading();
            else if (scenario == "journal") services.GetRequiredService<JournalViewModel>().CancelLoading();
        }));
        var completed = 0;
        try
        {
            var token = await session.GetTokenAsync();
            EnsureCurrent();
            if (string.IsNullOrEmpty(token)) return;
            var bootstrap = services.GetRequiredService<MobileBootstrapStore>();
            var planner = services.GetRequiredService<DayPlanClient>();
            DayPlanOptionsDto? options = null;
            if (scenario == "planning")
            {
                options = await planner.OptionsAsync(token, cancellation.Token);
                EnsureCurrent();
                if (!options.Enabled || options.TripId != tripId || options.StartsOn is null) return;
            }
            ClientDiagnostics.Record("review_batch_started_measured", new() { Area = scenario });
            for (var iteration = 0; iteration < 35; iteration++)
            {
                EnsureCurrent();
                switch (scenario)
                {
                    case "bootstrap":
                        var result = await bootstrap.RefreshResultAsync(token, cancellationToken: cancellation.Token);
                        if (!result.IsSuccess) throw new InvalidOperationException("Review bootstrap failed.");
                        break;
                    case "trip":
                        var trip = services.GetRequiredService<ScheduleViewModel>();
                        await trip.LoadScheduleCommand.ExecuteAsync(null);
                        if (trip.HasError || !trip.HasLoaded) throw new InvalidOperationException("Review trip load failed.");
                        break;
                    case "journal":
                        var journal = services.GetRequiredService<JournalViewModel>();
                        // Wait for the following sync outside the local-open measurement,
                        // keeping successive measured intervals from overlapping each other.
                        await journal.RefreshCommand.ExecuteAsync(null);
                        if (journal.HasError || !journal.HasLoaded) throw new InvalidOperationException("Review journal load failed.");
                        break;
                    case "planning":
                        await planner.GenerateAsync(token, new(tripId!.Value, options!.Revision, options.StartsOn!.Value,
                            1, Guid.NewGuid(), new("balanced", "medium", ["culture", "food"]), "es"), cancellation.Token);
                        break;
                }
                EnsureCurrent();
                completed++;
                // Yield between operations without altering global animation/display settings.
                await Task.Delay(100, cancellation.Token);
            }
            ClientDiagnostics.Record("review_batch_completed_measured", new() { Area = scenario, Status = 200, EventId = completed });
        }
        catch (OperationCanceledException)
        {
            ClientDiagnostics.Record("review_batch_completed_measured", new() { Area = scenario, Status = 499, EventId = completed, Canceled = true });
        }
        catch (Exception)
        {
            // No user content, paths, tokens or remote exception messages in measurements.
            ClientDiagnostics.Record("review_batch_completed_measured", new() { Area = scenario, Status = 500, EventId = completed });
        }
        finally
        {
            session.StateChanged -= changed;
            if (!alreadyKeptAwake) window?.ClearFlags(global::Android.Views.WindowManagerFlags.KeepScreenOn);
            if (ReferenceEquals(active, cancellation)) active = null;
        }

        void EnsureCurrent()
        {
            cancellation.Token.ThrowIfCancellationRequested();
            if (!ContextIsCurrent()
                || !ReviewPerformancePolicy.CanRun(MobileDiagnosticsSettings.IsEnabled, AppInfo.Current.PackageName,
                    api.BaseAddress, session.HasSession, session.CurrentTripId, Shell.Current?.CurrentState?.Location.OriginalString))
                throw new OperationCanceledException(cancellation.Token);
        }

        bool ContextIsCurrent() => ReviewPerformancePolicy.IsCurrent(context, session.ContextVersion,
            userId, session.CurrentUserId, tripId, session.CurrentTripId, session.HasSession);
    }
}

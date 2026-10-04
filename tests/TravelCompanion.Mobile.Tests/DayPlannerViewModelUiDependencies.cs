using TravelCompanion.Shared.Dtos;

// Platform boundaries only. Tests execute the production planner ViewModel, client and store.
public enum NetworkAccess { None, Internet }
public static class Connectivity
{
    public static ConnectivityState Current { get; } = new();
    public sealed class ConnectivityState { public NetworkAccess NetworkAccess { get; set; } = NetworkAccess.Internet; }
}
public sealed record Color(string Value)
{
    public static Color FromArgb(string value) => new(value);
}
public static class Colors { public static Color White { get; } = new("#FFFFFF"); }
public sealed class ShellNavigationQueryParameters : Dictionary<string, object>;
public sealed class Shell
{
    public static Shell Current { get; set; } = new();
    public List<(string Route, ShellNavigationQueryParameters Parameters)> Navigations { get; } = [];
    public Task GoToAsync(string route, ShellNavigationQueryParameters parameters)
    { Navigations.Add((route, parameters)); return Task.CompletedTask; }
    public Task DisplayAlertAsync(string title, string message, string cancel) => Task.CompletedTask;
}
public static class SemanticScreenReader
{
    public static ScreenReader Default { get; } = new();
    public sealed class ScreenReader { public void Announce(string? message) { } }
}

namespace TravelCompanion.Mobile.Pages
{
    public sealed class DayPlanProposalPage;
    public sealed class RecommendationDetailPage;
    public sealed class TripReviewPage;
}

namespace TravelCompanion.Mobile.Services
{
    public sealed class MobileBootstrapStore
    {
        public int InvalidationCount { get; private set; }
        public Task<OfflineCacheResult<MobileBootstrapDto>?> GetCachedAsync(
            string? destinationSlug = null, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<OfflineCacheResult<MobileBootstrapDto>?>(null);
        }
        public void Invalidate() => InvalidationCount++;
    }
    public static class PaywallNavigation
    {
        public static Task OpenAsync(PaywallEntryPoint entryPoint, bool limitReached = false) =>
            Shell.Current.GoToAsync("pass", new() { ["EntryPoint"] = entryPoint });
    }
}

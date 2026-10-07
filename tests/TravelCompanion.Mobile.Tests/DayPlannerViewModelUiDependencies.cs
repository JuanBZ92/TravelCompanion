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
public class Shell
{
    public static Shell Current { get; set; } = new();
    public TestModalNavigation Navigation { get; } = new();
    public List<(string Route, ShellNavigationQueryParameters Parameters)> Navigations { get; } = [];
    public Task GoToAsync(string route, ShellNavigationQueryParameters parameters)
    { Navigations.Add((route, parameters)); return Task.CompletedTask; }
    public Task GoToAsync(string route) => GoToAsync(route, new());
    public Func<string, string[], Task<string>> SelectAction { get; set; } = (_, _) => Task.FromResult("Cancel");
    public Task<string> DisplayActionSheetAsync(string title, string cancel, string? destruction, params string[] buttons) => SelectAction(title, buttons);
    public Task DisplayAlertAsync(string title, string message, string cancel) => Task.CompletedTask;
    public Task<bool> DisplayAlertAsync(string title, string message, string accept, string cancel) => Task.FromResult(false);
    public Func<string, Task<string?>> Prompt { get; set; } = _ => Task.FromResult<string?>(null);
    public Task<string?> DisplayPromptAsync(string title, string message, string accept = "OK", string cancel = "Cancel",
        string? placeholder = null, int maxLength = -1, object? keyboard = null, string initialValue = "") => Prompt(title);
}
public static class Clipboard { public static Task SetTextAsync(string text) => Task.CompletedTask; }
public sealed class TestModalNavigation
{
    public List<object> Modals { get; } = [];
    public Task PushModalAsync(object page, bool animated = true) { Modals.Add(page); return Task.CompletedTask; }
    public Task PopModalAsync(bool animated = true) { if (Modals.Count > 0) Modals.RemoveAt(Modals.Count - 1); return Task.CompletedTask; }
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
    public sealed class DocsPage;
}

namespace TravelCompanion.Mobile.Services
{
    public sealed class MobileBootstrapStore
    {
        public int InvalidationCount { get; private set; }
        public MobileBootstrapDto? Value { get; set; }
        public Func<CancellationToken, Task<OfflineCacheResult<MobileBootstrapDto>?>>? ReadCached { get; set; }
        public int RefreshRequests { get; private set; }
        public Func<string, CancellationToken, Task<MobileBootstrapDto?>>? Refresh { get; set; }
        public Task<OfflineCacheResult<MobileBootstrapDto>?> GetCachedAsync(
            string? destinationSlug = null, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (ReadCached is not null) return ReadCached(cancellationToken);
            return Task.FromResult(Value is null ? null : new OfflineCacheResult<MobileBootstrapDto>(Value, DateTimeOffset.UtcNow));
        }
        public void Invalidate() => InvalidationCount++;
        public Task<MobileBootstrapDto?> RefreshAsync(string token, string? destinationSlug = null,
            CancellationToken cancellationToken = default)
        {
            RefreshRequests++;
            cancellationToken.ThrowIfCancellationRequested();
            return Refresh?.Invoke(token, cancellationToken) ?? Task.FromResult(Value);
        }
    }
    public static class PaywallNavigation
    {
        public static Task OpenAsync(PaywallEntryPoint entryPoint, bool limitReached = false) =>
            Shell.Current.GoToAsync("pass", new() { ["EntryPoint"] = entryPoint });
    }
}

using TravelCompanion.Shared.Dtos;
using Microsoft.Extensions.DependencyInjection;

public static class Keyboard
{
    public static object Email { get; } = new();
    public static object Numeric { get; } = new();
}
public static class AppInfo
{
    public static AppInfoState Current { get; } = new();
    public sealed class AppInfoState { public string VersionString => "1.0-test"; }
}

namespace TravelCompanion.Mobile
{
    public static class MauiProgram
    {
        public static IServiceProvider Services { get; set; } = new Microsoft.Extensions.DependencyInjection.ServiceCollection().BuildServiceProvider();
    }
}
namespace TravelCompanion.Mobile.Pages
{
    public sealed class ItineraryItemEditorPage;
}
namespace TravelCompanion.Mobile.Services
{
    public sealed class SessionLogoutService
    {
        public Func<Task> Reset { get; set; } = () => Task.CompletedTask;
        public Task ResetContentAsync(Guid? userId, bool preservePendingItineraryAction = false) => Reset();
    }
    public static class BuilderSetupNavigation { public static Task OpenAsync() => Task.CompletedTask; }
    public sealed record StoreProductOffer(string ProductId, string? LocalizedPrice, string? CurrencyCode, bool IsAvailable, string? Error = null);
    public sealed record StorePurchaseResult(PurchaseIntentState State, string? Evidence, StoreEnvironment Environment, string? Error = null);
    // The native store is an I/O boundary. Tests execute the actual PaywallViewModel and recovery/store code.
    public interface IStorePurchaseService
    {
        StoreProvider Provider { get; }
        Task<StoreProductOffer> GetProductAsync(string productId, CancellationToken cancellationToken = default);
        Task<StorePurchaseResult> PurchaseAsync(string productId, string opaqueAccountId, CancellationToken cancellationToken = default);
        Task<IReadOnlyList<string>> RestoreAsync(CancellationToken cancellationToken = default);
        Task FinishAsync(string evidence, CancellationToken cancellationToken = default);
    }
    public sealed class ControlledPurchaseStore : IStorePurchaseService
    {
        public StoreProvider Provider => StoreProvider.Google;
        public int ProductCalls { get; private set; }
        public int PurchaseCalls { get; private set; }
        public int RestoreCalls { get; private set; }
        public int FinishCalls { get; private set; }
        public Func<Task<StoreProductOffer>> Product { get; set; } = () => Task.FromResult(new StoreProductOffer("test.pass", "€10", "EUR", true));
        public Func<Task<StorePurchaseResult>> Purchase { get; set; } = () => Task.FromResult(new StorePurchaseResult(PurchaseIntentState.Active, "synthetic-receipt", StoreEnvironment.Sandbox));
        public Func<Task<IReadOnlyList<string>>> Restore { get; set; } = () => Task.FromResult<IReadOnlyList<string>>([]);
        public Func<Task> Finish { get; set; } = () => Task.CompletedTask;
        public Task<StoreProductOffer> GetProductAsync(string productId, CancellationToken ct = default) { ProductCalls++; return Product(); }
        public Task<StorePurchaseResult> PurchaseAsync(string productId, string accountId, CancellationToken ct = default) { PurchaseCalls++; return Purchase(); }
        public Task<IReadOnlyList<string>> RestoreAsync(CancellationToken ct = default) { RestoreCalls++; return Restore(); }
        public Task FinishAsync(string evidence, CancellationToken ct = default) { FinishCalls++; return Finish(); }
    }
}

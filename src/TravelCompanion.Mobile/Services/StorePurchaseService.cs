using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Mobile.Services;

public sealed record StoreProductOffer(string ProductId, string? LocalizedPrice, string? CurrencyCode, bool IsAvailable, string? Error = null);
public sealed record StorePurchaseResult(PurchaseIntentState State, string? Evidence, StoreEnvironment Environment, string? Error = null);

public interface IStorePurchaseService
{
    StoreProvider Provider { get; }
    Task<StoreProductOffer> GetProductAsync(string productId, CancellationToken cancellationToken = default);
    Task<StorePurchaseResult> PurchaseAsync(string productId, string opaqueAccountId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<string>> RestoreAsync(CancellationToken cancellationToken = default);
    Task FinishAsync(string evidence, CancellationToken cancellationToken = default);
}

public sealed partial class NativeStorePurchaseService : IStorePurchaseService
{
    public partial StoreProvider Provider { get; }
    public partial Task<StoreProductOffer> GetProductAsync(string productId, CancellationToken cancellationToken = default);
    public partial Task<StorePurchaseResult> PurchaseAsync(string productId, string opaqueAccountId, CancellationToken cancellationToken = default);
    public partial Task<IReadOnlyList<string>> RestoreAsync(CancellationToken cancellationToken = default);
    public partial Task FinishAsync(string evidence, CancellationToken cancellationToken = default);
}

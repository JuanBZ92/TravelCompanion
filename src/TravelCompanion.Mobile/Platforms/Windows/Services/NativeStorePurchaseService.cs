using TravelCompanion.Shared.Dtos;
namespace TravelCompanion.Mobile.Services;
public sealed partial class NativeStorePurchaseService
{
    public partial StoreProvider Provider => StoreProvider.AdminPin;
    public partial Task<StoreProductOffer> GetProductAsync(string productId, CancellationToken cancellationToken) => Task.FromResult(new StoreProductOffer(productId, null, null, false));
    public partial Task<StorePurchaseResult> PurchaseAsync(string productId, string opaqueAccountId, CancellationToken cancellationToken) => Task.FromResult(new StorePurchaseResult(PurchaseIntentState.Failed, null, StoreEnvironment.Production));
    public partial Task<IReadOnlyList<string>> RestoreAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<string>>([]);
    public partial Task FinishAsync(string evidence, CancellationToken cancellationToken) => Task.CompletedTask;
}

using System.Text.Json;
using Android.BillingClient.Api;
using Microsoft.Maui.ApplicationModel;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Mobile.Services;

public sealed partial class NativeStorePurchaseService
{
    private readonly SemaphoreSlim billingGate = new(1, 1);
    private BillingClient? billingClient;
    private TaskCompletionSource<StorePurchaseResult>? activePurchase;
    private ProductDetails? cachedProduct;

    public partial StoreProvider Provider => StoreProvider.Google;

    public partial async Task<StoreProductOffer> GetProductAsync(string productId, CancellationToken cancellationToken)
    {
        try
        {
            var client = await GetClientAsync(cancellationToken);
            var product = QueryProductDetailsParams.Product.NewBuilder()
                .SetProductId(productId).SetProductType(BillingClient.ProductType.Inapp).Build();
            var request = QueryProductDetailsParams.NewBuilder().SetProductList([product]).Build();
            var response = await client.QueryProductDetailsAsync(request).WaitAsync(cancellationToken);
            cachedProduct = response.ProductDetailsList.FirstOrDefault(item => item.ProductId == productId);
            var offer = cachedProduct?.GetOneTimePurchaseOfferDetails()
                ?? cachedProduct?.OneTimePurchaseOfferDetailsList?.FirstOrDefault();
            return cachedProduct is null || offer is null
                ? new(productId, null, null, false, LocalizationResourceManager.Instance.GetString("PaywallUnavailable"))
                : new(productId, offer.FormattedPrice, offer.PriceCurrencyCode, true);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return new(productId, null, null, false, exception.Message);
        }
    }

    public partial async Task<StorePurchaseResult> PurchaseAsync(
        string productId, string opaqueAccountId, CancellationToken cancellationToken)
    {
        var activity = Platform.CurrentActivity;
        if (activity is null)
            return new(PurchaseIntentState.Failed, null, StoreEnvironment.Production,
                LocalizationResourceManager.Instance.GetString("PaywallUnavailable"));
        var offer = await GetProductAsync(productId, cancellationToken);
        if (!offer.IsAvailable || cachedProduct is null)
            return new(PurchaseIntentState.Failed, null, StoreEnvironment.Production, offer.Error);

        var productParams = BillingFlowParams.ProductDetailsParams.NewBuilder()
            .SetProductDetails(cachedProduct!).Build();
        var flow = BillingFlowParams.NewBuilder()
            .SetProductDetailsParamsList([productParams])
            .SetObfuscatedAccountId(opaqueAccountId)
            .Build();
        var client = await GetClientAsync(cancellationToken);
        activePurchase = new(TaskCreationOptions.RunContinuationsAsynchronously);
        using var registration = cancellationToken.Register(() => activePurchase.TrySetCanceled(cancellationToken));
        var launched = client.LaunchBillingFlow(activity, flow);
        if (launched.ResponseCode != BillingResponseCode.Ok)
        {
            activePurchase = null;
            return MapFailure(launched);
        }
        try { return await activePurchase.Task; }
        finally { activePurchase = null; }
    }

    public partial async Task<IReadOnlyList<string>> RestoreAsync(CancellationToken cancellationToken)
    {
        var client = await GetClientAsync(cancellationToken);
        var request = QueryPurchasesParams.NewBuilder().SetProductType(BillingClient.ProductType.Inapp).Build();
        var response = await client.QueryPurchasesAsync(request).WaitAsync(cancellationToken);
        if (response.Result.ResponseCode != BillingResponseCode.Ok) return [];
        return response.Purchases
            .Where(item => item.PurchaseState is PurchaseState.Purchased or PurchaseState.Pending)
            .Select(ToEvidence).Distinct(StringComparer.Ordinal).ToList();
    }

    public partial Task FinishAsync(string evidence, CancellationToken cancellationToken) => Task.CompletedTask;

    private async Task<BillingClient> GetClientAsync(CancellationToken cancellationToken)
    {
        if (billingClient?.IsReady == true) return billingClient;
        await billingGate.WaitAsync(cancellationToken);
        try
        {
            if (billingClient?.IsReady == true) return billingClient;
            var pending = PendingPurchasesParams.NewBuilder().EnableOneTimeProducts().Build();
            if (billingClient is null)
            {
                var builder = BillingClient.NewBuilder(Android.App.Application.Context)
                    .EnablePendingPurchases(pending).EnableAutoServiceReconnection();
                builder.SetListener(OnPurchasesUpdated);
                billingClient = builder.Build();
            }
            var result = await billingClient.StartConnectionAsync().WaitAsync(cancellationToken);
            if (result.ResponseCode != BillingResponseCode.Ok)
                throw new InvalidOperationException(result.DebugMessage);
            return billingClient;
        }
        finally { billingGate.Release(); }
    }

    private void OnPurchasesUpdated(BillingResult result, IList<Purchase> purchases)
    {
        var completion = activePurchase;
        if (completion is null) return;
        if (result.ResponseCode != BillingResponseCode.Ok)
        {
            completion.TrySetResult(MapFailure(result));
            return;
        }
        var purchase = purchases.FirstOrDefault();
        if (purchase is null)
        {
            completion.TrySetResult(new(PurchaseIntentState.Failed, null, StoreEnvironment.Production,
                result.DebugMessage));
            return;
        }
        completion.TrySetResult(purchase.PurchaseState == PurchaseState.Pending
            ? new(PurchaseIntentState.Pending, ToEvidence(purchase), StoreEnvironment.Production)
            : new(PurchaseIntentState.Verifying, ToEvidence(purchase), StoreEnvironment.Production));
    }

    private static StorePurchaseResult MapFailure(BillingResult result) =>
        result.ResponseCode == BillingResponseCode.UserCancelled
            ? new(PurchaseIntentState.Cancelled, null, StoreEnvironment.Production)
            : new(PurchaseIntentState.Failed, null, StoreEnvironment.Production, result.DebugMessage);

    private static string ToEvidence(Purchase purchase) => JsonSerializer.Serialize(new
    {
        purchaseToken = purchase.PurchaseToken,
        orderId = purchase.OrderId,
        purchaseTime = purchase.PurchaseTime,
        products = purchase.Products,
        originalJson = purchase.OriginalJson,
        signature = purchase.Signature
    });
}

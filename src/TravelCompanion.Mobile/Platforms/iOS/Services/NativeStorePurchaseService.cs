using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Mobile.Services;

public sealed partial class NativeStorePurchaseService
{
    private static readonly NativeCallback Callback = CompleteNativeCall;
    private static readonly IntPtr CallbackPointer = Marshal.GetFunctionPointerForDelegate(Callback);

    public partial StoreProvider Provider => StoreProvider.Apple;

    public partial async Task<StoreProductOffer> GetProductAsync(string productId, CancellationToken cancellationToken)
    {
        var json = await InvokeAsync((context, callback) => QueryProduct(productId, context, callback), cancellationToken);
        var result = JsonSerializer.Deserialize<NativeProductResult>(json, JsonOptions);
        return result is null
            ? new(productId, null, null, false, LocalizationResourceManager.Instance.GetString("PaywallUnavailable"))
            : new(productId, result.LocalizedPrice, result.CurrencyCode, result.Available, result.Error);
    }

    public partial async Task<StorePurchaseResult> PurchaseAsync(
        string productId, string opaqueAccountId, CancellationToken cancellationToken)
    {
        var json = await InvokeAsync((context, callback) => Purchase(productId, opaqueAccountId, context, callback), cancellationToken);
        var result = JsonSerializer.Deserialize<NativePurchaseResult>(json, JsonOptions);
        if (result is null)
            return new(PurchaseIntentState.Failed, null, StoreEnvironment.Production,
                LocalizationResourceManager.Instance.GetString("PaywallUnavailable"));
        var state = result.State switch
        {
            "cancelled" => PurchaseIntentState.Cancelled,
            "pending" => PurchaseIntentState.Pending,
            "verifying" => PurchaseIntentState.Verifying,
            _ => PurchaseIntentState.Failed
        };
        return new(state, result.Evidence, DetectEnvironment(result.Evidence), result.Error);
    }

    public partial async Task<IReadOnlyList<string>> RestoreAsync(CancellationToken cancellationToken)
    {
        var json = await InvokeAsync(Restore, cancellationToken);
        var result = JsonSerializer.Deserialize<NativeRestoreResult>(json, JsonOptions);
        if (!string.IsNullOrWhiteSpace(result?.Error)) throw new InvalidOperationException(result.Error);
        return result?.Evidence ?? [];
    }

    public partial async Task FinishAsync(string evidence, CancellationToken cancellationToken)
    {
        var transactionId = ReadTransactionId(evidence);
        if (string.IsNullOrWhiteSpace(transactionId)) return;
        await InvokeAsync((context, callback) => Finish(transactionId, context, callback), cancellationToken);
    }

    private static async Task<string> InvokeAsync(Action<IntPtr, IntPtr> nativeCall, CancellationToken cancellationToken)
    {
        var completion = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var handle = GCHandle.Alloc(completion);
        try
        {
            nativeCall(GCHandle.ToIntPtr(handle), CallbackPointer);
        }
        catch
        {
            handle.Free();
            throw;
        }
        return await completion.Task.WaitAsync(cancellationToken);
    }

    private static void CompleteNativeCall(IntPtr context, IntPtr json)
    {
        var handle = GCHandle.FromIntPtr(context);
        try
        {
            if (handle.Target is TaskCompletionSource<string> completion)
                completion.TrySetResult(Marshal.PtrToStringUTF8(json) ?? "{}");
        }
        finally
        {
            handle.Free();
        }
    }

    private static StoreEnvironment DetectEnvironment(string? evidence)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(evidence)) return StoreEnvironment.Production;
            using var payload = ReadJwsPayload(evidence);
            return payload.RootElement.TryGetProperty("environment", out var environment)
                && string.Equals(environment.GetString(), "Sandbox", StringComparison.OrdinalIgnoreCase)
                ? StoreEnvironment.Sandbox : StoreEnvironment.Production;
        }
        catch { return StoreEnvironment.Production; }
    }

    private static string? ReadTransactionId(string evidence)
    {
        try
        {
            using var payload = ReadJwsPayload(evidence);
            return payload.RootElement.TryGetProperty("transactionId", out var transaction)
                ? transaction.GetString() : null;
        }
        catch { return null; }
    }

    private static JsonDocument ReadJwsPayload(string evidence)
    {
        var parts = evidence.Split('.');
        if (parts.Length != 3) throw new FormatException("Invalid StoreKit transaction.");
        var value = parts[1].Replace('-', '+').Replace('_', '/');
        value = value.PadRight(value.Length + (4 - value.Length % 4) % 4, '=');
        return JsonDocument.Parse(Convert.FromBase64String(value));
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void NativeCallback(IntPtr context, IntPtr json);

    [DllImport("__Internal", EntryPoint = "yuku_store_query_product")]
    private static extern void QueryProduct(string productId, IntPtr context, IntPtr callback);

    [DllImport("__Internal", EntryPoint = "yuku_store_purchase")]
    private static extern void Purchase(string productId, string appAccountToken, IntPtr context, IntPtr callback);

    [DllImport("__Internal", EntryPoint = "yuku_store_restore")]
    private static extern void Restore(IntPtr context, IntPtr callback);

    [DllImport("__Internal", EntryPoint = "yuku_store_finish")]
    private static extern void Finish(string transactionId, IntPtr context, IntPtr callback);

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private sealed record NativeProductResult(bool Available, string? LocalizedPrice, string? CurrencyCode, string? Error);
    private sealed record NativePurchaseResult(string State, string? Evidence, string? Error);
    private sealed record NativeRestoreResult(
        [property: JsonPropertyName("evidence")] IReadOnlyList<string> Evidence,
        string? Error = null);
}

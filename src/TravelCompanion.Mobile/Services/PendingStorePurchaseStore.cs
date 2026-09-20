using System.Text.Json;
using System.Text.Json.Serialization;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Mobile.Services;

public sealed record PendingStorePurchase(
    Guid UserId,
    Guid TripId,
    Guid IntentId,
    StoreProvider Provider,
    string ProductId,
    string OpaqueAccountId,
    StoreEnvironment Environment,
    string? Evidence,
    DateTimeOffset UpdatedAtUtc);

public sealed class PendingStorePurchaseStore
{
    private const string StorageKey = "pending_store_purchase_v1";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public async Task SaveAsync(PendingStorePurchase purchase)
    {
        await SecureStorage.Default.SetAsync(StorageKey, JsonSerializer.Serialize(purchase, JsonOptions));
    }

    public async Task<PendingStorePurchase?> GetAsync()
    {
        try
        {
            var value = await SecureStorage.Default.GetAsync(StorageKey);
            return string.IsNullOrWhiteSpace(value)
                ? null
                : JsonSerializer.Deserialize<PendingStorePurchase>(value, JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    public void Clear() => SecureStorage.Default.Remove(StorageKey);
}

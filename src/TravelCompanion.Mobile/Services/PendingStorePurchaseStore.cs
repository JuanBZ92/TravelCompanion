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
    private const string UserStoragePrefix = "pending_store_purchases_v2_";
    private readonly SemaphoreSlim gate = new(1, 1);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public async Task SaveAsync(PendingStorePurchase purchase)
    {
        await gate.WaitAsync();
        try
        {
            var pending = await ReadForUserAsync(purchase.UserId);
            pending.RemoveAll(item => item.IntentId == purchase.IntentId);
            pending.Add(purchase);
            await SecureStorage.Default.SetAsync(UserStoragePrefix + purchase.UserId.ToString("N"),
                JsonSerializer.Serialize(pending, JsonOptions));
            await RemoveLegacyAsync(purchase.UserId);
        }
        finally { gate.Release(); }
    }

    public async Task<PendingStorePurchase?> GetAsync(Guid userId, Guid? tripId = null)
    {
        await gate.WaitAsync();
        try
        {
            return (await ReadForUserAsync(userId))
                .Where(item => !tripId.HasValue || item.TripId == tripId)
                .OrderBy(item => item.UpdatedAtUtc).ThenBy(item => item.IntentId).FirstOrDefault();
        }
        catch
        {
            return null;
        }
        finally { gate.Release(); }
    }

    public async Task ClearAsync(PendingStorePurchase purchase)
    {
        await gate.WaitAsync();
        try
        {
            var pending = await ReadForUserAsync(purchase.UserId);
            pending.RemoveAll(item => item.IntentId == purchase.IntentId);
            var key = UserStoragePrefix + purchase.UserId.ToString("N");
            if (pending.Count == 0) SecureStorage.Default.Remove(key);
            else await SecureStorage.Default.SetAsync(key, JsonSerializer.Serialize(pending, JsonOptions));
            await RemoveLegacyAsync(purchase.UserId);
        }
        finally { gate.Release(); }
    }

    public async Task ClearUserAsync(Guid userId)
    {
        await gate.WaitAsync();
        try
        {
            SecureStorage.Default.Remove(UserStoragePrefix + userId.ToString("N"));
            await RemoveLegacyAsync(userId);
        }
        finally { gate.Release(); }
    }

    private static async Task<List<PendingStorePurchase>> ReadForUserAsync(Guid userId)
    {
        var value = await SecureStorage.Default.GetAsync(UserStoragePrefix + userId.ToString("N"));
        var pending = ReadValue<List<PendingStorePurchase>>(value) ?? [];
        var legacyValue = await SecureStorage.Default.GetAsync(StorageKey);
        var legacy = ReadValue<PendingStorePurchase>(legacyValue);
        if (legacy?.UserId == userId && pending.All(item => item.IntentId != legacy.IntentId)) pending.Add(legacy);
        return pending.Where(item => item.UserId == userId).ToList();
    }

    private static async Task RemoveLegacyAsync(Guid userId)
    {
        var value = await SecureStorage.Default.GetAsync(StorageKey);
        if (ReadValue<PendingStorePurchase>(value)?.UserId == userId)
            SecureStorage.Default.Remove(StorageKey);
    }

    private static T? ReadValue<T>(string? value) where T : class
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        try { return JsonSerializer.Deserialize<T>(value, JsonOptions); }
        catch (JsonException) { return null; }
    }
}

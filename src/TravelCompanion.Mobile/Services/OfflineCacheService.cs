using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TravelCompanion.Mobile.Services;

public sealed class OfflineCacheService
{
    private const string EncryptionKeyStorageKey = "offline_cache_encryption_key_v1";
    private const string EncryptionVersion = "v1";
    private static readonly byte[] EncryptionContext = Encoding.UTF8.GetBytes("travelcompanion-offline-cache");
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public async Task SaveAsync<T>(string key, T value, CancellationToken cancellationToken = default)
    {
        var entry = new OfflineCacheEntry<T>(DateTimeOffset.UtcNow, value);
        await SaveEntryEncryptedAsync(key, entry, cancellationToken).ConfigureAwait(false);
    }

    public async Task<OfflineCacheResult<T>?> GetAsync<T>(string key, CancellationToken cancellationToken = default)
    {
        var path = GetPath(key);
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            var json = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);

            var encryptedEntry = await TryReadEncryptedEntryAsync<T>(json, cancellationToken).ConfigureAwait(false);
            if (encryptedEntry is not null)
            {
                return new OfflineCacheResult<T>(encryptedEntry.Value, encryptedEntry.SavedAt);
            }

            // Compatibilidad con caches legacy en texto plano; se migran automaticamente a cifrado.
            var legacyEntry = JsonSerializer.Deserialize<OfflineCacheEntry<T>>(json, JsonOptions);
            if (legacyEntry is null)
            {
                return null;
            }

            await SaveEntryEncryptedAsync(key, legacyEntry, cancellationToken).ConfigureAwait(false);
            return new OfflineCacheResult<T>(legacyEntry.Value, legacyEntry.SavedAt);
        }
        catch
        {
            return null;
        }
    }

    public Task DeleteAsync(string key)
    {
        var path = GetPath(key);
        if (File.Exists(path))
        {
            File.Delete(path);
        }

        return Task.CompletedTask;
    }

    public static string FormatSavedAt(DateTimeOffset savedAt)
    {
        var local = savedAt.ToLocalTime();
        return $"Datos guardados el {local:dd/MM HH:mm}.";
    }

    private static string GetPath(string key)
    {
        var safeKey = string.Concat(key.Select(character =>
            char.IsLetterOrDigit(character) || character is '-' or '_'
                ? character
                : '_'));

        return Path.Combine(FileSystem.AppDataDirectory, "offline-cache", $"{safeKey}.json");
    }

    private async Task SaveEntryEncryptedAsync<T>(
        string cacheKey,
        OfflineCacheEntry<T> entry,
        CancellationToken cancellationToken)
    {
        var key = await GetOrCreateEncryptionKeyAsync().ConfigureAwait(false);
        var nonce = RandomNumberGenerator.GetBytes(12);
        var plaintext = JsonSerializer.SerializeToUtf8Bytes(entry, JsonOptions);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[16];

        try
        {
            using var aes = new AesGcm(key, tag.Length);
            aes.Encrypt(nonce, plaintext, ciphertext, tag, EncryptionContext);

            var envelope = new OfflineCacheEnvelope(
                EncryptionVersion,
                Convert.ToBase64String(nonce),
                Convert.ToBase64String(tag),
                Convert.ToBase64String(ciphertext));

            var json = JsonSerializer.Serialize(envelope, JsonOptions);
            var path = GetPath(cacheKey);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, json, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
            CryptographicOperations.ZeroMemory(ciphertext);
            CryptographicOperations.ZeroMemory(tag);
        }
    }

    private async Task<OfflineCacheEntry<T>?> TryReadEncryptedEntryAsync<T>(
        string json,
        CancellationToken cancellationToken)
    {
        var envelope = JsonSerializer.Deserialize<OfflineCacheEnvelope>(json, JsonOptions);
        if (envelope is null || !string.Equals(envelope.Version, EncryptionVersion, StringComparison.Ordinal))
        {
            return null;
        }

        byte[]? plaintext = null;
        try
        {
            var nonce = Convert.FromBase64String(envelope.Nonce);
            var tag = Convert.FromBase64String(envelope.Tag);
            var ciphertext = Convert.FromBase64String(envelope.Ciphertext);

            var key = await GetOrCreateEncryptionKeyAsync().ConfigureAwait(false);
            plaintext = new byte[ciphertext.Length];

            using var aes = new AesGcm(key, tag.Length);
            aes.Decrypt(nonce, ciphertext, tag, plaintext, EncryptionContext);

            var entry = JsonSerializer.Deserialize<OfflineCacheEntry<T>>(plaintext, JsonOptions);
            return entry;
        }
        catch
        {
            return null;
        }
        finally
        {
            if (plaintext is not null)
            {
                CryptographicOperations.ZeroMemory(plaintext);
            }
        }
    }

    private static async Task<byte[]> GetOrCreateEncryptionKeyAsync()
    {
        var encodedKey = await SecureStorage.Default.GetAsync(EncryptionKeyStorageKey).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(encodedKey))
        {
            try
            {
                var existingKey = Convert.FromBase64String(encodedKey);
                if (existingKey.Length == 32)
                {
                    return existingKey;
                }
            }
            catch
            {
                // Si el valor almacenado esta corrupto, regeneramos.
            }
        }

        var newKey = RandomNumberGenerator.GetBytes(32);
        await SecureStorage.Default.SetAsync(EncryptionKeyStorageKey, Convert.ToBase64String(newKey)).ConfigureAwait(false);
        return newKey;
    }

    private sealed record OfflineCacheEntry<T>(DateTimeOffset SavedAt, T Value);
    private sealed record OfflineCacheEnvelope(string Version, string Nonce, string Tag, string Ciphertext);
}

public sealed record OfflineCacheResult<T>(T Value, DateTimeOffset SavedAt);

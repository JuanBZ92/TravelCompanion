using System.Text.Json;
using System.Text.Json.Serialization;

namespace TravelCompanion.Mobile.Services;

public sealed class OfflineCacheService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public async Task SaveAsync<T>(string key, T value, CancellationToken cancellationToken = default)
    {
        var entry = new OfflineCacheEntry<T>(DateTimeOffset.UtcNow, value);
        var json = JsonSerializer.Serialize(entry, JsonOptions);
        var path = GetPath(key);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, json, cancellationToken);
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
            var json = await File.ReadAllTextAsync(path, cancellationToken);
            var entry = JsonSerializer.Deserialize<OfflineCacheEntry<T>>(json, JsonOptions);
            return entry is null
                ? null
                : new OfflineCacheResult<T>(entry.Value, entry.SavedAt);
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

    private sealed record OfflineCacheEntry<T>(DateTimeOffset SavedAt, T Value);
}

public sealed record OfflineCacheResult<T>(T Value, DateTimeOffset SavedAt);

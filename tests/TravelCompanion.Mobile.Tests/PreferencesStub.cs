using System.Collections.Concurrent;

public static class Preferences
{
    public static PreferenceStore Default { get; } = new();

    public sealed class PreferenceStore
    {
        private readonly ConcurrentDictionary<string, object> _values = new(StringComparer.Ordinal);
        public T Get<T>(string key, T defaultValue) => _values.TryGetValue(key, out var value) ? (T)value : defaultValue;
        public void Set<T>(string key, T value) where T : notnull => _values[key] = value;
        public bool ContainsKey(string key) => _values.ContainsKey(key);
        public void Remove(string key) => _values.TryRemove(key, out _);
    }
}

public static class SecureStorage
{
    public static SecureStore Default { get; } = new();
    public sealed class SecureStore
    {
        private readonly ConcurrentDictionary<string, string> _values = new();
        public Task<string?> GetAsync(string key) => Task.FromResult(_values.GetValueOrDefault(key));
        public Task SetAsync(string key, string value) { _values[key] = value; return Task.CompletedTask; }
        public bool Remove(string key) => _values.TryRemove(key, out _);
    }
}

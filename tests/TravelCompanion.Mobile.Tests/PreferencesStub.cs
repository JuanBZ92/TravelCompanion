using System.Collections.Concurrent;

public static class Preferences
{
    public static PreferenceStore Default { get; } = new();

    public sealed class PreferenceStore
    {
        private readonly ConcurrentDictionary<string, double> _values = new(StringComparer.Ordinal);
        public double Get(string key, double defaultValue) => _values.GetValueOrDefault(key, defaultValue);
        public void Set(string key, double value) => _values[key] = value;
    }
}

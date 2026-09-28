namespace TravelCompanion.Mobile.Services;

// UI-thread only. Collapse native collection rebuilds without losing nested updates.
internal sealed class MapUpdateBatch(Action<string> update)
{
    private int depth;
    private readonly HashSet<string> pending = [];
    public IDisposable Begin()
    {
        depth++;
        return new Scope(this);
    }
    public bool Defer(string property)
    {
        if (depth == 0) return false;
        pending.Add(property);
        return true;
    }
    private void End()
    {
        if (--depth != 0) return;
        var changes = pending.ToArray();
        pending.Clear();
        foreach (var property in changes) update(property);
    }
    private sealed class Scope(MapUpdateBatch owner) : IDisposable
    {
        private MapUpdateBatch? current = owner;
        public void Dispose() => Interlocked.Exchange(ref current, null)?.End();
    }
}

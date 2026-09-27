namespace TravelCompanion.Mobile.Services;

/// <summary>History of root tabs; page and modal stacks remain owned by Shell.</summary>
public sealed class NavigationTrail
{
    private readonly List<string> _routes = [];
    public void Visit(string route)
    {
        if (_routes.LastOrDefault() == route) return;
        _routes.Add(route);
        if (_routes.Count > 32) _routes.RemoveAt(0);
    }
    public string? Previous => _routes.Count > 1 ? _routes[^2] : null;
    public void Pop() { if (_routes.Count > 1) _routes.RemoveAt(_routes.Count - 1); }
    public void Clear() => _routes.Clear();
}

public sealed class ExitBackPressGuard
{
    private DateTimeOffset? _previous;
    public bool ShouldExit(DateTimeOffset now)
    {
        var exit = _previous.HasValue && now >= _previous.Value
            && now - _previous.Value <= TimeSpan.FromSeconds(2);
        _previous = exit ? null : now;
        return exit;
    }
    public void Reset() => _previous = null;
}

using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Mobile.Services;

public sealed record PreparationCategoryState(
    string Key,
    PreparationManualState ManualState,
    DateTimeOffset? ManualChangedAtUtc);

public sealed record PreparationOrganizerState(bool LegacyImported, IReadOnlyList<PreparationCategoryState> Categories);

public sealed class TripPreparationOrganizerStore(OfflineCacheService cache, AuthSessionService sessions)
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private (Guid User, Guid Trip) Scope() => sessions.HasSession && sessions.CurrentUserId is { } user
        && sessions.CurrentTripId is { } trip ? (user, trip) : throw new UnauthorizedAccessException();
    private static string Key((Guid User, Guid Trip) scope) => $"preparation-organizer-{scope.User:N}-{scope.Trip:N}";

    public async Task<PreparationOrganizerState> GetAsync(CancellationToken ct = default)
    {
        var scope = Scope();
        var stored = await cache.GetAsync<PreparationOrganizerState>(Key(scope), cancellationToken: ct);
        EnsureScope(scope);
        return Normalize(stored?.Value ?? new(false, []));
    }

    public async Task<PreparationOrganizerState> ImportLegacyOnceAsync(
        IEnumerable<TripPreparationItemDto> legacy,
        CancellationToken ct = default)
    {
        var scope = Scope();
        await gate.WaitAsync(ct);
        try
        {
            var stored = (await cache.GetAsync<PreparationOrganizerState>(Key(scope), cancellationToken: ct))?.Value;
            EnsureScope(scope);
            if (stored?.LegacyImported == true) return Normalize(stored);
            var now = DateTimeOffset.UtcNow;
            var completed = legacy.Where(item => item.Completed).Select(item => item.Key).ToHashSet(StringComparer.Ordinal);
            var existing = Normalize(stored ?? new(false, [])).Categories.ToDictionary(item => item.Key, StringComparer.Ordinal);
            foreach (var key in TripPreparationKeys.All)
                if (completed.Contains(key) && existing[key].ManualState == PreparationManualState.Pending)
                    existing[key] = new(key, PreparationManualState.OutsideApp, now);
            var result = new PreparationOrganizerState(true, TripPreparationKeys.All.Select(key => existing[key]).ToList());
            await cache.SaveAsync(Key(scope), result, ct);
            EnsureScope(scope);
            return result;
        }
        finally { gate.Release(); }
    }

    public async Task SetManualStateAsync(string key, PreparationManualState state, CancellationToken ct = default)
    {
        if (!TripPreparationKeys.All.Contains(key)) throw new ArgumentOutOfRangeException(nameof(key));
        var scope = Scope();
        await gate.WaitAsync(ct);
        try
        {
            var current = Normalize((await cache.GetAsync<PreparationOrganizerState>(Key(scope), cancellationToken: ct))?.Value ?? new(false, []));
            EnsureScope(scope);
            var changed = current.Categories.Select(item => item.Key == key
                ? item with { ManualState = state, ManualChangedAtUtc = state == PreparationManualState.Pending ? null : DateTimeOffset.UtcNow }
                : item).ToList();
            await cache.SaveAsync(Key(scope), current with { Categories = changed }, ct);
            EnsureScope(scope);
        }
        finally { gate.Release(); }
    }

    private static PreparationOrganizerState Normalize(PreparationOrganizerState value)
    {
        var saved = value.Categories.ToDictionary(item => item.Key, StringComparer.Ordinal);
        return value with { Categories = TripPreparationKeys.All.Select(key =>
            saved.GetValueOrDefault(key) ?? new(key, PreparationManualState.Pending, null)).ToList() };
    }

    private void EnsureScope((Guid User, Guid Trip) expected)
    {
        if (Scope() != expected) throw new OperationCanceledException("The active trip changed.");
    }
}

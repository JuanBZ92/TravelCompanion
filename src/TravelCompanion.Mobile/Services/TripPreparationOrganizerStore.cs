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
        await gate.WaitAsync(ct);
        try { return await ReadStateAsync(scope, ct); }
        finally { gate.Release(); }
    }

    public async Task<PreparationOrganizerState> ImportLegacyOnceAsync(
        IEnumerable<TripPreparationItemDto> legacy,
        CancellationToken ct = default)
    {
        var scope = Scope();
        await gate.WaitAsync(ct);
        try
        {
            var stored = await ReadStateAsync(scope, ct);
            if (stored.LegacyImported) return stored;
            var now = DateTimeOffset.UtcNow;
            var completed = legacy.Where(item => item.Completed).Select(item => item.Key).ToHashSet(StringComparer.Ordinal);
            var existing = stored.Categories.ToDictionary(item => item.Key, StringComparer.Ordinal);
            foreach (var key in TripPreparationKeys.All)
                if (completed.Contains(key) && existing[key].ManualState == PreparationManualState.Pending
                    && existing[key].ManualChangedAtUtc is null)
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
            var current = await ReadStateAsync(scope, ct);
            var changed = current.Categories.Select(item => item.Key == key
                ? item with { ManualState = state, ManualChangedAtUtc = DateTimeOffset.UtcNow }
                : item).ToList();
            await cache.SaveAsync(Key(scope), current with { Categories = changed }, ct);
            EnsureScope(scope);
        }
        finally { gate.Release(); }
    }

    private async Task<PreparationOrganizerState> ReadStateAsync((Guid User, Guid Trip) scope, CancellationToken ct)
    {
        var stored = await cache.GetAsync<PreparationOrganizerState>(Key(scope), cancellationToken: ct);
        ct.ThrowIfCancellationRequested();
        EnsureScope(scope);
        if (stored?.Value?.Categories is not null) return Normalize(stored.Value);

        var copies = (await cache.GetLocalizedCopiesAsync<PreparationOrganizerState>(Key(scope), ct))
            .Where(copy => copy.Value?.Categories is not null).ToList();
        ct.ThrowIfCancellationRequested();
        EnsureScope(scope);
        if (copies.Count == 0) return Normalize(new(false, []));

        // Default Pending rows created in a second language are not decisions.
        // An explicit dated Pending is a decision and can supersede an older declaration.
        var categories = TripPreparationKeys.All.Select(key => copies
            .SelectMany(copy => copy.Value.Categories.Where(item => item.Key == key)
                .Select(item => (Item: item, ChangedAt: item.ManualChangedAtUtc ?? copy.SavedAt)))
            .Where(candidate => candidate.Item.ManualChangedAtUtc is not null
                || candidate.Item.ManualState != PreparationManualState.Pending)
            .OrderByDescending(candidate => candidate.ChangedAt)
            .Select(candidate => candidate.Item)
            .FirstOrDefault() ?? new(key, PreparationManualState.Pending, null)).ToList();
        var recovered = new PreparationOrganizerState(copies.Any(copy => copy.Value.LegacyImported), categories);
        await cache.SaveAsync(Key(scope), recovered, ct);
        ct.ThrowIfCancellationRequested();
        EnsureScope(scope);
        return recovered;
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

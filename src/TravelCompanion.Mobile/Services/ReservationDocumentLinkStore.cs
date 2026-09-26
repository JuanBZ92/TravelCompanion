namespace TravelCompanion.Mobile.Services;

public sealed record ReservationDocumentLink(Guid ReservationId, Guid? LocalDocumentId, string? CuratedUrl, string Title);

// Links are metadata only; document bytes stay in TripDocumentStore.
public sealed class ReservationDocumentLinkStore(OfflineCacheService cache, AuthSessionService sessions)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private (Guid User, Guid Trip) Scope() => sessions.HasSession && sessions.CurrentUserId is { } user
        && sessions.CurrentTripId is { } trip ? (user, trip) : throw new UnauthorizedAccessException();
    private static string Key((Guid User, Guid Trip) scope) => $"document-links-{scope.User:N}-{scope.Trip:N}";

    public async Task<ReservationDocumentLink?> GetAsync(Guid reservationId, CancellationToken ct = default)
    {
        var scope = Scope();
        var links = (await cache.GetAsync<List<ReservationDocumentLink>>(Key(scope), cancellationToken: ct))?.Value ?? [];
        if (Scope() != scope) throw new OperationCanceledException("The active trip changed.");
        return links.FirstOrDefault(link => link.ReservationId == reservationId);
    }

    public async Task SetAsync(ReservationDocumentLink link, CancellationToken ct = default)
    {
        if (link.ReservationId == Guid.Empty || (link.LocalDocumentId.HasValue == (link.CuratedUrl is not null)))
            throw new ArgumentException("Choose one document source.");
        var scope = Scope();
        await _gate.WaitAsync(ct);
        try
        {
            var links = (await cache.GetAsync<List<ReservationDocumentLink>>(Key(scope), cancellationToken: ct))?.Value ?? [];
            if (Scope() != scope) throw new OperationCanceledException("The active trip changed.");
            await cache.SaveAsync(Key(scope), links.Where(item => item.ReservationId != link.ReservationId).Append(link).ToList(), ct);
        }
        finally { _gate.Release(); }
    }

    public async Task RemoveDocumentAsync(Guid documentId, CancellationToken ct = default)
    {
        var scope = Scope();
        await _gate.WaitAsync(ct);
        try
        {
            var links = (await cache.GetAsync<List<ReservationDocumentLink>>(Key(scope), cancellationToken: ct))?.Value ?? [];
            if (Scope() != scope) throw new OperationCanceledException("The active trip changed.");
            await cache.SaveAsync(Key(scope), links.Where(item => item.LocalDocumentId != documentId).ToList(), ct);
        }
        finally { _gate.Release(); }
    }

    public async Task RemoveReservationAsync(Guid reservationId, CancellationToken ct = default)
    {
        var scope = Scope();
        await _gate.WaitAsync(ct);
        try
        {
            var links = (await cache.GetAsync<List<ReservationDocumentLink>>(Key(scope), cancellationToken: ct))?.Value ?? [];
            if (Scope() != scope) throw new OperationCanceledException("The active trip changed.");
            await cache.SaveAsync(Key(scope), links.Where(item => item.ReservationId != reservationId).ToList(), ct);
        }
        finally { _gate.Release(); }
    }
}

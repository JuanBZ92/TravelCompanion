using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Mobile.Services;

public sealed class OfflineTripPreparationService(AuthSessionService sessions, MobileBootstrapStore bootstrap,
    TravelCompanionApiClient api, OfflineCacheService cache, TripDocumentStore documents, ProductAnalyticsTracker analytics,
    MobileSyncStateStore syncState)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private static string Key(Guid user, Guid trip) => $"offline-preparation-{user:N}-{trip:N}";

    public async Task<OfflineTripManifest?> GetAsync(CancellationToken ct = default)
    {
        if (!sessions.HasSession || sessions.CurrentUserId is not { } user || sessions.CurrentTripId is not { } trip) return null;
        var manifest = (await cache.GetAsync<OfflineTripManifest>(Key(user, trip), cancellationToken: ct))?.Value;
        if (manifest is null) return null;
        EnsureScope(user, trip);
        var resources = new List<OfflineTripResource>();
        foreach (var item in manifest.Resources)
            resources.Add(item.State == "available" && item.SourceUrl is not null && !await documents.IsDownloadedAsync(item.SourceUrl, ct)
                ? item with { State = "pending" } : item);
        EnsureScope(user, trip);
        return manifest with { Resources = resources };
    }

    public async Task<OfflineTripManifest> PrepareAsync(CancellationToken ct = default)
    {
        if (!sessions.HasKnownValidAccess || sessions.IsFreeMapPreview || sessions.CurrentUserId is not { } user
            || sessions.CurrentTripId is not { } trip) throw new UnauthorizedAccessException();
        await _gate.WaitAsync(ct);
        try
        {
            EnsureScope(user, trip);
            var previous = await GetAsync(ct);
            await cache.SaveAsync(Key(user, trip), previous is null
                ? new OfflineTripManifest(trip, 0, null, [], true) : previous with { Interrupted = true }, ct);
            var token = await sessions.GetTokenAsync() ?? throw new UnauthorizedAccessException();
            var versions = (await syncState.CheckAsync(force: true, cancellationToken: ct))?.Current;
            EnsureScope(user, trip);
            var result = await bootstrap.RefreshResultAsync(token, cancellationToken: ct);
            if (!result.IsSuccess || result.Value?.Schedule is not { } schedule || schedule.TripId != trip)
                throw new IOException("Could not download the complete itinerary.");
            var resources = new List<OfflineTripResource>
            {
                new("itinerary", "itinerary", "available"), new("places", "places", "available")
            };
            if (sessions.HasCuratedDocs)
            {
                var docsResult = await api.GetTravelDocsResultAsync(token, ct);
                if (!docsResult.IsSuccess || docsResult.Value is not { } docs || docs.TripId != trip)
                    resources.Add(new("documents", "documents", "pending"));
                else
                {
                    EnsureScope(user, trip);
                    // Same key used by DocsViewModel; metadata and actual files are separate resources.
                    var metadata = await syncState.CreateCacheMetadataAsync("documents", "downloaded", cancellationToken: ct);
                    await cache.SaveAsync($"mobile-docs-{trip}-{user}", docs, metadata, ct);
                    foreach (var document in docs.HotelDocuments.Concat(docs.OtherDocuments))
                    {
                        var state = "available";
                        try
                        {
                            EnsureScope(user, trip);
                            await documents.DownloadAsync(document.FileUrl, document.Title, ct);
                        }
                        catch (InvalidDataException) when (!HasFileExtension(document.FileUrl)) { state = "external"; }
                        catch (Exception exception) when (exception is HttpRequestException or IOException) { state = "pending"; }
                        resources.Add(new(document.Id.ToString("N"), document.Title, state, document.FileUrl));
                    }
                }
            }
            EnsureScope(user, trip);
            var manifest = new OfflineTripManifest(trip, schedule.Revision, DateTimeOffset.UtcNow, resources,
                CatalogVersion: versions?.CatalogVersion, DocumentsVersion: sessions.HasCuratedDocs ? versions?.DocumentsVersion : null);
            await cache.SaveAsync(Key(user, trip), manifest, ct);
            await analytics.TrackAsync(manifest.IsComplete ? "offline_download_completed" : "offline_download_failed", "itinerary", tripId: trip, cancellationToken: ct);
            return manifest;
        }
        catch
        {
            if (sessions.CurrentUserId == user && sessions.CurrentTripId == trip)
                await analytics.TrackAsync("offline_download_failed", "itinerary", tripId: trip);
            throw;
        }
        finally { _gate.Release(); }
    }

    private static bool HasFileExtension(string url) => Uri.TryCreate(url, UriKind.RelativeOrAbsolute, out var uri)
        && new[] { ".pdf", ".jpg", ".jpeg", ".png" }.Contains(Path.GetExtension(uri.IsAbsoluteUri ? uri.AbsolutePath : url).ToLowerInvariant());
    private void EnsureScope(Guid user, Guid trip)
    {
        if (!sessions.HasSession || sessions.CurrentUserId != user || sessions.CurrentTripId != trip)
            throw new OperationCanceledException("The active trip changed.");
    }
}

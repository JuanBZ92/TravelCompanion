using Microsoft.Extensions.Logging;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Mobile.Services;

public sealed class MobileSyncStateStore(
    TravelCompanionApiClient apiClient,
    AuthSessionService sessionService,
    OfflineCacheService offlineCacheService,
    ILogger<MobileSyncStateStore> logger)
{
    private static readonly TimeSpan BackgroundCheckInterval = TimeSpan.FromMinutes(15);
    private readonly object _checkLock = new();
    private Task<MobileSyncComparison?>? _checkTask;
    private string? _checkKey;
    private bool _checkForce;

    public async Task<MobileSyncComparison?> CheckAsync(
        bool force = false,
        CancellationToken cancellationToken = default)
    {
        var context = CaptureContext();
        Task<MobileSyncComparison?> checkTask;
        var forceFollowUp = false;
        lock (_checkLock)
        {
            if (_checkTask is null || _checkTask.IsCompleted || !string.Equals(_checkKey, context.Key, StringComparison.Ordinal))
            {
                _checkKey = context.Key;
                _checkForce = force;
                // This request belongs to the session, not to the first page that requested it.
                _checkTask = CheckCoreAsync(context, force, CancellationToken.None);
            }
            else
            {
                logger.LogInformation("Mobile sync-state check joined an equivalent in-flight request.");
                forceFollowUp = force && !_checkForce;
            }

            checkTask = _checkTask;
        }

        MobileSyncComparison? comparison;
        try
        {
            comparison = await checkTask.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            if (checkTask.IsCompleted)
            {
                lock (_checkLock)
                {
                    if (ReferenceEquals(_checkTask, checkTask))
                    {
                        _checkTask = null;
                        _checkKey = null;
                        _checkForce = false;
                    }
                }
            }
        }

        return forceFollowUp
            ? await CheckAsync(force: true, cancellationToken).ConfigureAwait(false)
            : comparison;
    }

    public async Task AcknowledgeItineraryVersionAsync(
        int revision,
        CancellationToken cancellationToken = default)
    {
        var context = CaptureContext();
        var cached = await offlineCacheService
            .GetAsync<MobileSyncCacheEntry>(context.CacheKey, cancellationToken)
            .ConfigureAwait(false);
        if (cached is null || !IsCurrent(context)) return;

        var updated = cached.Value with
        {
            State = cached.Value.State with { ItineraryVersion = revision },
            ETag = null,
            CheckedAtUtc = DateTimeOffset.UtcNow
        };
        await offlineCacheService.SaveAsync(
            context.CacheKey,
            updated,
            CreateSyncMetadata(context, updated.State),
            cancellationToken).ConfigureAwait(false);
    }

    public Task ClearAsync() => offlineCacheService.DeleteAsync(GetCacheKey());

    public async Task<OfflineCacheMetadata> CreateCacheMetadataAsync(
        string dataScope,
        string fallbackDataVersion,
        Guid? destinationId = null,
        string? destinationSlug = null,
        CancellationToken cancellationToken = default)
    {
        var context = CaptureContext();
        var cached = await offlineCacheService
            .GetAsync<MobileSyncCacheEntry>(context.CacheKey, cancellationToken)
            .ConfigureAwait(false);
        var state = cached?.Value.State;
        return new OfflineCacheMetadata(
            context.UserId,
            state?.TripId ?? context.TripId,
            state?.DestinationId ?? destinationId,
            state?.DestinationSlug ?? destinationSlug,
            System.Globalization.CultureInfo.CurrentUICulture.Name,
            dataScope,
            ResolveDataVersion(dataScope, state) ?? fallbackDataVersion);
    }

    private async Task<MobileSyncComparison?> CheckCoreAsync(
        SyncContext context,
        bool force,
        CancellationToken cancellationToken)
    {
        var cached = await offlineCacheService
            .GetAsync<MobileSyncCacheEntry>(context.CacheKey, cancellationToken)
            .ConfigureAwait(false);
        if (!force && cached is not null && DateTimeOffset.UtcNow - cached.Value.CheckedAtUtc < BackgroundCheckInterval)
        {
            return new MobileSyncComparison(cached.Value.State, cached.Value.State, false, false, false, false, false, false);
        }

        var token = await sessionService.GetTokenAsync().ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(token) || !IsCurrent(context)) return null;
        var result = await apiClient
            .GetMobileSyncStateAsync(token, cached?.Value.ETag, cancellationToken)
            .ConfigureAwait(false);
        if (!IsCurrent(context)) return null;
        if (result.Status != ApiCallStatus.Success)
        {
            logger.LogInformation("Sync-state check kept cached content. Status={Status}.", result.Status);
            return cached is null
                ? null
                : new MobileSyncComparison(cached.Value.State, cached.Value.State, false, false, false, false, false, false);
        }

        if (result.NotModified && cached is not null)
        {
            var current = cached.Value with
            {
                CheckedAtUtc = DateTimeOffset.UtcNow,
                ETag = result.ETag ?? cached.Value.ETag
            };
            await offlineCacheService.SaveAsync(
                context.CacheKey,
                current,
                CreateSyncMetadata(context, current.State),
                cancellationToken).ConfigureAwait(false);
            return new MobileSyncComparison(current.State, current.State, false, false, false, false, false, false);
        }

        if (result.State is not { } state || !IsCurrent(context)) return null;
        sessionService.ApplySyncState(state);
        var previous = cached?.Value.State;
        var entry = new MobileSyncCacheEntry(state, result.ETag, DateTimeOffset.UtcNow);
        await offlineCacheService.SaveAsync(
            context.CacheKey,
            entry,
            CreateSyncMetadata(context, state),
            cancellationToken).ConfigureAwait(false);
        return new MobileSyncComparison(
            previous,
            state,
            previous is not null && previous.CatalogVersion != state.CatalogVersion,
            previous is not null && previous.ItineraryVersion != state.ItineraryVersion,
            previous is not null && previous.DocumentsVersion != state.DocumentsVersion,
            previous is not null && previous.TodayPersonalizationVersion != state.TodayPersonalizationVersion,
            previous is not null && previous.FreeCatalogVersion != state.FreeCatalogVersion,
            previous is null);
    }

    private SyncContext CaptureContext()
    {
        var userId = sessionService.CurrentUserId;
        var tripId = sessionService.CurrentTripId;
        var contextVersion = sessionService.ContextVersion;
        var cacheKey = GetCacheKey(userId, tripId);
        return new SyncContext(userId, tripId, contextVersion, cacheKey, $"{contextVersion}:{userId}:{tripId}");
    }

    private bool IsCurrent(SyncContext context) =>
        context.ContextVersion == sessionService.ContextVersion
        && context.UserId == sessionService.CurrentUserId
        && context.TripId == sessionService.CurrentTripId;

    private string GetCacheKey() => GetCacheKey(sessionService.CurrentUserId, sessionService.CurrentTripId);

    private static string GetCacheKey(Guid? userId, Guid? tripId) =>
        $"mobile-sync-state-v1-{userId?.ToString() ?? "anonymous"}-{tripId?.ToString() ?? "auto"}";

    private static OfflineCacheMetadata CreateSyncMetadata(SyncContext context, MobileSyncStateDto state) => new(
        context.UserId,
        state.TripId ?? context.TripId,
        state.DestinationId,
        state.DestinationSlug,
        System.Globalization.CultureInfo.CurrentUICulture.Name,
        "sync-state",
        $"catalog:{state.CatalogVersion};itinerary:{state.ItineraryVersion};documents:{state.DocumentsVersion};today:{state.TodayPersonalizationVersion};free:{state.FreeCatalogVersion}");

    private static string? ResolveDataVersion(string dataScope, MobileSyncStateDto? state)
    {
        if (state is null) return null;
        return dataScope switch
        {
            "bootstrap" => $"catalog:{state.CatalogVersion};itinerary:{state.ItineraryVersion}",
            "catalog" => state.CatalogVersion.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "itinerary" => state.ItineraryVersion.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "documents" => state.DocumentsVersion.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "today" => $"catalog:{state.CatalogVersion};itinerary:{state.ItineraryVersion};personalization:{state.TodayPersonalizationVersion}",
            "free-catalog" => state.FreeCatalogVersion.ToString(System.Globalization.CultureInfo.InvariantCulture),
            _ => null
        };
    }

    private sealed record SyncContext(Guid? UserId, Guid? TripId, long ContextVersion, string CacheKey, string Key);
}

public sealed record MobileSyncCacheEntry(MobileSyncStateDto State, string? ETag, DateTimeOffset CheckedAtUtc);

public sealed record MobileSyncComparison(
    MobileSyncStateDto? Previous,
    MobileSyncStateDto Current,
    bool CatalogChanged,
    bool ItineraryChanged,
    bool DocumentsChanged,
    bool TodayChanged,
    bool FreeCatalogChanged,
    bool RequiresScopeRecovery);

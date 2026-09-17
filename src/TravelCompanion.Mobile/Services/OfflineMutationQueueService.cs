using System.Net.Http;
using Microsoft.Extensions.Logging;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Mobile.Services;

public sealed class OfflineMutationQueueService(
    OfflineCacheService offlineCacheService,
    TravelCompanionApiClient apiClient,
    MobileBootstrapStore bootstrapStore,
    AuthSessionService sessionService,
    ILogger<OfflineMutationQueueService> logger)
{
    private const string QueueCacheKey = "offline-mutation-queue-v1";
    private const string SaveItineraryItemKind = "save_itinerary_item";
    private readonly SemaphoreSlim _queueLock = new(1, 1);

    public async Task<int> GetPendingCountAsync(CancellationToken cancellationToken = default)
    {
        var queue = await ReadQueueAsync(cancellationToken).ConfigureAwait(false);
        return queue.Items.Count(IsForCurrentSession);
    }

    public async Task ClearAsync()
    {
        await _queueLock.WaitAsync().ConfigureAwait(false);
        try
        {
            await offlineCacheService.DeleteAsync(QueueCacheKey).ConfigureAwait(false);
        }
        finally
        {
            _queueLock.Release();
        }
    }

    public async Task<Guid> EnqueueSaveItineraryItemAsync(
        SaveItineraryItemRequest request,
        CancellationToken cancellationToken = default)
    {
        await _queueLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var queue = await ReadQueueAsync(cancellationToken).ConfigureAwait(false);
            var localId = request.ClientMutationId ?? Guid.NewGuid();
            request = request with { ClientMutationId = localId };
            var existing = queue.Items.FirstOrDefault(item =>
                IsForCurrentSession(item)
                &&
                item.Kind == SaveItineraryItemKind
                && item.SaveItineraryItem is { } queuedRequest
                && (queuedRequest.ClientMutationId == request.ClientMutationId
                    || (queuedRequest.RecommendationId == request.RecommendationId
                        && queuedRequest.Date == request.Date
                        && queuedRequest.StartsAt == request.StartsAt)));
            if (existing is not null)
            {
                logger.LogInformation(
                    "Skipped duplicate offline itinerary save mutation. LocalId={LocalId}; RecommendationId={RecommendationId}.",
                    existing.LocalId,
                    request.RecommendationId);
                return existing.LocalId;
            }

            var mutation = new OfflineMutationItem(
                localId,
                SaveItineraryItemKind,
                DateTimeOffset.UtcNow,
                0,
                null,
                null,
                request,
                sessionService.CurrentUserId,
                sessionService.CurrentTripId);
            queue = queue with
            {
                Items = queue.Items.Append(mutation).ToList()
            };
            await SaveQueueAsync(queue, cancellationToken).ConfigureAwait(false);

            logger.LogInformation(
                "Queued offline itinerary save mutation. LocalId={LocalId}; RecommendationId={RecommendationId}.",
                mutation.LocalId,
                request.RecommendationId);
            return mutation.LocalId;
        }
        finally
        {
            _queueLock.Release();
        }
    }

    public async Task<OfflineMutationReplayResult> ReplayPendingAsync(
        string token,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return new OfflineMutationReplayResult(0, 0, 0);
        }

        await _queueLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var queue = await ReadQueueAsync(cancellationToken).ConfigureAwait(false);
            if (queue.Items.Count == 0)
            {
                return new OfflineMutationReplayResult(0, 0, 0);
            }

            var remaining = new List<OfflineMutationItem>();
            var succeeded = 0;
            var failed = 0;
            var permanentFailures = 0;

            foreach (var item in queue.Items.OrderBy(item => item.CreatedAt))
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!IsForCurrentSession(item))
                {
                    remaining.Add(item);
                    continue;
                }

                if (item.FailedPermanently)
                {
                    remaining.Add(item);
                    permanentFailures++;
                    continue;
                }

                if (item.Kind != SaveItineraryItemKind || item.SaveItineraryItem is null)
                {
                    remaining.Add(item with
                    {
                        AttemptCount = item.AttemptCount + 1,
                        LastAttemptAt = DateTimeOffset.UtcNow,
                        LastError = "Unsupported offline mutation kind.",
                        FailedPermanently = true
                    });
                    failed++;
                    permanentFailures++;
                    continue;
                }

                try
                {
                    var response = await apiClient
                        .SaveItineraryItemAsync(token, item.SaveItineraryItem, cancellationToken)
                        .ConfigureAwait(false);
                    if (response?.Saved == true)
                    {
                        succeeded++;
                        if (response.Item is not null)
                        {
                            await bootstrapStore
                                .UpsertScheduleItemAsync(response.Item, cancellationToken)
                                .ConfigureAwait(false);
                        }

                        continue;
                    }

                    failed++;
                    remaining.Add(item with
                    {
                        AttemptCount = item.AttemptCount + 1,
                        LastAttemptAt = DateTimeOffset.UtcNow,
                        LastError = response?.Message ?? "The server did not confirm the save.",
                        FailedPermanently = true
                    });
                    permanentFailures++;
                }
                catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
                {
                    failed++;
                    remaining.Add(item with
                    {
                        AttemptCount = item.AttemptCount + 1,
                        LastAttemptAt = DateTimeOffset.UtcNow,
                        LastError = ex.Message
                    });
                }
            }

            await SaveQueueAsync(queue with { Items = remaining }, cancellationToken).ConfigureAwait(false);
            logger.LogInformation(
                "Offline mutation replay finished. Total={Total}; Succeeded={Succeeded}; Failed={Failed}; Remaining={Remaining}.",
                queue.Items.Count,
                succeeded,
                failed,
                remaining.Count);

            return new OfflineMutationReplayResult(queue.Items.Count(IsForCurrentSession), succeeded, failed, permanentFailures);
        }
        finally
        {
            _queueLock.Release();
        }
    }

    private async Task<OfflineMutationQueue> ReadQueueAsync(CancellationToken cancellationToken)
    {
        var cached = await offlineCacheService
            .GetAsync<OfflineMutationQueue>(QueueCacheKey, cancellationToken)
            .ConfigureAwait(false);

        return cached?.Value ?? new OfflineMutationQueue([]);
    }

    private async Task SaveQueueAsync(
        OfflineMutationQueue queue,
        CancellationToken cancellationToken)
    {
        if (queue.Items.Count == 0)
        {
            await offlineCacheService.DeleteAsync(QueueCacheKey).ConfigureAwait(false);
            return;
        }

        await offlineCacheService.SaveAsync(QueueCacheKey, queue, cancellationToken).ConfigureAwait(false);
    }

    private bool IsForCurrentSession(OfflineMutationItem item)
    {
        return item.UserId.HasValue
            && item.TripId.HasValue
            && item.UserId == sessionService.CurrentUserId
            && item.TripId == sessionService.CurrentTripId;
    }

    private sealed record OfflineMutationQueue(IReadOnlyList<OfflineMutationItem> Items);

    private sealed record OfflineMutationItem(
        Guid LocalId,
        string Kind,
        DateTimeOffset CreatedAt,
        int AttemptCount,
        DateTimeOffset? LastAttemptAt,
        string? LastError,
        SaveItineraryItemRequest? SaveItineraryItem,
        Guid? UserId = null,
        Guid? TripId = null,
        bool FailedPermanently = false);
}

public sealed record OfflineMutationReplayResult(
    int Total,
    int Succeeded,
    int Failed,
    int PermanentFailures = 0);

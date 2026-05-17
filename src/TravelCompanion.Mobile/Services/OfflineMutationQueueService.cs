using System.Net.Http;
using Microsoft.Extensions.Logging;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Mobile.Services;

public sealed class OfflineMutationQueueService(
    OfflineCacheService offlineCacheService,
    TravelCompanionApiClient apiClient,
    MobileBootstrapStore bootstrapStore,
    ILogger<OfflineMutationQueueService> logger)
{
    private const string QueueCacheKey = "offline-mutation-queue-v1";
    private const string SaveItineraryItemKind = "save_itinerary_item";
    private readonly SemaphoreSlim _queueLock = new(1, 1);

    public async Task<int> GetPendingCountAsync(CancellationToken cancellationToken = default)
    {
        var queue = await ReadQueueAsync(cancellationToken).ConfigureAwait(false);
        return queue.Items.Count;
    }

    public async Task<Guid> EnqueueSaveItineraryItemAsync(
        SaveItineraryItemRequest request,
        CancellationToken cancellationToken = default)
    {
        await _queueLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var queue = await ReadQueueAsync(cancellationToken).ConfigureAwait(false);
            var existing = queue.Items.FirstOrDefault(item =>
                item.Kind == SaveItineraryItemKind
                && item.SaveItineraryItem is { } queuedRequest
                && queuedRequest.RecommendationId == request.RecommendationId
                && queuedRequest.Date == request.Date
                && queuedRequest.StartsAt == request.StartsAt);
            if (existing is not null)
            {
                logger.LogInformation(
                    "Skipped duplicate offline itinerary save mutation. LocalId={LocalId}; RecommendationId={RecommendationId}.",
                    existing.LocalId,
                    request.RecommendationId);
                return existing.LocalId;
            }

            var mutation = new OfflineMutationItem(
                Guid.NewGuid(),
                SaveItineraryItemKind,
                DateTimeOffset.UtcNow,
                0,
                null,
                null,
                request);
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

            foreach (var item in queue.Items.OrderBy(item => item.CreatedAt))
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (item.Kind != SaveItineraryItemKind || item.SaveItineraryItem is null)
                {
                    remaining.Add(item with
                    {
                        LastAttemptAt = DateTimeOffset.UtcNow,
                        LastError = "Unsupported offline mutation kind."
                    });
                    failed++;
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
                        LastError = response?.Message ?? "The server did not confirm the save."
                    });
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

            return new OfflineMutationReplayResult(queue.Items.Count, succeeded, failed);
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

    private sealed record OfflineMutationQueue(IReadOnlyList<OfflineMutationItem> Items);

    private sealed record OfflineMutationItem(
        Guid LocalId,
        string Kind,
        DateTimeOffset CreatedAt,
        int AttemptCount,
        DateTimeOffset? LastAttemptAt,
        string? LastError,
        SaveItineraryItemRequest? SaveItineraryItem);
}

public sealed record OfflineMutationReplayResult(
    int Total,
    int Succeeded,
    int Failed);

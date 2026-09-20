using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Mobile.Services;

public sealed class ProductAnalyticsQueueService(
    TravelCompanionApiClient api,
    AuthSessionService sessions,
    OfflineCacheService cache)
{
    private const string CacheKey = "product-analytics-queue";
    private const int MaximumQueuedEvents = 200;
    private const int MaximumBatchSize = 50;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task EnqueueAndFlushAsync(
        ProductAnalyticsEventDto analyticsEvent,
        CancellationToken cancellationToken = default)
    {
        var userId = sessions.CurrentUserId;
        if (!userId.HasValue) return;

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var queued = await ReadQueueAsync(cancellationToken).ConfigureAwait(false);
            if (queued.All(item => item.Event.EventId != analyticsEvent.EventId))
            {
                queued.Add(new(userId.Value, analyticsEvent));
                if (queued.Count > MaximumQueuedEvents)
                    queued.RemoveRange(0, queued.Count - MaximumQueuedEvents);
                await cache.SaveAsync(CacheKey, queued, cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            _gate.Release();
        }

        await FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task FlushAsync(CancellationToken cancellationToken = default)
    {
        var userId = sessions.CurrentUserId;
        var token = await sessions.GetTokenAsync().ConfigureAwait(false);
        if (!userId.HasValue || string.IsNullOrWhiteSpace(token)) return;

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var queued = await ReadQueueAsync(cancellationToken).ConfigureAwait(false);
            var batch = queued.Where(item => item.UserId == userId.Value)
                .Take(MaximumBatchSize).ToList();
            if (batch.Count == 0) return;

            try
            {
                var delivered = await api.SendProductAnalyticsBatchAsync(
                    token,
                    batch.Select(item => item.Event).ToList(),
                    cancellationToken).ConfigureAwait(false);
                if (!delivered) return;
            }
            catch (HttpRequestException)
            {
                return;
            }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return;
            }

            var deliveredIds = batch.Select(item => item.Event.EventId).ToHashSet();
            queued.RemoveAll(item => item.UserId == userId.Value && deliveredIds.Contains(item.Event.EventId));
            if (queued.Count == 0) await cache.DeleteAsync(CacheKey).ConfigureAwait(false);
            else await cache.SaveAsync(CacheKey, queued, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task RemoveForUserAsync(Guid userId)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var queued = await ReadQueueAsync(CancellationToken.None).ConfigureAwait(false);
            queued.RemoveAll(item => item.UserId == userId);
            if (queued.Count == 0) await cache.DeleteAsync(CacheKey).ConfigureAwait(false);
            else await cache.SaveAsync(CacheKey, queued).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<List<QueuedProductAnalyticsEvent>> ReadQueueAsync(CancellationToken cancellationToken)
    {
        var cached = await cache.GetAsync<List<QueuedProductAnalyticsEvent>>(CacheKey, cancellationToken)
            .ConfigureAwait(false);
        return cached?.Value ?? [];
    }

    private sealed record QueuedProductAnalyticsEvent(Guid UserId, ProductAnalyticsEventDto Event);
}

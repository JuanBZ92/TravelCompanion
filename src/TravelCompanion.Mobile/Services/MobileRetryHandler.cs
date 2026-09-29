using System.Net;

namespace TravelCompanion.Mobile.Services;

public sealed class MobileRetryHandler(HttpMessageHandler innerHandler) : DelegatingHandler(innerHandler)
{
    // Injectable wait keeps retry/cancellation tests deterministic and fast.
    internal Func<TimeSpan, CancellationToken, Task> DelayAsync { get; init; } = Task.Delay;
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var canRetry = request.Method == HttpMethod.Get
            || request.Method == HttpMethod.Head
            || request.Method == HttpMethod.Options
            || request.Headers.Contains("Idempotency-Key");
        if (!canRetry)
        {
            return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }

        var snapshot = await RequestSnapshot.CreateAsync(request, cancellationToken).ConfigureAwait(false);
        for (var attempt = 0; ; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var delay = TimeSpan.FromSeconds(2 * Math.Pow(2, attempt));
            try
            {
                using var retryRequest = snapshot.CreateRequest();
                var response = await base.SendAsync(retryRequest, cancellationToken).ConfigureAwait(false);
                if (!IsTransient(response.StatusCode) || attempt == 3)
                {
                    return response;
                }

                var retryAfter = response.Headers.RetryAfter?.Delta
                    ?? (response.Headers.RetryAfter?.Date - DateTimeOffset.UtcNow);
                // Never retry sooner than the server asks. Long waits stay a manual retry.
                if (retryAfter > TimeSpan.FromSeconds(8)) return response;
                if (retryAfter > delay) delay = retryAfter.Value;
                response.Dispose();
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested && attempt < 3
                && (ex is HttpRequestException { StatusCode: null } or IOException or OperationCanceledException))
            {
                // Native transports can report "Canceled" without caller cancellation.
            }
            await DelayAsync(delay, cancellationToken).ConfigureAwait(false);
        }
    }

    private static bool IsTransient(HttpStatusCode statusCode) =>
        statusCode == HttpStatusCode.RequestTimeout
        || statusCode == HttpStatusCode.TooManyRequests
        || (int)statusCode >= 500;

    private sealed record RequestSnapshot(
        HttpMethod Method,
        Uri? RequestUri,
        Version Version,
        HttpVersionPolicy VersionPolicy,
        IReadOnlyList<KeyValuePair<string, IEnumerable<string>>> Headers,
        byte[]? Content,
        IReadOnlyList<KeyValuePair<string, IEnumerable<string>>> ContentHeaders)
    {
        public static async Task<RequestSnapshot> CreateAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var content = request.Content is null
                ? null
                : await request.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
            return new RequestSnapshot(
                request.Method,
                request.RequestUri,
                request.Version,
                request.VersionPolicy,
                request.Headers.Select(header => new KeyValuePair<string, IEnumerable<string>>(header.Key, header.Value)).ToList(),
                content,
                request.Content?.Headers.Select(header => new KeyValuePair<string, IEnumerable<string>>(header.Key, header.Value)).ToList()
                    ?? []);
        }

        public HttpRequestMessage CreateRequest()
        {
            var request = new HttpRequestMessage(Method, RequestUri)
            {
                Version = Version,
                VersionPolicy = VersionPolicy
            };
            foreach (var header in Headers)
            {
                request.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }

            if (Content is not null)
            {
                request.Content = new ByteArrayContent(Content);
                foreach (var header in ContentHeaders)
                {
                    request.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
                }
            }

            return request;
        }
    }
}

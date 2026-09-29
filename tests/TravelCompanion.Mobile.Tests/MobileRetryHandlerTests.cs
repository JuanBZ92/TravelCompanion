using System.Net;
using TravelCompanion.Mobile.Services;

namespace TravelCompanion.Mobile.Tests;

public sealed class MobileRetryHandlerTests
{
    [Theory]
    [InlineData("network")]
    [InlineData("canceled")]
    [InlineData("server")]
    public async Task Transient_failure_retries_with_bounded_backoff(string failure)
    {
        var waits = new List<double>();
        var transport = new Transport((attempt, _) => attempt == 4
            ? Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK))
            : failure switch
            {
                "network" => throw new HttpRequestException("Canceled"),
                "canceled" => throw new OperationCanceledException(),
                _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable))
            });
        using var client = Client(transport, waits);
        using var response = await client.GetAsync("https://example.test");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(new double[] { 2, 4, 8 }, waits);
        Assert.Equal(4, transport.Calls);
    }

    [Fact]
    public async Task Exhaustion_stops_after_four_attempts()
    {
        var transport = new Transport((_, _) => throw new HttpRequestException("Canceled"));
        using var client = Client(transport, []);
        await Assert.ThrowsAsync<HttpRequestException>(() => client.GetAsync("https://example.test"));
        Assert.Equal(4, transport.Calls);
    }

    [Fact]
    public async Task Cancellation_during_backoff_stops_retries()
    {
        using var cancellation = new CancellationTokenSource();
        var transport = new Transport((_, _) => throw new HttpRequestException("Canceled"));
        using var handler = new MobileRetryHandler(transport)
        {
            DelayAsync = (_, ct) => { cancellation.Cancel(); ct.ThrowIfCancellationRequested(); return Task.CompletedTask; }
        };
        using var client = new HttpClient(handler);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.GetAsync("https://example.test", cancellation.Token));
        Assert.Equal(1, transport.Calls);
    }

    [Theory]
    [InlineData("GET", 401)]
    [InlineData("GET", 400)]
    [InlineData("POST", 503)]
    public async Task Does_not_retry_permanent_errors_or_unkeyed_writes(string method, int status)
    {
        var transport = new Transport((_, _) => Task.FromResult(new HttpResponseMessage((HttpStatusCode)status)));
        using var client = Client(transport, []);
        using var request = new HttpRequestMessage(new HttpMethod(method), "https://example.test");
        using var response = await client.SendAsync(request);
        Assert.Equal(1, transport.Calls);
    }

    [Fact]
    public async Task Long_retry_after_is_not_shortened()
    {
        var transport = new Transport((_, _) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
            response.Headers.RetryAfter = new(TimeSpan.FromMinutes(1));
            return Task.FromResult(response);
        });
        using var client = Client(transport, []);
        using var response = await client.GetAsync("https://example.test");
        Assert.Equal(1, transport.Calls);
    }

    private static HttpClient Client(Transport transport, List<double> waits) => new(new MobileRetryHandler(transport)
    {
        DelayAsync = (delay, ct) => { ct.ThrowIfCancellationRequested(); waits.Add(delay.TotalSeconds); return Task.CompletedTask; }
    });
    private sealed class Transport(Func<int, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => send(++Calls, cancellationToken);
    }
}

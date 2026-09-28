using System.Net;
using Microsoft.Extensions.Caching.Memory;
using TravelCompanion.Api.Services;

namespace TravelCompanion.Api.Tests;

public sealed class ExpenseRateServiceTests
{
    [Fact]
    public async Task HistoricalResponseIsCachedAndOnlyPairAndDateAreSent()
    {
        var handler = new Handler(); using var http = new HttpClient(handler) { BaseAddress = new("https://api.frankfurter.dev/") };
        using var cache = new MemoryCache(new MemoryCacheOptions()); var service = new ExpenseRateService(http, cache);
        var first = await service.GetAsync("JPY", "EUR", new(2026, 1, 1), default);
        var second = await service.GetAsync("JPY", "EUR", new(2026, 1, 1), default);
        Assert.Equal(first, second); Assert.Equal(1, handler.Calls); Assert.Equal(.006m, first!.Rate);
        Assert.Equal("/v2/rate/JPY/EUR?date=2026-01-01", handler.Uri!.PathAndQuery);
    }
    [Fact]
    public async Task ProviderFailureLeavesConversionPendingAndIdentityNeedsNoNetwork()
    {
        var handler = new Handler { Fail = true }; using var http = new HttpClient(handler) { BaseAddress = new("https://api.frankfurter.dev/") };
        using var cache = new MemoryCache(new MemoryCacheOptions()); var service = new ExpenseRateService(http, cache);
        Assert.Null(await service.GetAsync("JPY", "EUR", new(2026, 1, 1), default));
        Assert.Equal(1m, (await service.GetAsync("JPY", "JPY", new(2026, 1, 1), default))!.Rate);
        Assert.Equal(1, handler.Calls);
    }
    private sealed class Handler : HttpMessageHandler
    {
        public int Calls; public bool Fail; public Uri? Uri;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls++; Uri = request.RequestUri;
            return Task.FromResult(new HttpResponseMessage(Fail ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK)
            { Content = new StringContent("{\"date\":\"2025-12-31\",\"base\":\"JPY\",\"quote\":\"EUR\",\"rate\":0.006}") });
        }
    }
}

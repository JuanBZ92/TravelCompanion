using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TravelCompanion.Api.Options;
using TravelCompanion.Api.Services;

namespace TravelCompanion.Api.Tests;

public sealed class GoogleRoutesServiceTests
{
    [Theory]
    [InlineData("WALK", 1)]
    [InlineData("DRIVE", 1)]
    [InlineData("TRANSIT", 1)]
    public async Task Uses_place_id_and_correct_time_parameter(string mode, int calls)
    {
        var handler = new Handler();
        var service = Create(handler, new GoogleRoutesOptions { Enabled = true, ApiKey = "test" });
        var arrival = DateTimeOffset.UtcNow.AddDays(1);
        var result = await service.EstimateAsync(new(null, 35m, 139m), new("verified-place", 35.1m, 139.1m), mode, arrival, default);
        Assert.NotNull(result);
        Assert.Equal(10, result.Minutes);
        Assert.Equal(arrival.AddMinutes(-10), result.LeaveAt);
        Assert.Equal(calls, handler.Bodies.Count);
        using var json = JsonDocument.Parse(handler.Bodies[0]);
        Assert.Equal("verified-place", json.RootElement.GetProperty("destination").GetProperty("placeId").GetString());
        Assert.Equal(mode == "TRANSIT", json.RootElement.TryGetProperty("arrivalTime", out _));
        Assert.Equal(mode == "DRIVE", json.RootElement.TryGetProperty("departureTime", out _));
    }

    [Fact]
    public async Task Disabled_routes_do_not_call_google()
    {
        var handler = new Handler();
        var service = Create(handler, new GoogleRoutesOptions());
        Assert.Null(await service.EstimateAsync(new("hotel", null, null), new("place", null, null), "WALK", DateTimeOffset.UtcNow, default));
        Assert.Empty(handler.Bodies);
    }

    private sealed class Factory(Handler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, false);
    }
    private static GoogleRoutesService Create(Handler handler, GoogleRoutesOptions options) => new(
        new Factory(handler), Microsoft.Extensions.Options.Options.Create(options),
        Microsoft.Extensions.Options.Options.Create(new GooglePlacesOptions()), NullLogger<GoogleRoutesService>.Instance);
    private sealed class Handler : HttpMessageHandler
    {
        public List<string> Bodies { get; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Bodies.Add(await request.Content!.ReadAsStringAsync(ct));
            return new(HttpStatusCode.OK) { Content = new StringContent("""{"routes":[{"duration":"600s"}]}""") };
        }
    }
}

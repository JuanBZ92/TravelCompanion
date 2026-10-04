using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using TravelCompanion.Mobile.Services;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Mobile.Tests;

public sealed class DayPlanClientTests
{
    [Fact]
    public async Task Generate_sends_stable_operation_identity_and_bearer_header()
    {
        var request = new DayPlanRequest(Guid.NewGuid(), 3, new(2026, 10, 20), 3, Guid.NewGuid(), new("efficient", "low", ["Culture"]), "en");
        var calls = 0;
        using var client = Create(async (message, ct) =>
        {
            calls++;
            Assert.Equal(HttpMethod.Post, message.Method);
            Assert.Equal("/api/ai/day-plans", message.RequestUri!.AbsolutePath);
            Assert.Equal("Bearer", message.Headers.Authorization!.Scheme);
            Assert.Equal("test-token", message.Headers.Authorization.Parameter);
            var body = await message.Content!.ReadFromJsonAsync<DayPlanRequest>(cancellationToken: ct);
            Assert.Equal(request.OperationId, body!.OperationId);
            Assert.Equal(request.TripId, body.TripId);
            Assert.Equal(request.Preferences!.Interests, body.Preferences!.Interests);
            return new(HttpStatusCode.OK) { Content = JsonContent.Create(new DayPlanResponse(request.OperationId, request.TripId, 3, [], "Proposal")) };
        });
        var result = await client.GenerateAsync("test-token", request, default);
        Assert.Equal(request.OperationId, result.OperationId);
        Assert.Equal(1, calls);
    }

    [Theory]
    [InlineData(HttpStatusCode.Conflict, "stale")]
    [InlineData(HttpStatusCode.Forbidden, "quota")]
    [InlineData(HttpStatusCode.BadRequest, "selection")]
    public async Task Structured_errors_preserve_status_and_code(HttpStatusCode status, string code)
    {
        using var client = Create((_, _) => Task.FromResult(new HttpResponseMessage(status)
        { Content = JsonContent.Create(new DayPlanErrorDto(code, "Keep your selection")) }));
        var error = await Assert.ThrowsAsync<DayPlanApiException>(() => client.ApplyAsync("token",
            new(Guid.NewGuid(), Guid.NewGuid(), 0, Guid.NewGuid(), [Guid.NewGuid()]), default));
        Assert.Equal(status, error.Status);
        Assert.Equal(code, error.Code);
        Assert.Equal("Keep your selection", error.Message);
    }

    [Fact]
    public async Task Non_json_server_failure_is_a_recoverable_unavailable_error()
    {
        using var client = Create((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
        { Content = new StringContent("Service temporarily unavailable") }));
        var error = await Assert.ThrowsAsync<DayPlanApiException>(() => client.OptionsAsync("token", default));
        Assert.Equal("unavailable", error.Code);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, error.Status);
    }

    [Fact]
    public async Task Empty_success_body_is_rejected_instead_of_erasing_a_saved_proposal()
    {
        using var client = Create((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        { Content = new StringContent("null", System.Text.Encoding.UTF8, "application/json") }));
        await Assert.ThrowsAsync<JsonException>(() => client.OptionsAsync("token", default));
    }

    [Fact]
    public async Task Cancellation_stops_the_pending_request()
    {
        using var cancel = new CancellationTokenSource();
        using var client = Create(async (_, ct) =>
        {
            cancel.Cancel();
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            throw new InvalidOperationException("Unreachable");
        });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.OptionsAsync("token", cancel.Token));
    }

    private static DayPlanClient Create(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> response) =>
        new(new HttpClient(new Handler(response)) { BaseAddress = new("https://example.invalid/") });

    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => response(request, ct);
    }
}

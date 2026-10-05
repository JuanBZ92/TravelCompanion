using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Mobile.Services;

// This bounded request has its own timeout; ordinary mobile calls retain theirs.
public sealed class DayPlanClient : IDisposable
{
    private readonly HttpClient client;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    { Converters = { new JsonStringEnumConverter() } };

    public DayPlanClient(TravelCompanionApiClient api) => client = new(
        new DiagnosticHttpHandler(new MobileRetryHandler(new HttpClientHandler())))
    { BaseAddress = api.BaseAddress, Timeout = TimeSpan.FromSeconds(45) };

    internal DayPlanClient(HttpClient client) => this.client = client;

    public Task<DayPlanOptionsDto> OptionsAsync(string token, CancellationToken ct) =>
        SendAsync<DayPlanOptionsDto>(HttpMethod.Get, "api/ai/day-plans/options", token, null, ct);
    public Task<DayPlanResponse> GenerateAsync(string token, DayPlanRequest request, CancellationToken ct) =>
        SendAsync<DayPlanResponse>(HttpMethod.Post, "api/ai/day-plans", token, request, ct);
    public Task<DayPlanApplyResponse> ApplyAsync(string token, DayPlanApplyRequest request, CancellationToken ct) =>
        SendAsync<DayPlanApplyResponse>(HttpMethod.Post, "api/ai/day-plans/apply", token, request, ct);
    public Task<DayPlanReplaceResponse> ReplaceAsync(string token, DayPlanReplaceRequest request, CancellationToken ct) =>
        SendAsync<DayPlanReplaceResponse>(HttpMethod.Post, "api/ai/day-plans/replace", token, request, ct);

    private async Task<T> SendAsync<T>(HttpMethod method, string path, string token, object? body, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var locale = System.Globalization.CultureInfo.CurrentUICulture.Name;
        if (!string.IsNullOrWhiteSpace(locale)) request.Headers.AcceptLanguage.ParseAdd(locale);
        if (body is not null) request.Content = JsonContent.Create(body, options: Json);
        using var response = await client.SendAsync(request, ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            DayPlanErrorDto? error = null;
            try { error = await response.Content.ReadFromJsonAsync<DayPlanErrorDto>(Json, ct).ConfigureAwait(false); }
            catch (JsonException) { }
            throw new DayPlanApiException(response.StatusCode, error?.Code ?? "unavailable", error?.Message);
        }
        return await response.Content.ReadFromJsonAsync<T>(Json, ct).ConfigureAwait(false)
            ?? throw new JsonException("Empty planner response.");
    }
    public void Dispose() => client.Dispose();
}

public sealed class DayPlanApiException(HttpStatusCode status, string code, string? message)
    : Exception(message)
{
    public HttpStatusCode Status { get; } = status;
    public string Code { get; } = code;
}

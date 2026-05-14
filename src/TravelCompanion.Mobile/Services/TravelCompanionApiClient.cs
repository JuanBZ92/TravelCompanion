using System.Diagnostics;
using System.Net.Http.Json;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Mobile.Services;

public sealed class TravelCompanionApiClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly HttpClient _httpClient;
    private readonly ILogger<TravelCompanionApiClient> _logger;

    public TravelCompanionApiClient(
        HttpClient httpClient,
        ILogger<TravelCompanionApiClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<IReadOnlyList<DestinationSummaryDto>> GetDestinationsAsync(CancellationToken cancellationToken = default)
    {
        return await GetPagedItemsAsync<DestinationSummaryDto>("api/destinations?pageSize=100", cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<TravelPackageDto>> GetPackagesAsync(
        string? destinationSlug = null,
        string? token = null,
        CancellationToken cancellationToken = default)
    {
        var url = BuildDestinationUrl("api/packages", destinationSlug, pageSize: 100);
        if (string.IsNullOrWhiteSpace(token))
        {
            return await GetPagedItemsAsync<TravelPackageDto>(url, cancellationToken).ConfigureAwait(false);
        }

        using var request = CreateAuthorizedRequest(HttpMethod.Get, url, token);
        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (response.IsSuccessStatusCode)
        {
            return await ReadPagedItemsAsync<TravelPackageDto>(response.Content, cancellationToken).ConfigureAwait(false);
        }

        return await GetPagedItemsAsync<TravelPackageDto>(url, cancellationToken).ConfigureAwait(false);
    }

    public async Task<AuthSessionDto?> LoginAsync(string email, string password, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.PostAsJsonAsync(
            "api/auth/login",
            new LoginRequestDto(email, password),
            JsonOptions,
            cancellationToken).ConfigureAwait(false);

        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<AuthSessionDto>(JsonOptions, cancellationToken).ConfigureAwait(false);
    }

    public async Task ChangePasswordAsync(
        string token,
        string currentPassword,
        string newPassword,
        CancellationToken cancellationToken = default)
    {
        using var request = CreateAuthorizedRequest(HttpMethod.Post, "api/auth/change-password", token);
        request.Content = JsonContent.Create(
            new ChangePasswordRequestDto(currentPassword, newPassword),
            options: JsonOptions);

        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
    }

    public async Task LogoutAsync(string token, CancellationToken cancellationToken = default)
    {
        using var request = CreateAuthorizedRequest(HttpMethod.Post, "api/auth/logout", token);
        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
    }

    public async Task<UserEntitlementsDto?> GetDemoEntitlementsAsync(CancellationToken cancellationToken = default)
    {
        return await _httpClient.GetFromJsonAsync<UserEntitlementsDto>(
            "api/users/demo/entitlements",
            JsonOptions,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<UserEntitlementsDto?> GetEntitlementsAsync(string token, CancellationToken cancellationToken = default)
    {
        using var request = CreateAuthorizedRequest(HttpMethod.Get, "api/me/entitlements", token);
        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        return response.IsSuccessStatusCode
            ? await response.Content.ReadFromJsonAsync<UserEntitlementsDto>(JsonOptions, cancellationToken).ConfigureAwait(false)
            : null;
    }

    public async Task<MobileBootstrapDto?> GetMobileBootstrapAsync(
        string token,
        string? destinationSlug = null,
        CancellationToken cancellationToken = default)
    {
        var url = string.IsNullOrWhiteSpace(destinationSlug)
            ? "api/mobile/bootstrap"
            : $"api/mobile/bootstrap?destinationSlug={Uri.EscapeDataString(destinationSlug)}";

        var stopwatch = Stopwatch.StartNew();
        using var request = CreateAuthorizedRequest(HttpMethod.Get, url, token);
        using var response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);

        var headersElapsedMs = stopwatch.Elapsed.TotalMilliseconds;
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning(
                "Mobile bootstrap request failed with {StatusCode} after {ElapsedMs}ms.",
                (int)response.StatusCode,
                headersElapsedMs);
            return null;
        }

        var bootstrap = await response.Content
            .ReadFromJsonAsync<MobileBootstrapDto>(JsonOptions, cancellationToken)
            .ConfigureAwait(false);
        stopwatch.Stop();

        _logger.LogInformation(
            "Mobile bootstrap request completed in {ElapsedMs}ms. Headers={HeadersElapsedMs}ms; BodyAndJson={BodyElapsedMs}ms; ServerTiming={ServerTiming}.",
            stopwatch.Elapsed.TotalMilliseconds,
            headersElapsedMs,
            stopwatch.Elapsed.TotalMilliseconds - headersElapsedMs,
            GetServerTiming(response));

        return bootstrap;
    }

    public async Task<MobileDiscoverDto?> GetMobileDiscoverAsync(
        string token,
        string? destinationSlug = null,
        CancellationToken cancellationToken = default)
    {
        var url = string.IsNullOrWhiteSpace(destinationSlug)
            ? "api/mobile/discover"
            : $"api/mobile/discover?destinationSlug={Uri.EscapeDataString(destinationSlug)}";

        var stopwatch = Stopwatch.StartNew();
        using var request = CreateAuthorizedRequest(HttpMethod.Get, url, token);
        using var response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);

        var headersElapsedMs = stopwatch.Elapsed.TotalMilliseconds;
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning(
                "Mobile discover request failed with {StatusCode} after {ElapsedMs}ms.",
                (int)response.StatusCode,
                headersElapsedMs);
            return null;
        }

        var discover = await response.Content
            .ReadFromJsonAsync<MobileDiscoverDto>(JsonOptions, cancellationToken)
            .ConfigureAwait(false);
        stopwatch.Stop();

        _logger.LogInformation(
            "Mobile discover request completed in {ElapsedMs}ms. Headers={HeadersElapsedMs}ms; BodyAndJson={BodyElapsedMs}ms; ServerTiming={ServerTiming}.",
            stopwatch.Elapsed.TotalMilliseconds,
            headersElapsedMs,
            stopwatch.Elapsed.TotalMilliseconds - headersElapsedMs,
            GetServerTiming(response));

        return discover;
    }

    public async Task<IReadOnlyList<RecommendationDto>> GetRecommendationsAsync(
        string? destinationSlug = null,
        decimal? latitude = null,
        decimal? longitude = null,
        CancellationToken cancellationToken = default)
    {
        var url = BuildDestinationUrl("api/recommendations", destinationSlug, pageSize: 100);
        if (latitude.HasValue && longitude.HasValue)
        {
            url += $"&latitude={latitude.Value}&longitude={longitude.Value}";
        }

        return await GetPagedItemsAsync<RecommendationDto>(url, cancellationToken).ConfigureAwait(false);
    }

    public async Task<TripScheduleDto?> GetDemoScheduleAsync(CancellationToken cancellationToken = default)
    {
        return await _httpClient.GetFromJsonAsync<TripScheduleDto>(
            "api/trips/44444444-4444-4444-4444-444444444401/schedule",
            JsonOptions,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<TripScheduleDto?> GetScheduleAsync(string token, CancellationToken cancellationToken = default)
    {
        using var request = CreateAuthorizedRequest(HttpMethod.Get, "api/me/schedule", token);
        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        return response.IsSuccessStatusCode
            ? await response.Content.ReadFromJsonAsync<TripScheduleDto>(JsonOptions, cancellationToken).ConfigureAwait(false)
            : null;
    }

    private static HttpRequestMessage CreateAuthorizedRequest(HttpMethod method, string url, string token)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
    }

    private async Task<IReadOnlyList<T>> GetPagedItemsAsync<T>(
        string url,
        CancellationToken cancellationToken = default)
    {
        var result = await _httpClient.GetFromJsonAsync<PagedResultDto<T>>(
            url,
            JsonOptions,
            cancellationToken).ConfigureAwait(false);

        return result?.Items ?? [];
    }

    private static async Task<IReadOnlyList<T>> ReadPagedItemsAsync<T>(
        HttpContent content,
        CancellationToken cancellationToken = default)
    {
        var result = await content.ReadFromJsonAsync<PagedResultDto<T>>(
            JsonOptions,
            cancellationToken).ConfigureAwait(false);

        return result?.Items ?? [];
    }

    private static string BuildDestinationUrl(string basePath, string? destinationSlug, int pageSize)
    {
        var url = $"{basePath}?pageSize={pageSize}";
        if (!string.IsNullOrWhiteSpace(destinationSlug))
        {
            url += $"&destinationSlug={Uri.EscapeDataString(destinationSlug)}";
        }

        return url;
    }

    private static string GetServerTiming(HttpResponseMessage response)
    {
        return response.Headers.TryGetValues("Server-Timing", out var values)
            ? string.Join(" | ", values)
            : "none";
    }
}

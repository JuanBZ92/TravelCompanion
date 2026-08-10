using System.Diagnostics;
using System.Globalization;
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

    public Uri? BaseAddress => _httpClient.BaseAddress;

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

    public async Task<AuthSessionDto?> LoginWithPinAsync(string pin, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.PostAsJsonAsync(
            "api/auth/pin-login",
            new PinLoginRequestDto(pin),
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

        return MobilePayloadNormalizer.Normalize(bootstrap);
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

        return MobilePayloadNormalizer.Normalize(discover);
    }

    public async Task<TodayDto?> GetMobileTodayAsync(
        string token,
        DateOnly? date = null,
        GeoPointDto? currentLocation = null,
        CancellationToken cancellationToken = default)
    {
        var query = new List<string>();
        if (date.HasValue)
        {
            query.Add($"date={Uri.EscapeDataString(date.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))}");
        }

        if (currentLocation is not null)
        {
            query.Add($"latitude={currentLocation.Latitude.ToString(CultureInfo.InvariantCulture)}");
            query.Add($"longitude={currentLocation.Longitude.ToString(CultureInfo.InvariantCulture)}");
        }

        var url = query.Count == 0
            ? "api/mobile/today"
            : $"api/mobile/today?{string.Join('&', query)}";

        using var request = CreateAuthorizedRequest(HttpMethod.Get, url, token);
        using var response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning(
                "Mobile today request failed with {StatusCode}.",
                (int)response.StatusCode);
            return null;
        }

        var today = await response.Content
            .ReadFromJsonAsync<TodayDto>(JsonOptions, cancellationToken)
            .ConfigureAwait(false);

        return MobilePayloadNormalizer.Normalize(today);
    }

    public async Task<TravelDocsDto?> GetTravelDocsAsync(
        string token,
        CancellationToken cancellationToken = default)
    {
        using var request = CreateAuthorizedRequest(HttpMethod.Get, "api/mobile/docs", token);
        using var response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning(
                "Travel docs request failed with {StatusCode}.",
                (int)response.StatusCode);
            return null;
        }

        return await response.Content
            .ReadFromJsonAsync<TravelDocsDto>(JsonOptions, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<TravelChatResponse?> SendTravelChatAsync(
        string token,
        TravelChatRequest request,
        CancellationToken cancellationToken = default)
    {
        using var httpRequest = CreateAuthorizedRequest(HttpMethod.Post, "api/ai/travel-chat", token);
        httpRequest.Content = JsonContent.Create(request, options: JsonOptions);

        using var response = await _httpClient.SendAsync(httpRequest, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning(
                "Travel chat request failed with {StatusCode}.",
                (int)response.StatusCode);
            return null;
        }

        var travelChatResponse = await response.Content
            .ReadFromJsonAsync<TravelChatResponse>(JsonOptions, cancellationToken)
            .ConfigureAwait(false);

        return MobilePayloadNormalizer.Normalize(travelChatResponse);
    }

    public async Task<TravelPreferenceProfileDto?> GetTravelPreferenceProfileAsync(
        string token,
        CancellationToken cancellationToken = default)
    {
        using var request = CreateAuthorizedRequest(HttpMethod.Get, "api/me/travel-preference-profile", token);
        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        return response.IsSuccessStatusCode
            ? await response.Content.ReadFromJsonAsync<TravelPreferenceProfileDto>(JsonOptions, cancellationToken).ConfigureAwait(false)
            : null;
    }

    public async Task<TravelPreferenceProfileDto?> PatchTravelPreferenceProfileAsync(
        string token,
        TravelPreferenceProfilePatchDto patch,
        CancellationToken cancellationToken = default)
    {
        using var request = CreateAuthorizedRequest(HttpMethod.Patch, "api/me/travel-preference-profile", token);
        request.Content = JsonContent.Create(patch, options: JsonOptions);

        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        return response.IsSuccessStatusCode
            ? await response.Content.ReadFromJsonAsync<TravelPreferenceProfileDto>(JsonOptions, cancellationToken).ConfigureAwait(false)
            : null;
    }

    public async Task<SaveItineraryItemResponse?> SaveItineraryItemAsync(
        string token,
        SaveItineraryItemRequest saveRequest,
        CancellationToken cancellationToken = default)
    {
        using var request = CreateAuthorizedRequest(HttpMethod.Post, "api/ai/save_itinerary_item", token);
        request.Content = JsonContent.Create(saveRequest, options: JsonOptions);

        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var payload = await response.Content
            .ReadFromJsonAsync<SaveItineraryItemResponse>(JsonOptions, cancellationToken)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning(
                "Save itinerary item request failed with {StatusCode}.",
                (int)response.StatusCode);
        }

        return payload;
    }

    public async Task<TravelAssistantFeedbackResponse?> SendTravelAssistantFeedbackAsync(
        string token,
        TravelAssistantFeedbackRequest feedbackRequest,
        CancellationToken cancellationToken = default)
    {
        using var request = CreateAuthorizedRequest(HttpMethod.Post, "api/ai/feedback", token);
        request.Content = JsonContent.Create(feedbackRequest, options: JsonOptions);

        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning(
                "Travel assistant feedback request failed with {StatusCode}.",
                (int)response.StatusCode);
            return null;
        }

        return await response.Content
            .ReadFromJsonAsync<TravelAssistantFeedbackResponse>(JsonOptions, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<RecommendationSignalResponse?> SendRecommendationSignalAsync(
        string token,
        Guid recommendationId,
        RecommendationSignalRequest signalRequest,
        CancellationToken cancellationToken = default)
    {
        using var request = CreateAuthorizedRequest(
            HttpMethod.Post,
            $"api/mobile/recommendations/{recommendationId}/signals",
            token);
        request.Content = JsonContent.Create(signalRequest, options: JsonOptions);

        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning(
                "Recommendation signal request failed with {StatusCode}. RecommendationId={RecommendationId}; Signal={Signal}.",
                (int)response.StatusCode,
                recommendationId,
                signalRequest.Signal);
            return null;
        }

        return await response.Content
            .ReadFromJsonAsync<RecommendationSignalResponse>(JsonOptions, cancellationToken)
            .ConfigureAwait(false);
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

    public async Task<RecommendationDto?> GetMobileRecommendationDetailAsync(
        string token,
        Guid recommendationId,
        CancellationToken cancellationToken = default)
    {
        using var request = CreateAuthorizedRequest(
            HttpMethod.Get,
            $"api/mobile/recommendations/{recommendationId}",
            token);

        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning(
                "Mobile recommendation detail request failed with {StatusCode}. RecommendationId={RecommendationId}.",
                (int)response.StatusCode,
                recommendationId);
            return null;
        }

        return await response.Content
            .ReadFromJsonAsync<RecommendationDto>(JsonOptions, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<TripScheduleDto?> GetDemoScheduleAsync(CancellationToken cancellationToken = default)
    {
        var schedule = await _httpClient.GetFromJsonAsync<TripScheduleDto>(
            "api/trips/44444444-4444-4444-4444-444444444401/schedule",
            JsonOptions,
            cancellationToken).ConfigureAwait(false);

        return MobilePayloadNormalizer.Normalize(schedule);
    }

    public async Task<TripScheduleDto?> GetScheduleAsync(string token, CancellationToken cancellationToken = default)
    {
        using var request = CreateAuthorizedRequest(HttpMethod.Get, "api/me/schedule", token);
        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        var schedule = await response.Content
            .ReadFromJsonAsync<TripScheduleDto>(JsonOptions, cancellationToken)
            .ConfigureAwait(false);

        return MobilePayloadNormalizer.Normalize(schedule);
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

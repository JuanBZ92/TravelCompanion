using System.Net.Http.Json;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Mobile.Services;

public sealed class TravelCompanionApiClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly HttpClient _httpClient;

    public TravelCompanionApiClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<IReadOnlyList<DestinationSummaryDto>> GetDestinationsAsync(CancellationToken cancellationToken = default)
    {
        return await GetPagedItemsAsync<DestinationSummaryDto>("api/destinations?pageSize=100", cancellationToken);
    }

    public async Task<IReadOnlyList<TravelPackageDto>> GetPackagesAsync(
        string? destinationSlug = null,
        string? token = null,
        CancellationToken cancellationToken = default)
    {
        var url = BuildDestinationUrl("api/packages", destinationSlug, pageSize: 100);
        if (string.IsNullOrWhiteSpace(token))
        {
            return await GetPagedItemsAsync<TravelPackageDto>(url, cancellationToken);
        }

        using var request = CreateAuthorizedRequest(HttpMethod.Get, url, token);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (response.IsSuccessStatusCode)
        {
            return await ReadPagedItemsAsync<TravelPackageDto>(response.Content, cancellationToken);
        }

        return await GetPagedItemsAsync<TravelPackageDto>(url, cancellationToken);
    }

    public async Task<AuthSessionDto?> LoginAsync(string email, string password, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.PostAsJsonAsync(
            "api/auth/login",
            new LoginRequestDto(email, password),
            JsonOptions,
            cancellationToken);

        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<AuthSessionDto>(JsonOptions, cancellationToken);
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

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public async Task LogoutAsync(string token, CancellationToken cancellationToken = default)
    {
        using var request = CreateAuthorizedRequest(HttpMethod.Post, "api/auth/logout", token);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public async Task<UserEntitlementsDto?> GetDemoEntitlementsAsync(CancellationToken cancellationToken = default)
    {
        return await _httpClient.GetFromJsonAsync<UserEntitlementsDto>(
            "api/users/demo/entitlements",
            JsonOptions,
            cancellationToken);
    }

    public async Task<UserEntitlementsDto?> GetEntitlementsAsync(string token, CancellationToken cancellationToken = default)
    {
        using var request = CreateAuthorizedRequest(HttpMethod.Get, "api/me/entitlements", token);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        return response.IsSuccessStatusCode
            ? await response.Content.ReadFromJsonAsync<UserEntitlementsDto>(JsonOptions, cancellationToken)
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
        using var request = CreateAuthorizedRequest(HttpMethod.Get, url, token);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        return response.IsSuccessStatusCode
            ? await response.Content.ReadFromJsonAsync<MobileBootstrapDto>(JsonOptions, cancellationToken)
            : null;
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

        return await GetPagedItemsAsync<RecommendationDto>(url, cancellationToken);
    }

    public async Task<TripScheduleDto?> GetDemoScheduleAsync(CancellationToken cancellationToken = default)
    {
        return await _httpClient.GetFromJsonAsync<TripScheduleDto>(
            "api/trips/44444444-4444-4444-4444-444444444401/schedule",
            JsonOptions,
            cancellationToken);
    }

    public async Task<TripScheduleDto?> GetScheduleAsync(string token, CancellationToken cancellationToken = default)
    {
        using var request = CreateAuthorizedRequest(HttpMethod.Get, "api/me/schedule", token);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        return response.IsSuccessStatusCode
            ? await response.Content.ReadFromJsonAsync<TripScheduleDto>(JsonOptions, cancellationToken)
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
            cancellationToken);

        return result?.Items ?? [];
    }

    private static async Task<IReadOnlyList<T>> ReadPagedItemsAsync<T>(
        HttpContent content,
        CancellationToken cancellationToken = default)
    {
        var result = await content.ReadFromJsonAsync<PagedResultDto<T>>(
            JsonOptions,
            cancellationToken);

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
}

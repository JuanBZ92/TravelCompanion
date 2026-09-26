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
    public event Func<Task>? ItineraryChanged;

    public async Task<IReadOnlyList<ReservationReminderDto>> GetReservationRemindersAsync(string token, CancellationToken ct)
    {
        using var request = CreateRequest(HttpMethod.Get,
            $"api/notifications/reminders?locale={CultureInfo.CurrentUICulture.TwoLetterISOLanguageName}", token);
        using var response = await _httpClient.SendAsync(request, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<List<ReservationReminderDto>>(JsonOptions, ct).ConfigureAwait(false) ?? [];
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

    public async Task<AuthSessionDto?> LoginWithPinAsync(string pin, CancellationToken cancellationToken = default)
    {
        const string clientInstanceKey = "free_trial_client_instance_id";
        var clientInstanceId = Preferences.Default.Get(clientInstanceKey, string.Empty);
        if (string.IsNullOrWhiteSpace(clientInstanceId))
        {
            clientInstanceId = Guid.NewGuid().ToString("N");
            Preferences.Default.Set(clientInstanceKey, clientInstanceId);
        }
        using var response = await _httpClient.PostAsJsonAsync(
            "api/auth/pin-login",
            new PinLoginRequestDto(pin, clientInstanceId, SupportsPersistentFree: true),
            JsonOptions,
            cancellationToken).ConfigureAwait(false);

        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<AuthSessionDto>(JsonOptions, cancellationToken).ConfigureAwait(false);
    }

    public async Task<AuthSessionDto?> RedeemTravelPassAsync(
        string token,
        string pin,
        CancellationToken cancellationToken = default)
    {
        using var request = CreateAuthorizedRequest(HttpMethod.Post, "api/mobile/pass/redeem", token);
        request.Content = JsonContent.Create(new RedeemTravelPassRequest(pin), options: JsonOptions);
        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        return await response.Content.ReadFromJsonAsync<AuthSessionDto>(JsonOptions, cancellationToken).ConfigureAwait(false);
    }

    public async Task<PaywallOfferDto?> GetPaywallOfferAsync(string token, Guid tripId, PaywallEntryPoint entryPoint, string platform, CancellationToken ct = default)
    {
        using var request = CreateAuthorizedRequest(HttpMethod.Get,
            $"api/mobile/conversion/paywall/{tripId}?entryPoint={entryPoint}&platform={Uri.EscapeDataString(platform)}", token);
        using var response = await _httpClient.SendAsync(request, ct).ConfigureAwait(false);
        return response.IsSuccessStatusCode ? await response.Content.ReadFromJsonAsync<PaywallOfferDto>(JsonOptions, ct).ConfigureAwait(false) : null;
    }

    public async Task<EmailCodeRequestedDto?> RequestEmailCodeAsync(string? token, string email, string locale, CancellationToken ct = default)
    {
        using var request = CreateRequest(HttpMethod.Post, "api/mobile/account/email/code", token);
        request.Content = JsonContent.Create(new RequestEmailCodeDto(email, locale), options: JsonOptions);
        using var response = await _httpClient.SendAsync(request, ct).ConfigureAwait(false);
        return response.IsSuccessStatusCode ? await response.Content.ReadFromJsonAsync<EmailCodeRequestedDto>(JsonOptions, ct).ConfigureAwait(false) : null;
    }

    public async Task<AuthSessionDto?> VerifyEmailCodeAsync(string? token, string email, string code, CancellationToken ct = default)
    {
        using var request = CreateRequest(HttpMethod.Post, "api/mobile/account/email/verify", token);
        request.Content = JsonContent.Create(new VerifyEmailCodeDto(email, code), options: JsonOptions);
        using var response = await _httpClient.SendAsync(request, ct).ConfigureAwait(false);
        return response.IsSuccessStatusCode ? await response.Content.ReadFromJsonAsync<AuthSessionDto>(JsonOptions, ct).ConfigureAwait(false) : null;
    }

    public async Task<PurchaseIntentDto?> CreatePurchaseIntentAsync(string token, CreatePurchaseIntentDto payload, CancellationToken ct = default)
    {
        using var request = CreateAuthorizedRequest(HttpMethod.Post, "api/mobile/purchases/intents", token);
        request.Content = JsonContent.Create(payload, options: JsonOptions);
        using var response = await _httpClient.SendAsync(request, ct).ConfigureAwait(false);
        return response.IsSuccessStatusCode ? await response.Content.ReadFromJsonAsync<PurchaseIntentDto>(JsonOptions, ct).ConfigureAwait(false) : null;
    }

    public async Task<PurchaseIntentDto?> CancelPurchaseIntentAsync(string token, Guid intentId, CancellationToken ct = default)
    {
        using var request = CreateAuthorizedRequest(HttpMethod.Post, $"api/mobile/purchases/intents/{intentId}/cancel", token);
        using var response = await _httpClient.SendAsync(request, ct).ConfigureAwait(false);
        return response.IsSuccessStatusCode
            ? await response.Content.ReadFromJsonAsync<PurchaseIntentDto>(JsonOptions, ct).ConfigureAwait(false) : null;
    }

    public Task<bool> SendProductAnalyticsAsync(string token, ProductAnalyticsEventDto analyticsEvent, CancellationToken ct = default) =>
        SendProductAnalyticsBatchAsync(token, [analyticsEvent], ct);

    public async Task<bool> SendProductAnalyticsBatchAsync(
        string token,
        IReadOnlyList<ProductAnalyticsEventDto> analyticsEvents,
        CancellationToken ct = default)
    {
        if (analyticsEvents.Count == 0) return true;
        using var request = CreateAuthorizedRequest(HttpMethod.Post, "api/mobile/conversion/events", token);
        request.Content = JsonContent.Create(new ProductAnalyticsBatchDto(analyticsEvents), options: JsonOptions);
        using var response = await _httpClient.SendAsync(request, ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            _logger.LogDebug("Product analytics delivery deferred after HTTP {StatusCode}.", (int)response.StatusCode);
        return response.IsSuccessStatusCode;
    }

    public async Task<PassAccessDto?> VerifyPurchaseAsync(string token, VerifyPurchaseDto payload, CancellationToken ct = default)
    {
        using var request = CreateAuthorizedRequest(HttpMethod.Post, "api/mobile/purchases/verify", token);
        request.Content = JsonContent.Create(payload, options: JsonOptions);
        using var response = await _httpClient.SendAsync(request, ct).ConfigureAwait(false);
        return response.IsSuccessStatusCode ? await response.Content.ReadFromJsonAsync<PassAccessDto>(JsonOptions, ct).ConfigureAwait(false) : null;
    }

    public async Task<IReadOnlyList<PassAccessDto>> RestorePassesAsync(string token, CancellationToken ct = default)
    {
        using var request = CreateAuthorizedRequest(HttpMethod.Post, "api/mobile/purchases/restore", token);
        using var response = await _httpClient.SendAsync(request, ct).ConfigureAwait(false);
        return response.IsSuccessStatusCode
            ? await response.Content.ReadFromJsonAsync<List<PassAccessDto>>(JsonOptions, ct).ConfigureAwait(false) ?? [] : [];
    }

    public async Task<AuthSessionDto?> SelectAccountTripAsync(string token, Guid tripId, CancellationToken ct = default)
    {
        using var request = CreateAuthorizedRequest(HttpMethod.Post, "api/mobile/account/select-trip", token);
        request.Content = JsonContent.Create(new SelectAccountTripDto(tripId), options: JsonOptions);
        using var response = await _httpClient.SendAsync(request, ct).ConfigureAwait(false);
        return response.IsSuccessStatusCode ? await response.Content.ReadFromJsonAsync<AuthSessionDto>(JsonOptions, ct).ConfigureAwait(false) : null;
    }

    public async Task<TravelerAccountDto?> GetAccountAsync(string token, CancellationToken ct = default)
    {
        using var request = CreateAuthorizedRequest(HttpMethod.Get, "api/mobile/account", token);
        using var response = await _httpClient.SendAsync(request, ct).ConfigureAwait(false);
        return response.IsSuccessStatusCode
            ? await response.Content.ReadFromJsonAsync<TravelerAccountDto>(JsonOptions, ct).ConfigureAwait(false)
            : null;
    }

    public async Task<AuthSessionDto?> ArchiveAccountTripAsync(string token, Guid tripId, CancellationToken ct = default)
    {
        using var request = CreateAuthorizedRequest(HttpMethod.Post, $"api/mobile/account/trips/{tripId}/archive", token);
        using var response = await _httpClient.SendAsync(request, ct).ConfigureAwait(false);
        return response.IsSuccessStatusCode
            ? await response.Content.ReadFromJsonAsync<AuthSessionDto>(JsonOptions, ct).ConfigureAwait(false)
            : null;
    }

    public async Task<bool> DeleteAccountAsync(string token, CancellationToken ct = default)
    {
        using var request = CreateAuthorizedRequest(HttpMethod.Delete, "api/mobile/account", token);
        using var response = await _httpClient.SendAsync(request, ct).ConfigureAwait(false);
        return response.IsSuccessStatusCode;
    }

    public async Task<bool> UpdateAnalyticsConsentAsync(string token, bool granted, CancellationToken ct = default)
    {
        using var request = CreateAuthorizedRequest(HttpMethod.Put, "api/mobile/account/analytics-consent", token);
        request.Content = JsonContent.Create(new UpdateAnalyticsConsentDto(granted), options: JsonOptions);
        using var response = await _httpClient.SendAsync(request, ct).ConfigureAwait(false);
        return response.IsSuccessStatusCode;
    }

    public async Task<IReadOnlyList<FreeMapCityDto>?> GetFreeMapCitiesAsync(
        string token,
        CancellationToken cancellationToken = default)
    {
        using var request = CreateAuthorizedRequest(HttpMethod.Get, "api/mobile/free-map/cities", token);
        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        return await response.Content
            .ReadFromJsonAsync<IReadOnlyList<FreeMapCityDto>>(JsonOptions, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<FreeMapPreviewDto?> GetFreeMapCityAsync(
        string token,
        string citySlug,
        CancellationToken cancellationToken = default)
    {
        using var request = CreateAuthorizedRequest(
            HttpMethod.Get,
            $"api/mobile/free-map/{Uri.EscapeDataString(citySlug)}",
            token);
        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        return await response.Content
            .ReadFromJsonAsync<FreeMapPreviewDto>(JsonOptions, cancellationToken)
            .ConfigureAwait(false);
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
        return (await GetMobileResultAsync<MobileBootstrapDto>(
            url,
            token,
            "bootstrap",
            MobilePayloadNormalizer.Normalize,
            cancellationToken).ConfigureAwait(false)).Value;
    }

    public Task<ApiCallResult<MobileBootstrapDto>> GetMobileBootstrapResultAsync(
        string token,
        string? destinationSlug = null,
        CancellationToken cancellationToken = default)
    {
        var url = string.IsNullOrWhiteSpace(destinationSlug)
            ? "api/mobile/bootstrap"
            : $"api/mobile/bootstrap?destinationSlug={Uri.EscapeDataString(destinationSlug)}";
        return GetMobileResultAsync<MobileBootstrapDto>(url, token, "bootstrap", MobilePayloadNormalizer.Normalize, cancellationToken);
    }

    public async Task<MobileSyncCheckResult> GetMobileSyncStateAsync(
        string token,
        string? etag,
        CancellationToken cancellationToken = default)
    {
        using var request = CreateAuthorizedRequest(HttpMethod.Get, "api/mobile/sync-state", token);
        if (!string.IsNullOrWhiteSpace(etag))
        {
            request.Headers.TryAddWithoutValidation("If-None-Match", etag);
        }

        try
        {
            using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            var responseEtag = response.Headers.ETag?.ToString() ?? etag;
            if (response.StatusCode == System.Net.HttpStatusCode.NotModified)
            {
                return new MobileSyncCheckResult(ApiCallStatus.Success, null, responseEtag, true);
            }
            if (!response.IsSuccessStatusCode)
            {
                return new MobileSyncCheckResult(
                    ApiCallResult<MobileSyncStateDto>.FromStatusCode(response.StatusCode).Status,
                    null,
                    responseEtag,
                    false);
            }

            var state = await response.Content.ReadFromJsonAsync<MobileSyncStateDto>(JsonOptions, cancellationToken).ConfigureAwait(false);
            return state is null
                ? new MobileSyncCheckResult(ApiCallStatus.InvalidResponse, null, responseEtag, false)
                : new MobileSyncCheckResult(ApiCallStatus.Success, state, responseEtag, false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new MobileSyncCheckResult(ApiCallStatus.TransientFailure, null, etag, false);
        }
        catch (HttpRequestException)
        {
            return new MobileSyncCheckResult(ApiCallStatus.TransientFailure, null, etag, false);
        }
    }

    public async Task<MobileDiscoverDto?> GetMobileDiscoverAsync(
        string token,
        string? destinationSlug = null,
        CancellationToken cancellationToken = default)
    {
        var url = string.IsNullOrWhiteSpace(destinationSlug)
            ? "api/mobile/discover"
            : $"api/mobile/discover?destinationSlug={Uri.EscapeDataString(destinationSlug)}";

        return (await GetMobileResultAsync<MobileDiscoverDto>(
            url,
            token,
            "discover",
            MobilePayloadNormalizer.Normalize,
            cancellationToken).ConfigureAwait(false)).Value;
    }

    public Task<ApiCallResult<MobileDiscoverDto>> GetMobileDiscoverResultAsync(
        string token,
        string? destinationSlug = null,
        CancellationToken cancellationToken = default)
    {
        var url = string.IsNullOrWhiteSpace(destinationSlug)
            ? "api/mobile/discover"
            : $"api/mobile/discover?destinationSlug={Uri.EscapeDataString(destinationSlug)}";
        return GetMobileResultAsync<MobileDiscoverDto>(url, token, "discover", MobilePayloadNormalizer.Normalize, cancellationToken);
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

        return (await GetMobileResultAsync<TodayDto>(
            url,
            token,
            "today",
            MobilePayloadNormalizer.Normalize,
            cancellationToken).ConfigureAwait(false)).Value;
    }

    public Task<ApiCallResult<TodayDto>> GetMobileTodayResultAsync(
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
        var url = query.Count == 0 ? "api/mobile/today" : $"api/mobile/today?{string.Join('&', query)}";
        return GetMobileResultAsync<TodayDto>(url, token, "today", MobilePayloadNormalizer.Normalize, cancellationToken);
    }

    private async Task<ApiCallResult<T>> GetMobileResultAsync<T>(
        string url,
        string token,
        string operation,
        Func<T?, T?> normalize,
        CancellationToken cancellationToken)
        where T : class
    {
        var stopwatch = Stopwatch.StartNew();
        using var request = CreateAuthorizedRequest(HttpMethod.Get, url, token);
        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("Mobile {Operation} request timed out after {ElapsedMs}ms.", operation, stopwatch.Elapsed.TotalMilliseconds);
            return ApiCallResult<T>.TransientFailure();
        }
        catch (HttpRequestException exception)
        {
            _logger.LogWarning(exception, "Mobile {Operation} request failed after {ElapsedMs}ms.", operation, stopwatch.Elapsed.TotalMilliseconds);
            return ApiCallResult<T>.TransientFailure();
        }

        using (response)
        {
            var headersElapsedMs = stopwatch.Elapsed.TotalMilliseconds;
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Mobile {Operation} request failed with {StatusCode} after {ElapsedMs}ms.",
                    operation,
                    (int)response.StatusCode,
                    headersElapsedMs);
                return ApiCallResult<T>.FromStatusCode(response.StatusCode);
            }

            T? payload;
            try
            {
                payload = await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken).ConfigureAwait(false);
            }
            catch (JsonException exception)
            {
                _logger.LogWarning(exception, "Mobile {Operation} returned an invalid response.", operation);
                return ApiCallResult<T>.InvalidResponse(response.StatusCode);
            }

            var normalized = normalize(payload);
            stopwatch.Stop();
            _logger.LogInformation(
                "Mobile {Operation} request completed in {ElapsedMs}ms. Headers={HeadersElapsedMs}ms; BodyAndJson={BodyElapsedMs}ms; ServerTiming={ServerTiming}; ResponseBytes={ResponseBytes}.",
                operation,
                stopwatch.Elapsed.TotalMilliseconds,
                headersElapsedMs,
                stopwatch.Elapsed.TotalMilliseconds - headersElapsedMs,
                GetServerTiming(response),
                response.Content.Headers.ContentLength);

            return normalized is null
                ? ApiCallResult<T>.InvalidResponse(response.StatusCode)
                : ApiCallResult<T>.Success(normalized);
        }
    }

    public async Task<TravelDocsDto?> GetTravelDocsAsync(
        string token,
        CancellationToken cancellationToken = default)
    {
        return (await GetTravelDocsResultAsync(token, cancellationToken).ConfigureAwait(false)).Value;
    }

    public Task<ApiCallResult<TravelDocsDto>> GetTravelDocsResultAsync(
        string token,
        CancellationToken cancellationToken = default) =>
        GetMobileResultAsync<TravelDocsDto>(
            "api/mobile/docs",
            token,
            "docs",
            static docs => docs,
            cancellationToken);

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
        if (saveRequest.ClientMutationId.HasValue)
        {
            request.Headers.TryAddWithoutValidation("Idempotency-Key", saveRequest.ClientMutationId.Value.ToString("N"));
        }

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
        string token,
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

        using var request = CreateAuthorizedRequest(HttpMethod.Get, url, token);
        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        return response.IsSuccessStatusCode
            ? await ReadPagedItemsAsync<RecommendationDto>(response.Content, cancellationToken).ConfigureAwait(false)
            : [];
    }

    public async Task<IReadOnlyList<RecommendationDto>> SearchPlacesAsync(
        string token,
        PlaceSearchRequest search,
        CancellationToken cancellationToken = default)
    {
        var result = await SearchPlacesResultAsync(token, search, cancellationToken).ConfigureAwait(false);
        return result.Value ?? [];
    }

    public async Task<ApiCallResult<IReadOnlyList<RecommendationDto>>> SearchPlacesResultAsync(
        string token,
        PlaceSearchRequest search,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        using var request = CreateAuthorizedRequest(HttpMethod.Post, "api/mobile/places/search", token);
        request.Content = JsonContent.Create(search, options: JsonOptions);
        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("Places search timed out after {ElapsedMs}ms.", stopwatch.Elapsed.TotalMilliseconds);
            return ApiCallResult<IReadOnlyList<RecommendationDto>>.TransientFailure();
        }
        catch (HttpRequestException exception)
        {
            _logger.LogWarning(exception, "Places search failed after {ElapsedMs}ms.", stopwatch.Elapsed.TotalMilliseconds);
            return ApiCallResult<IReadOnlyList<RecommendationDto>>.TransientFailure();
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                return ApiCallResult<IReadOnlyList<RecommendationDto>>.FromStatusCode(response.StatusCode);
            }

            try
            {
                var places = await response.Content
                    .ReadFromJsonAsync<IReadOnlyList<RecommendationDto>>(JsonOptions, cancellationToken)
                    .ConfigureAwait(false) ?? [];
                _logger.LogInformation(
                    "Places search completed in {ElapsedMs}ms. Results={ResultCount}; ResponseBytes={ResponseBytes}.",
                    stopwatch.Elapsed.TotalMilliseconds,
                    places.Count,
                    response.Content.Headers.ContentLength);
                return ApiCallResult<IReadOnlyList<RecommendationDto>>.Success(places);
            }
            catch (JsonException exception)
            {
                _logger.LogWarning(exception, "Places search returned an invalid response.");
                return ApiCallResult<IReadOnlyList<RecommendationDto>>.InvalidResponse(response.StatusCode);
            }
        }
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

    public async Task<BuilderTripSetupDto?> GetBuilderTripSetupAsync(string token, CancellationToken cancellationToken = default)
    {
        using var request = CreateAuthorizedRequest(HttpMethod.Get, "api/mobile/builder/setup", token);
        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Builder setup load failed with {StatusCode}.", (int)response.StatusCode);
            return null;
        }

        return await response.Content.ReadFromJsonAsync<BuilderTripSetupDto>(JsonOptions, cancellationToken).ConfigureAwait(false);
    }

    public async Task<ItineraryRouteDto?> GetItineraryRouteAsync(string token, Guid id, ItineraryRouteRequest route, CancellationToken ct)
    {
        using var request = CreateAuthorizedRequest(HttpMethod.Post, $"api/mobile/itinerary/{id}/route", token);
        request.Content = JsonContent.Create(route, options: JsonOptions);
        using var response = await _httpClient.SendAsync(request, ct).ConfigureAwait(false);
        return response.IsSuccessStatusCode
            ? await response.Content.ReadFromJsonAsync<ItineraryRouteDto>(JsonOptions, ct).ConfigureAwait(false) : null;
    }

    public async Task<BuilderTripSetupDto?> SaveBuilderTripSetupAsync(string token, SaveBuilderTripSetupRequest setup, CancellationToken cancellationToken = default)
    {
        using var request = CreateAuthorizedRequest(HttpMethod.Put, "api/mobile/builder/setup", token);
        request.Content = JsonContent.Create(setup, options: JsonOptions);
        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Builder setup failed with {StatusCode}.", (int)response.StatusCode);
            var error = await ReadApiErrorMessageAsync(response, cancellationToken).ConfigureAwait(false);
            throw new InvalidOperationException(error ?? "No pudimos crear el itinerario. Intenta nuevamente.");
        }
        return await response.Content.ReadFromJsonAsync<BuilderTripSetupDto>(JsonOptions, cancellationToken).ConfigureAwait(false);
    }

    public async Task<BuilderTripSetupDto?> DeleteBuilderTripSetupAsync(
        string token,
        DeleteBuilderTripSetupRequest setup,
        CancellationToken cancellationToken = default)
    {
        using var request = CreateAuthorizedRequest(HttpMethod.Delete, "api/mobile/builder/setup", token);
        request.Content = JsonContent.Create(setup, options: JsonOptions);
        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Builder setup deletion failed with {StatusCode}.", (int)response.StatusCode);
            if (response.StatusCode == System.Net.HttpStatusCode.Conflict)
            {
                throw new InvalidOperationException(LocalizationResourceManager.Instance["ItineraryDeleteConflict"]);
            }
            var error = await ReadApiErrorMessageAsync(response, cancellationToken).ConfigureAwait(false);
            throw new InvalidOperationException(error ?? "No pudimos eliminar el itinerario. Intenta nuevamente.");
        }

        return await response.Content.ReadFromJsonAsync<BuilderTripSetupDto>(JsonOptions, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<string?> ReadApiErrorMessageAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            var payload = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions, cancellationToken).ConfigureAwait(false);
            if (payload.ValueKind == JsonValueKind.Object)
            {
                if (payload.TryGetProperty("message", out var message) && message.ValueKind == JsonValueKind.String)
                {
                    return message.GetString();
                }

                if (payload.TryGetProperty("detail", out var detail) && detail.ValueKind == JsonValueKind.String)
                {
                    return detail.GetString();
                }
            }
        }
        catch (JsonException)
        {
            // The fallback below is intentionally user-safe when the server returns non-JSON content.
        }

        return null;
    }

    public async Task<ItineraryItemMutationResponse?> CreateItineraryItemAsync(string token, ItineraryItemMutationRequest mutation, CancellationToken cancellationToken = default)
    {
        using var request = CreateAuthorizedRequest(HttpMethod.Post, "api/mobile/itinerary", token);
        request.Content = JsonContent.Create(mutation, options: JsonOptions);
        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var result = await response.Content.ReadFromJsonAsync<ItineraryItemMutationResponse>(JsonOptions, cancellationToken).ConfigureAwait(false);
        if (response.IsSuccessStatusCode && result?.Success == true && ItineraryChanged is { } changed)
            await changed().ConfigureAwait(false);
        return result;
    }

    public async Task<ItineraryItemMutationResponse?> UpdateItineraryItemAsync(string token, Guid id, ItineraryItemMutationRequest mutation, CancellationToken cancellationToken = default)
    {
        using var request = CreateAuthorizedRequest(HttpMethod.Patch, $"api/mobile/itinerary/{id}", token);
        request.Content = JsonContent.Create(mutation, options: JsonOptions);
        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var result = await response.Content.ReadFromJsonAsync<ItineraryItemMutationResponse>(JsonOptions, cancellationToken).ConfigureAwait(false);
        if (response.IsSuccessStatusCode && result?.Success == true && ItineraryChanged is { } changed)
            await changed().ConfigureAwait(false);
        return result;
    }

    public async Task<ItineraryItemMutationResponse?> DeleteItineraryItemAsync(string token, Guid id, int expectedRevision, CancellationToken cancellationToken = default)
    {
        using var request = CreateAuthorizedRequest(HttpMethod.Delete, $"api/mobile/itinerary/{id}?expectedRevision={expectedRevision}", token);
        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var result = await response.Content.ReadFromJsonAsync<ItineraryItemMutationResponse>(JsonOptions, cancellationToken).ConfigureAwait(false);
        if (response.IsSuccessStatusCode && result?.Success == true && ItineraryChanged is { } changed)
            await changed().ConfigureAwait(false);
        return result;
    }

    private static HttpRequestMessage CreateAuthorizedRequest(HttpMethod method, string url, string token)
        => CreateRequest(method, url, token);

    private static HttpRequestMessage CreateRequest(HttpMethod method, string url, string? token = null)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.AcceptLanguage.ParseAdd(System.Globalization.CultureInfo.CurrentUICulture.Name);
        if (!string.IsNullOrWhiteSpace(token))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
    }

    public async Task<PagedResultDto<RecommendationDto>?> SearchPlacesPageAsync(string token, PlaceSearchRequest query, int page, CancellationToken cancellationToken = default)
    {
        using var request = CreateAuthorizedRequest(HttpMethod.Post, $"api/mobile/places/search-page?page={page}", token);
        request.Content = JsonContent.Create(query, options: JsonOptions);
        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<PagedResultDto<RecommendationDto>>(JsonOptions, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<PlaceSuggestionDto>> AutocompleteHotelsAsync(string token, PlaceAutocompleteRequest query, CancellationToken cancellationToken = default)
        => await AutocompletePlacesAsync(token, query, cancellationToken).ConfigureAwait(false);

    public async Task<IReadOnlyList<PlaceSuggestionDto>> AutocompletePlacesAsync(string token, PlaceAutocompleteRequest query, CancellationToken cancellationToken = default)
    {
        using var request = CreateAuthorizedRequest(HttpMethod.Post, "api/mobile/places/autocomplete", token);
        request.Content = JsonContent.Create(query, options: JsonOptions);
        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<List<PlaceSuggestionDto>>(JsonOptions, cancellationToken).ConfigureAwait(false) ?? [];
    }

    public async Task<RecommendationDto?> GetPlaceDetailsAsync(string token, PlaceDetailsRequest query, CancellationToken cancellationToken = default)
    {
        using var request = CreateAuthorizedRequest(HttpMethod.Post, "api/mobile/places/details", token);
        request.Content = JsonContent.Create(query, options: JsonOptions);
        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) return null;
        return await response.Content.ReadFromJsonAsync<RecommendationDto>(JsonOptions, cancellationToken).ConfigureAwait(false);
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

public sealed record MobileSyncCheckResult(
    ApiCallStatus Status,
    MobileSyncStateDto? State,
    string? ETag,
    bool NotModified);

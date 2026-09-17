using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using TravelCompanion.Api.Options;
using TravelCompanion.Shared;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Api.Services;

public interface IGooglePlacesService
{
    Task<IReadOnlyList<RecommendationDto>> SearchAsync(Guid destinationId, PlaceSearchRequest request, CancellationToken cancellationToken);
    Task<IReadOnlyList<PlaceSuggestionDto>> AutocompleteAsync(PlaceAutocompleteRequest request, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<PlaceSuggestionDto>>([]);
    Task<RecommendationDto?> DetailsAsync(Guid destinationId, PlaceDetailsRequest request, CancellationToken cancellationToken) => Task.FromResult<RecommendationDto?>(null);
}

public sealed class GooglePlacesService(
    IHttpClientFactory httpClientFactory,
    IOptions<GooglePlacesOptions> options,
    ILogger<GooglePlacesService> logger) : IGooglePlacesService
{
    public async Task<IReadOnlyList<PlaceSuggestionDto>> AutocompleteAsync(PlaceAutocompleteRequest request, CancellationToken cancellationToken)
    {
        if (!options.Value.Enabled || string.IsNullOrWhiteSpace(options.Value.ApiKey)) return [];
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        using var message = new HttpRequestMessage(HttpMethod.Post, "https://places.googleapis.com/v1/places:autocomplete");
        message.Headers.Add("X-Goog-Api-Key", options.Value.ApiKey);
        message.Headers.Add("X-Goog-FieldMask", "suggestions.placePrediction.placeId,suggestions.placePrediction.structuredFormat");
        var requestPayload = new Dictionary<string, object?>
        {
            ["input"] = BuildAutocompleteInput(request.Query, request.City),
            ["includedRegionCodes"] = new[] { "jp" },
            ["sessionToken"] = request.SessionToken,
            ["languageCode"] = Language(request.Locale)
        };
        if (request.Mode == PlaceAutocompleteMode.Hotel)
        {
            requestPayload["includedPrimaryTypes"] = new[] { "lodging" };
        }
        message.Content = JsonContent.Create(requestPayload);
        try
        {
            using var response = await httpClientFactory.CreateClient().SendAsync(message, timeout.Token);
            if (!response.IsSuccessStatusCode) { logger.LogWarning("Places autocomplete returned {StatusCode}.", (int)response.StatusCode); return []; }
            var payload = await response.Content.ReadFromJsonAsync<AutocompleteResponse>(cancellationToken: timeout.Token);
            return payload?.Suggestions?.Where(s => !string.IsNullOrWhiteSpace(s.PlacePrediction?.PlaceId))
                .Take(5).Select(s => new PlaceSuggestionDto(s.PlacePrediction!.PlaceId,
                    s.PlacePrediction.StructuredFormat?.MainText?.Text
                        ?? (request.Mode == PlaceAutocompleteMode.Hotel ? "Hotel" : "Lugar"),
                    s.PlacePrediction.StructuredFormat?.SecondaryText?.Text ?? string.Empty)).ToList() ?? [];
        }
        catch (HttpRequestException) { logger.LogWarning("Places autocomplete unavailable."); return []; }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { logger.LogWarning("Places autocomplete timed out."); return []; }
    }

    public async Task<RecommendationDto?> DetailsAsync(Guid destinationId, PlaceDetailsRequest request, CancellationToken cancellationToken)
    {
        if (!options.Value.Enabled || string.IsNullOrWhiteSpace(options.Value.ApiKey)) return null;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        using var message = new HttpRequestMessage(HttpMethod.Get,
            $"https://places.googleapis.com/v1/places/{Uri.EscapeDataString(request.PlaceId)}?languageCode={Language(request.Locale)}"
            + (string.IsNullOrWhiteSpace(request.SessionToken) ? string.Empty : $"&sessionToken={Uri.EscapeDataString(request.SessionToken)}"));
        message.Headers.Add("X-Goog-Api-Key", options.Value.ApiKey);
        message.Headers.Add("X-Goog-FieldMask", "id,displayName,formattedAddress,location,primaryType");
        try
        {
            using var response = await httpClientFactory.CreateClient().SendAsync(message, timeout.Token);
            if (!response.IsSuccessStatusCode) { logger.LogWarning("Places details returned {StatusCode}.", (int)response.StatusCode); return null; }
            var place = await response.Content.ReadFromJsonAsync<GooglePlace>(cancellationToken: timeout.Token);
            return place?.Location is null ? null : new RecommendationDto(Guid.Empty, destinationId,
                place.DisplayName?.Text ?? "Hotel", place.PrimaryType ?? "lodging", place.FormattedAddress ?? string.Empty,
                string.Empty, [], "medium", (decimal)place.Location.Latitude, (decimal)place.Location.Longitude,
                60, null, null, ContentAccessLevel.Free, [], null)
            { Provider = "Google", ProviderPlaceId = place.Id, Attribution = "Google Maps", IsPriceKnown = false };
        }
        catch (HttpRequestException) { logger.LogWarning("Places details unavailable."); return null; }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { logger.LogWarning("Places details timed out."); return null; }
    }

    private static string Language(string? locale) => (locale ?? System.Globalization.CultureInfo.CurrentUICulture.Name).StartsWith("en", StringComparison.OrdinalIgnoreCase) ? "en" : "es";
    private static string BuildAutocompleteInput(string query, string? city)
    {
        var input = NormalizeSearchText(query);
        var normalizedCity = NormalizeSearchText(city ?? string.Empty);
        if (!string.IsNullOrWhiteSpace(normalizedCity) && !ContainsSearchTerm(input, normalizedCity))
        {
            input = $"{input}, {normalizedCity}";
        }

        if (!ContainsSearchTerm(input, "Japan") && !ContainsSearchTerm(input, "Japon"))
        {
            input = $"{input}, Japan";
        }

        return input;
    }

    private static string NormalizeSearchText(string value) =>
        string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static bool ContainsSearchTerm(string input, string term) =>
        CultureInfo.InvariantCulture.CompareInfo.IndexOf(
            input,
            term,
            CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) >= 0;

    private sealed record AutocompleteResponse(List<AutocompleteSuggestion>? Suggestions);
    private sealed record AutocompleteSuggestion(PlacePrediction? PlacePrediction);
    private sealed record PlacePrediction(string PlaceId, StructuredFormat? StructuredFormat);
    private sealed record StructuredFormat(GoogleDisplayName? MainText, GoogleDisplayName? SecondaryText);

    public async Task<IReadOnlyList<RecommendationDto>> SearchAsync(Guid destinationId, PlaceSearchRequest request, CancellationToken cancellationToken)
    {
        var configuration = options.Value;
        if (!configuration.Enabled || string.IsNullOrWhiteSpace(configuration.ApiKey) || request.Query.Trim().Length < 3)
        {
            return [];
        }

        try
        {
            using var message = new HttpRequestMessage(HttpMethod.Post, "https://places.googleapis.com/v1/places:searchText");
            message.Headers.Add("X-Goog-Api-Key", configuration.ApiKey);
            message.Headers.Add("X-Goog-FieldMask", "places.id,places.displayName,places.formattedAddress,places.location,places.primaryType,places.rating");
            message.Content = JsonContent.Create(new
            {
                textQuery = string.IsNullOrWhiteSpace(request.City) ? request.Query.Trim() : $"{request.Query.Trim()} in {request.City.Trim()}, Japan",
                maxResultCount = Math.Clamp(configuration.MaxResults, 1, 20),
                locationBias = request.Latitude.HasValue && request.Longitude.HasValue
                    ? new { circle = new { center = new { latitude = request.Latitude, longitude = request.Longitude }, radius = 15000.0 } }
                    : null
            });
            using var response = await httpClientFactory.CreateClient().SendAsync(message, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Google Places search returned {StatusCode}.", (int)response.StatusCode);
                return [];
            }

            var payload = await response.Content.ReadFromJsonAsync<GooglePlacesResponse>(cancellationToken: cancellationToken);
            return payload?.Places?.Where(place => place.Location is not null && !string.IsNullOrWhiteSpace(place.Id))
                .Select(place => new RecommendationDto(
                    Guid.Empty, destinationId, place.DisplayName?.Text ?? "Lugar", place.PrimaryType ?? "place",
                    place.FormattedAddress ?? string.Empty, string.Empty, [], "medium",
                    (decimal)place.Location!.Latitude, (decimal)place.Location.Longitude, 60, place.Rating,
                    null, ContentAccessLevel.Free, [], null)
                {
                    Provider = "Google",
                    ProviderPlaceId = place.Id,
                    Attribution = "Google Maps",
                    IsPriceKnown = false
                }).ToList() ?? [];
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning("Google Places is temporarily unavailable.");
            return [];
        }
    }

    private sealed record GooglePlacesResponse([property: JsonPropertyName("places")] List<GooglePlace>? Places);
    private sealed record GooglePlace(
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("displayName")] GoogleDisplayName? DisplayName,
        [property: JsonPropertyName("formattedAddress")] string? FormattedAddress,
        [property: JsonPropertyName("location")] GoogleLocation? Location,
        [property: JsonPropertyName("primaryType")] string? PrimaryType,
        [property: JsonPropertyName("rating")] double? Rating);
    private sealed record GoogleDisplayName([property: JsonPropertyName("text")] string Text);
    private sealed record GoogleLocation([property: JsonPropertyName("latitude")] double Latitude, [property: JsonPropertyName("longitude")] double Longitude);
}

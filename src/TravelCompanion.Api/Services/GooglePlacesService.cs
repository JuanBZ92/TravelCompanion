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
}

public sealed class GooglePlacesService(
    IHttpClientFactory httpClientFactory,
    IOptions<GooglePlacesOptions> options,
    ILogger<GooglePlacesService> logger) : IGooglePlacesService
{
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
                    Attribution = "Google"
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

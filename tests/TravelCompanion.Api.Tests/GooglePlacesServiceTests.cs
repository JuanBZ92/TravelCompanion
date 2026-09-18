using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using TravelCompanion.Api.Options;
using TravelCompanion.Api.Services;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Api.Tests;

public sealed class GooglePlacesServiceTests
{
    [Theory]
    [InlineData("  tokyo   hotel  ", "Tokyo", "tokyo hotel, Japan")]
    [InlineData("Park Hyatt", "Tokyo", "Park Hyatt, Tokyo, Japan")]
    public async Task Autocomplete_normalizes_words_and_does_not_duplicate_city(
        string query,
        string city,
        string expectedInput)
    {
        var handler = new Handler();
        var service = new GooglePlacesService(
            new Factory(handler),
            Microsoft.Extensions.Options.Options.Create(new GooglePlacesOptions { Enabled = true, ApiKey = "test" }),
            NullLogger<GooglePlacesService>.Instance);

        var results = await service.AutocompleteAsync(
            new PlaceAutocompleteRequest(query, city, Guid.NewGuid().ToString(), "es-ES"),
            CancellationToken.None);

        Assert.Single(results);
        using var body = JsonDocument.Parse(Assert.Single(handler.Bodies));
        Assert.Equal(expectedInput, body.RootElement.GetProperty("input").GetString());
        Assert.Equal("lodging", body.RootElement.GetProperty("includedPrimaryTypes")[0].GetString());
    }

    [Fact]
    public async Task Autocomplete_place_mode_keeps_japan_scope_without_lodging_filter()
    {
        var handler = new Handler();
        var service = new GooglePlacesService(
            new Factory(handler),
            Microsoft.Extensions.Options.Options.Create(new GooglePlacesOptions { Enabled = true, ApiKey = "test" }),
            NullLogger<GooglePlacesService>.Instance);

        var results = await service.AutocompleteAsync(
            new PlaceAutocompleteRequest(
                "Tonkatsu",
                "Kyoto",
                Guid.NewGuid().ToString(),
                "es-ES",
                PlaceAutocompleteMode.Place),
            CancellationToken.None);

        Assert.Single(results);
        using var body = JsonDocument.Parse(Assert.Single(handler.Bodies));
        Assert.Equal("Tonkatsu, Kyoto, Japan", body.RootElement.GetProperty("input").GetString());
        Assert.Equal("jp", body.RootElement.GetProperty("includedRegionCodes")[0].GetString());
        Assert.False(body.RootElement.TryGetProperty("includedPrimaryTypes", out _));
    }

    [Fact]
    public async Task Text_search_restricts_google_to_japan_and_rejects_foreign_results()
    {
        const string response = """
            {
              "places": [
                {
                  "id": "japan-1",
                  "displayName": { "text": "Starbucks Shibuya" },
                  "formattedAddress": "Tokyo, Japan",
                  "addressComponents": [{ "shortText": "JP", "types": ["country"] }],
                  "location": { "latitude": 35.6595, "longitude": 139.7005 },
                  "primaryType": "cafe",
                  "rating": 4.2
                },
                {
                  "id": "usa-1",
                  "displayName": { "text": "Starbucks Seattle" },
                  "formattedAddress": "Seattle, WA, USA",
                  "addressComponents": [{ "shortText": "US", "types": ["country"] }],
                  "location": { "latitude": 47.6062, "longitude": -122.3321 },
                  "primaryType": "cafe",
                  "rating": 4.4
                }
              ]
            }
            """;
        var handler = new Handler(response);
        var service = new GooglePlacesService(
            new Factory(handler),
            Microsoft.Extensions.Options.Options.Create(new GooglePlacesOptions { Enabled = true, ApiKey = "test" }),
            NullLogger<GooglePlacesService>.Instance);

        var results = await service.SearchAsync(
            Guid.NewGuid(),
            new PlaceSearchRequest("Starbucks"),
            CancellationToken.None);

        var result = Assert.Single(results);
        Assert.Equal("japan-1", result.ProviderPlaceId);
        using var body = JsonDocument.Parse(Assert.Single(handler.Bodies));
        Assert.Equal("Starbucks, Japan", body.RootElement.GetProperty("textQuery").GetString());
        Assert.False(body.RootElement.TryGetProperty("locationBias", out _));
        var rectangle = body.RootElement.GetProperty("locationRestriction").GetProperty("rectangle");
        Assert.Equal(20.0, rectangle.GetProperty("low").GetProperty("latitude").GetDouble());
        Assert.Equal(154.5, rectangle.GetProperty("high").GetProperty("longitude").GetDouble());
        Assert.Contains("places.addressComponents", Assert.Single(handler.FieldMasks));
    }

    [Fact]
    public async Task Details_rejects_a_place_outside_japan()
    {
        const string response = """
            {
              "id": "france-1",
              "displayName": { "text": "Eiffel Tower" },
              "formattedAddress": "Paris, France",
              "addressComponents": [{ "shortText": "FR", "types": ["country"] }],
              "location": { "latitude": 48.8584, "longitude": 2.2945 },
              "primaryType": "tourist_attraction"
            }
            """;
        var handler = new Handler(response);
        var service = new GooglePlacesService(
            new Factory(handler),
            Microsoft.Extensions.Options.Options.Create(new GooglePlacesOptions { Enabled = true, ApiKey = "test" }),
            NullLogger<GooglePlacesService>.Instance);

        var result = await service.DetailsAsync(
            Guid.NewGuid(),
            new PlaceDetailsRequest("france-1", Guid.NewGuid().ToString(), "es-ES"),
            CancellationToken.None);

        Assert.Null(result);
        Assert.Contains("addressComponents", Assert.Single(handler.FieldMasks));
    }

    private sealed class Factory(Handler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class Handler(string? responseContent = null) : HttpMessageHandler
    {
        public List<string> Bodies { get; } = [];
        public List<string> FieldMasks { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (request.Content is not null)
            {
                Bodies.Add(await request.Content.ReadAsStringAsync(cancellationToken));
            }
            FieldMasks.Add(request.Headers.GetValues("X-Goog-FieldMask").Single());
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    responseContent
                    ?? """{"suggestions":[{"placePrediction":{"placeId":"hotel-1","structuredFormat":{"mainText":{"text":"Tokyo Hotel"},"secondaryText":{"text":"Tokyo, Japan"}}}}]}""")
            };
        }
    }
}

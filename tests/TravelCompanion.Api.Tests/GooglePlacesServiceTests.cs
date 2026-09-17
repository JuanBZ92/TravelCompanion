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

    private sealed class Factory(Handler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class Handler : HttpMessageHandler
    {
        public List<string> Bodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Bodies.Add(await request.Content!.ReadAsStringAsync(cancellationToken));
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """{"suggestions":[{"placePrediction":{"placeId":"hotel-1","structuredFormat":{"mainText":{"text":"Tokyo Hotel"},"secondaryText":{"text":"Tokyo, Japan"}}}}]}""")
            };
        }
    }
}

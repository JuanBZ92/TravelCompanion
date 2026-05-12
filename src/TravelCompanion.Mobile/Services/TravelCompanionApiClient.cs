using System.Net.Http.Json;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Mobile.Services;

public sealed class TravelCompanionApiClient
{
    private readonly HttpClient _httpClient;

    public TravelCompanionApiClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<IReadOnlyList<DestinationSummaryDto>> GetDestinationsAsync(CancellationToken cancellationToken = default)
    {
        return await _httpClient.GetFromJsonAsync<IReadOnlyList<DestinationSummaryDto>>(
            "api/destinations",
            cancellationToken) ?? [];
    }

    public async Task<IReadOnlyList<TravelPackageDto>> GetPackagesAsync(CancellationToken cancellationToken = default)
    {
        return await _httpClient.GetFromJsonAsync<IReadOnlyList<TravelPackageDto>>(
            "api/packages?destinationSlug=japon",
            cancellationToken) ?? [];
    }

    public async Task<IReadOnlyList<RecommendationDto>> GetRecommendationsAsync(
        decimal? latitude = null,
        decimal? longitude = null,
        CancellationToken cancellationToken = default)
    {
        var url = "api/recommendations?destinationSlug=japon";
        if (latitude.HasValue && longitude.HasValue)
        {
            url += $"&latitude={latitude.Value}&longitude={longitude.Value}";
        }

        return await _httpClient.GetFromJsonAsync<IReadOnlyList<RecommendationDto>>(
            url,
            cancellationToken) ?? [];
    }

    public async Task<TripScheduleDto?> GetDemoScheduleAsync(CancellationToken cancellationToken = default)
    {
        return await _httpClient.GetFromJsonAsync<TripScheduleDto>(
            "api/trips/44444444-4444-4444-4444-444444444401/schedule",
            cancellationToken);
    }
}

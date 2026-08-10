using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Mobile.Services;

public sealed class FreeMapStore(
    TravelCompanionApiClient apiClient,
    OfflineCacheService offlineCacheService)
{
    private const string CitiesCacheKey = "free-map-cities";
    private const string CityCachePrefix = "free-map-city-";

    public Task<OfflineCacheResult<IReadOnlyList<FreeMapCityDto>>?> GetCachedCitiesAsync(
        CancellationToken cancellationToken = default) =>
        offlineCacheService.GetAsync<IReadOnlyList<FreeMapCityDto>>(CitiesCacheKey, cancellationToken);

    public Task<OfflineCacheResult<FreeMapPreviewDto>?> GetCachedCityAsync(
        string citySlug,
        CancellationToken cancellationToken = default) =>
        offlineCacheService.GetAsync<FreeMapPreviewDto>(GetCityCacheKey(citySlug), cancellationToken);

    public async Task<IReadOnlyList<FreeMapCityDto>?> RefreshCitiesAsync(
        string token,
        CancellationToken cancellationToken = default)
    {
        var cities = await apiClient.GetFreeMapCitiesAsync(token, cancellationToken);
        if (cities is not null)
        {
            await offlineCacheService.SaveAsync(CitiesCacheKey, cities, cancellationToken);
        }

        return cities;
    }

    public async Task<FreeMapPreviewDto?> RefreshCityAsync(
        string token,
        string citySlug,
        CancellationToken cancellationToken = default)
    {
        var preview = await apiClient.GetFreeMapCityAsync(token, citySlug, cancellationToken);
        if (preview is not null)
        {
            await offlineCacheService.SaveAsync(GetCityCacheKey(citySlug), preview, cancellationToken);
        }

        return preview;
    }

    public Task ClearAsync() => offlineCacheService.DeleteByPrefixAsync(CityCachePrefix, CitiesCacheKey);

    private static string GetCityCacheKey(string citySlug) => $"{CityCachePrefix}{citySlug}";
}

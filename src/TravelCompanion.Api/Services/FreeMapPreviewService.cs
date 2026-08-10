using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TravelCompanion.Api.Data;
using TravelCompanion.Api.Models;
using TravelCompanion.Api.Options;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Api.Services;

public sealed class FreeMapPreviewService(
    TravelCompanionDbContext dbContext,
    IOptions<FreePreviewOptions> options,
    IWebHostEnvironment environment)
{
    public async Task<IReadOnlyList<FreeMapCityDto>> GetCitiesAsync(
        CancellationToken cancellationToken = default)
    {
        return await dbContext.FreeMapCities
            .AsNoTracking()
            .Where(city => city.IsEnabled)
            .OrderBy(city => city.SortOrder)
            .ThenBy(city => city.DisplayName)
            .Select(city => new FreeMapCityDto(
                city.CitySlug,
                city.DisplayName,
                city.CenterLatitude,
                city.CenterLongitude,
                city.FreeRadiusKm,
                city.SortOrder))
            .ToListAsync(cancellationToken);
    }

    public async Task<FreeMapPreviewDto?> GetCityAsync(
        string citySlug,
        CancellationToken cancellationToken = default)
    {
        var normalizedSlug = RecommendationCitySlug.FromCity(citySlug);
        var city = await dbContext.FreeMapCities
            .AsNoTracking()
            .FirstOrDefaultAsync(existing =>
                existing.IsEnabled && existing.CitySlug == normalizedSlug,
                cancellationToken);
        if (city is null)
        {
            return null;
        }

        var recommendations = await dbContext.Recommendations
            .AsNoTracking()
            .Where(recommendation => recommendation.DestinationId == city.DestinationId)
            .OrderBy(recommendation => recommendation.Title)
            .ToListAsync(cancellationToken);

        var candidates = recommendations
            .Where(recommendation => ResolveCitySlug(recommendation) == city.CitySlug)
            .Select(recommendation => new Candidate(
                recommendation,
                CalculateDistanceKm(
                    city.CenterLatitude,
                    city.CenterLongitude,
                    recommendation.Latitude,
                    recommendation.Longitude)))
            .Where(candidate => candidate.DistanceKm <= city.CoverageRadiusKm)
            .OrderBy(candidate => candidate.DistanceKm)
            .ThenBy(candidate => candidate.Recommendation.Title)
            .ToList();

        var markers = candidates
            .Select(candidate => candidate.DistanceKm <= city.FreeRadiusKm
                ? CreateUnlockedMarker(candidate)
                : CreateLockedMarker(city, candidate.Recommendation))
            .ToList();

        var unlockedCount = markers.Count(marker => marker.Access == FreeMapMarkerAccess.Unlocked);
        return new FreeMapPreviewDto(
            DateTimeOffset.UtcNow,
            ToDto(city),
            string.IsNullOrWhiteSpace(city.ContactUrl) ? null : city.ContactUrl,
            unlockedCount,
            markers.Count - unlockedCount,
            markers);
    }

    private FreeMapMarkerDto CreateLockedMarker(FreeMapCity city, Recommendation recommendation)
    {
        var key = GetObfuscationKey();
        using var hmac = new HMACSHA256(key);
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes($"{city.Id:N}:{recommendation.Id:N}"));
        var angleRatio = BitConverter.ToUInt64(hash, 0) / (double)ulong.MaxValue;
        var distanceRatio = BitConverter.ToUInt64(hash, 8) / (double)ulong.MaxValue;
        var angleRadians = angleRatio * Math.PI * 2;
        var offsetKm = 0.3 + distanceRatio * 0.4;
        var latitude = (double)recommendation.Latitude + Math.Cos(angleRadians) * offsetKm / 111.32;
        var longitudeScale = Math.Max(0.2, Math.Cos((double)recommendation.Latitude * Math.PI / 180));
        var longitude = (double)recommendation.Longitude
            + Math.Sin(angleRadians) * offsetKm / (111.32 * longitudeScale);

        var displayedDistance = CalculateDistanceKm(
            city.CenterLatitude,
            city.CenterLongitude,
            (decimal)latitude,
            (decimal)longitude);
        var minimumLockedDistance = city.FreeRadiusKm + 0.15m;
        if (displayedDistance < minimumLockedDistance)
        {
            (latitude, longitude) = MoveFromCenter(
                city.CenterLatitude,
                city.CenterLongitude,
                (decimal)latitude,
                (decimal)longitude,
                minimumLockedDistance);
        }

        var markerKey = Convert.ToHexString(hash.AsSpan(16, 10)).ToLowerInvariant();
        return new FreeMapMarkerDto(
            markerKey,
            Math.Round((decimal)latitude, 6),
            Math.Round((decimal)longitude, 6),
            FreeMapMarkerAccess.Locked,
            null);
    }

    private static FreeMapMarkerDto CreateUnlockedMarker(Candidate candidate)
    {
        var recommendation = candidate.Recommendation;
        return new FreeMapMarkerDto(
            recommendation.Id.ToString("N"),
            recommendation.Latitude,
            recommendation.Longitude,
            FreeMapMarkerAccess.Unlocked,
            new RecommendationDto(
                recommendation.Id,
                recommendation.DestinationId,
                recommendation.Title,
                recommendation.Category,
                recommendation.Neighborhood,
                recommendation.Description,
                recommendation.Tags,
                recommendation.PriceLevel,
                recommendation.Latitude,
                recommendation.Longitude,
                recommendation.SuggestedDurationMinutes,
                recommendation.Rating,
                recommendation.OpeningHours,
                recommendation.AccessLevel,
                [],
                candidate.DistanceKm));
    }

    private byte[] GetObfuscationKey()
    {
        var configuredKey = options.Value.MarkerObfuscationKey;
        if (!string.IsNullOrWhiteSpace(configuredKey))
        {
            return SHA256.HashData(Encoding.UTF8.GetBytes(configuredKey));
        }

        if (environment.IsProduction())
        {
            throw new InvalidOperationException(
                "FreePreview:MarkerObfuscationKey must be configured in Production.");
        }

        return SHA256.HashData(Encoding.UTF8.GetBytes("travelcompanion-free-preview-local-only"));
    }

    private static string ResolveCitySlug(Recommendation recommendation) =>
        string.IsNullOrWhiteSpace(recommendation.CitySlug)
            ? RecommendationCitySlug.FromCity(recommendation.Neighborhood)
            : RecommendationCitySlug.FromCity(recommendation.CitySlug);

    private static FreeMapCityDto ToDto(FreeMapCity city) =>
        new(
            city.CitySlug,
            city.DisplayName,
            city.CenterLatitude,
            city.CenterLongitude,
            city.FreeRadiusKm,
            city.SortOrder);

    internal static decimal CalculateDistanceKm(
        decimal originLatitude,
        decimal originLongitude,
        decimal targetLatitude,
        decimal targetLongitude)
    {
        const double earthRadiusKm = 6371;
        static double ToRadians(decimal degrees) => (double)degrees * Math.PI / 180;

        var latitudeDelta = ToRadians(targetLatitude - originLatitude);
        var longitudeDelta = ToRadians(targetLongitude - originLongitude);
        var originLatitudeRadians = ToRadians(originLatitude);
        var targetLatitudeRadians = ToRadians(targetLatitude);
        var a = Math.Sin(latitudeDelta / 2) * Math.Sin(latitudeDelta / 2)
            + Math.Cos(originLatitudeRadians) * Math.Cos(targetLatitudeRadians)
            * Math.Sin(longitudeDelta / 2) * Math.Sin(longitudeDelta / 2);
        var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
        return Math.Round((decimal)(earthRadiusKm * c), 3);
    }

    private static (double Latitude, double Longitude) MoveFromCenter(
        decimal centerLatitude,
        decimal centerLongitude,
        decimal targetLatitude,
        decimal targetLongitude,
        decimal distanceKm)
    {
        var latitudeDelta = (double)(targetLatitude - centerLatitude);
        var longitudeDelta = (double)(targetLongitude - centerLongitude);
        var bearing = Math.Atan2(longitudeDelta, latitudeDelta);
        var latitude = (double)centerLatitude + Math.Cos(bearing) * (double)distanceKm / 111.32;
        var longitudeScale = Math.Max(0.2, Math.Cos((double)centerLatitude * Math.PI / 180));
        var longitude = (double)centerLongitude
            + Math.Sin(bearing) * (double)distanceKm / (111.32 * longitudeScale);
        return (latitude, longitude);
    }

    private sealed record Candidate(Recommendation Recommendation, decimal DistanceKm);
}

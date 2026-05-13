using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TravelCompanion.Api.Data;
using TravelCompanion.Api.Models;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class RecommendationsController(TravelCompanionDbContext dbContext) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetRecommendations(
        [FromQuery] string? destinationSlug = null,
        [FromQuery] string? category = null,
        [FromQuery] decimal? latitude = null,
        [FromQuery] decimal? longitude = null,
        [FromQuery] int page = PaginationRequest.DefaultPage,
        [FromQuery] int pageSize = PaginationRequest.DefaultPageSize,
        CancellationToken cancellationToken = default)
    {
        if (!PaginationRequest.TryCreate(page, pageSize, out var pagination, out var error))
        {
            return this.ValidationError("pagination", error!);
        }

        var query = dbContext.Recommendations
            .AsNoTracking()
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(destinationSlug))
        {
            query = query.Where(recommendation => recommendation.Destination != null
                && recommendation.Destination.Slug == destinationSlug);
        }

        if (!string.IsNullOrWhiteSpace(category))
        {
            query = query.Where(recommendation => recommendation.Category == category);
        }

        var totalItems = await query.CountAsync(cancellationToken);
        var recommendations = await query.ToListAsync(cancellationToken);

        var response = recommendations
            .Select(recommendation => ToDto(recommendation, latitude, longitude))
            .OrderBy(recommendation => recommendation.DistanceKm ?? decimal.MaxValue)
            .ThenBy(recommendation => recommendation.Title)
            .Skip((pagination.Page - 1) * pagination.PageSize)
            .Take(pagination.PageSize)
            .ToList();

        var pagedResponse = response.ToPagedResult(pagination, totalItems);
        return HttpCache.OkOrNotModified(this, pagedResponse);
    }

    private static RecommendationDto ToDto(
        Recommendation recommendation,
        decimal? latitude,
        decimal? longitude)
    {
        decimal? distanceKm = latitude.HasValue && longitude.HasValue
            ? CalculateDistanceKm(latitude.Value, longitude.Value, recommendation.Latitude, recommendation.Longitude)
            : null;

        return new RecommendationDto(
            recommendation.Id,
            recommendation.DestinationId,
            recommendation.Title,
            recommendation.Category,
            recommendation.Neighborhood,
            recommendation.Description,
            recommendation.Latitude,
            recommendation.Longitude,
            recommendation.SuggestedDurationMinutes,
            recommendation.AccessLevel,
            distanceKm);
    }

    private static decimal CalculateDistanceKm(decimal originLatitude, decimal originLongitude, decimal targetLatitude, decimal targetLongitude)
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

        return Math.Round((decimal)(earthRadiusKm * c), 2);
    }
}

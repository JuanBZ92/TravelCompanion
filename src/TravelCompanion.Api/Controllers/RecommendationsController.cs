using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TravelCompanion.Api.Data;
using TravelCompanion.Api.Models;
using TravelCompanion.Api.Services;
using TravelCompanion.Shared;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class RecommendationsController(
    TravelCompanionDbContext dbContext,
    IRecommendationTagCatalogService tagCatalogService) : ControllerBase
{
    [HttpGet("tags")]
    public async Task<ActionResult<IReadOnlyList<RecommendationTagDto>>> GetTags(
        [FromQuery] string? destinationSlug = null,
        CancellationToken cancellationToken = default)
    {
        return Ok(await tagCatalogService.GetCatalogAsync(destinationSlug, cancellationToken));
    }

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
            .Include(recommendation => recommendation.Packages)
            .Where(recommendation =>
                recommendation.AccessLevel == ContentAccessLevel.Free
                && !recommendation.Packages.Any())
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
        var pagedRecommendations = await ApplyOrdering(query, latitude, longitude)
            .Skip((pagination.Page - 1) * pagination.PageSize)
            .Take(pagination.PageSize)
            .ToListAsync(cancellationToken);

        var response = pagedRecommendations
            .Select(recommendation => ToDto(recommendation, latitude, longitude))
            .ToList();

        var pagedResponse = response.ToPagedResult(pagination, totalItems);
        return HttpCache.OkOrNotModified(this, pagedResponse);
    }

    private static IQueryable<Recommendation> ApplyOrdering(
        IQueryable<Recommendation> query,
        decimal? latitude,
        decimal? longitude)
    {
        if (!latitude.HasValue || !longitude.HasValue)
        {
            return query.OrderBy(recommendation => recommendation.Title);
        }

        var originLatitude = latitude.Value;
        var originLongitude = longitude.Value;

        // Orden por cercania aproximada en DB para evitar cargar todo en memoria.
        // La distancia en km para UI se calcula luego solo para la pagina pedida.
        return query
            .OrderBy(recommendation =>
                (recommendation.Latitude - originLatitude) * (recommendation.Latitude - originLatitude)
                + (recommendation.Longitude - originLongitude) * (recommendation.Longitude - originLongitude))
            .ThenBy(recommendation => recommendation.Title);
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
            recommendation.Tags,
            recommendation.PriceLevel,
            recommendation.Latitude,
            recommendation.Longitude,
            recommendation.SuggestedDurationMinutes,
            recommendation.Rating,
            recommendation.OpeningHours,
            recommendation.AccessLevel,
            recommendation.Packages.Select(package => package.Id).ToList(),
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

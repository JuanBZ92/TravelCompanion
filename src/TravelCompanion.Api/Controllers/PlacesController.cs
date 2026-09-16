using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TravelCompanion.Api.Data;
using TravelCompanion.Api.Services;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Api.Controllers;

[ApiController]
[Route("api/mobile/places")]
public sealed class PlacesController(
    TravelCompanionDbContext dbContext,
    TravelerAccessService accessService,
    IGooglePlacesService googlePlacesService) : ControllerBase
{
    [HttpPost("search-page")]
    public async Task<ActionResult<PagedResultDto<RecommendationDto>>> SearchPage(PlaceSearchRequest request, CancellationToken cancellationToken, [FromQuery] int page = 1)
    {
        var result = await Search(request, cancellationToken);
        if (result.Result is not OkObjectResult { Value: IReadOnlyList<RecommendationDto> places }) return result.Result!;
        const int pageSize = 10;
        var totalPages = Math.Max(1, (int)Math.Ceiling(places.Count / (double)pageSize));
        page = Math.Clamp(page, 1, totalPages);
        return Ok(new PagedResultDto<RecommendationDto>(places.Skip((page - 1) * pageSize).Take(pageSize).ToList(),
            page, pageSize, places.Count, totalPages, page > 1, page < totalPages));
    }

    [HttpPost("autocomplete")]
    public async Task<ActionResult<IReadOnlyList<PlaceSuggestionDto>>> Autocomplete(PlaceAutocompleteRequest request, CancellationToken cancellationToken)
    {
        var access = await accessService.GetAsync(HttpContext, cancellationToken);
        if (access?.Capabilities.CanSearchGooglePlaces != true) return Forbid();
        if (string.IsNullOrWhiteSpace(request.Query) || request.Query.Trim().Length < 3) return Ok(Array.Empty<PlaceSuggestionDto>());
        if (request.Query.Length > 200 || request.City?.Length > 120 || !Guid.TryParse(request.SessionToken, out _)) return BadRequest();
        return Ok(await googlePlacesService.AutocompleteAsync(request, cancellationToken));
    }

    [HttpPost("details")]
    public async Task<ActionResult<RecommendationDto>> Details(PlaceDetailsRequest request, CancellationToken cancellationToken)
    {
        var access = await accessService.GetAsync(HttpContext, cancellationToken);
        if (access?.Capabilities.CanSearchGooglePlaces != true) return Forbid();
        if (string.IsNullOrWhiteSpace(request.PlaceId) || request.PlaceId.Length > 256 || !Guid.TryParse(request.SessionToken, out _)) return BadRequest();
        var destinationId = access.TripId.HasValue
            ? await dbContext.Trips.Where(t => t.Id == access.TripId).Select(t => t.DestinationId).SingleAsync(cancellationToken)
            : await dbContext.BuilderAccessGrants.Where(g => g.AppUserId == access.User.Id && g.RevokedAtUtc == null).Select(g => g.DestinationId).FirstAsync(cancellationToken);
        var place = await googlePlacesService.DetailsAsync(destinationId, request, cancellationToken);
        return place is null ? NotFound() : Ok(place);
    }

    [HttpPost("search")]
    public async Task<ActionResult<IReadOnlyList<RecommendationDto>>> Search(PlaceSearchRequest request, CancellationToken cancellationToken)
    {
        var access = await accessService.GetAsync(HttpContext, cancellationToken);
        if (access is null || !access.Capabilities.CanViewFullMap) return Forbid();
        if (string.IsNullOrWhiteSpace(request.Query) || request.Query.Trim().Length < 2) return Ok(Array.Empty<RecommendationDto>());

        var destinationId = access.TripId.HasValue
            ? await dbContext.Trips.Where(item => item.Id == access.TripId).Select(item => item.DestinationId).SingleAsync(cancellationToken)
            : await dbContext.BuilderAccessGrants.Where(item => item.AppUserId == access.User.Id && item.RevokedAtUtc == null).Select(item => item.DestinationId).FirstAsync(cancellationToken);
        var yukuEntities = await dbContext.Recommendations.AsNoTracking().Where(item => item.DestinationId == destinationId).ToListAsync(cancellationToken);
        var yuku = yukuEntities.Select(item => new { Item = item, Score = CatalogSearch.Score(item, request.Query) })
            .Where(candidate => candidate.Score > 0).OrderByDescending(candidate => candidate.Score)
            .ThenBy(candidate => request.Latitude.HasValue && request.Longitude.HasValue
                ? FreeMapPreviewService.CalculateDistanceKm(request.Latitude.Value, request.Longitude.Value, candidate.Item.Latitude, candidate.Item.Longitude) : 0)
            .ThenBy(candidate => candidate.Item.Title)
            .Select(candidate => RecommendationPresentation.ToDto(candidate.Item)).ToList();
        var google = access.Capabilities.CanSearchGooglePlaces
            ? await googlePlacesService.SearchAsync(destinationId, request, cancellationToken)
            : [];
        var catalogByPlaceId = yukuEntities.Where(item => !string.IsNullOrWhiteSpace(item.ProviderPlaceId))
            .ToDictionary(item => item.ProviderPlaceId!, StringComparer.Ordinal);
        var results = yuku.Concat(google.Select(item => item.ProviderPlaceId is not null && catalogByPlaceId.TryGetValue(item.ProviderPlaceId, out var curated)
            ? RecommendationPresentation.ToDto(curated) : item));
        return Ok(results.DistinctBy(item => item.SelectionKey).ToList());
    }
}

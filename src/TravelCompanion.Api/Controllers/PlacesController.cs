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
    [HttpPost("search")]
    public async Task<ActionResult<IReadOnlyList<RecommendationDto>>> Search(PlaceSearchRequest request, CancellationToken cancellationToken)
    {
        var access = await accessService.GetAsync(HttpContext, cancellationToken);
        if (access is null || !access.Capabilities.CanViewFullMap) return Forbid();
        if (string.IsNullOrWhiteSpace(request.Query) || request.Query.Trim().Length < 2) return Ok(Array.Empty<RecommendationDto>());

        var destinationId = access.TripId.HasValue
            ? await dbContext.Trips.Where(item => item.Id == access.TripId).Select(item => item.DestinationId).SingleAsync(cancellationToken)
            : await dbContext.BuilderAccessGrants.Where(item => item.AppUserId == access.User.Id && item.RevokedAtUtc == null).Select(item => item.DestinationId).FirstAsync(cancellationToken);
        var query = request.Query.Trim().ToLowerInvariant();
        var yukuEntities = await dbContext.Recommendations.AsNoTracking().Where(item => item.DestinationId == destinationId).ToListAsync(cancellationToken);
        var yuku = yukuEntities.Where(item => item.Title.Contains(query, StringComparison.OrdinalIgnoreCase)
                || item.Category.Contains(query, StringComparison.OrdinalIgnoreCase)
                || item.Tags.Any(tag => tag.Contains(query, StringComparison.OrdinalIgnoreCase)))
            .Take(20).Select(MobileController.ToRecommendationDtoForInternalUse).ToList();
        var google = access.Capabilities.CanSearchGooglePlaces
            ? await googlePlacesService.SearchAsync(destinationId, request, cancellationToken)
            : [];
        return Ok(yuku.Concat(google).Take(30).ToList());
    }
}

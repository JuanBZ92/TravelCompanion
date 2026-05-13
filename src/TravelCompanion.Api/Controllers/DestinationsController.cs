using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TravelCompanion.Api.Data;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class DestinationsController(TravelCompanionDbContext dbContext) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetDestinations(
        [FromQuery] int page = PaginationRequest.DefaultPage,
        [FromQuery] int pageSize = PaginationRequest.DefaultPageSize,
        CancellationToken cancellationToken = default)
    {
        if (!PaginationRequest.TryCreate(page, pageSize, out var pagination, out var error))
        {
            return this.ValidationError("pagination", error!);
        }

        var query = dbContext.Destinations
            .AsNoTracking()
            .OrderBy(destination => destination.Name)
            .Select(destination => new DestinationSummaryDto(
                destination.Id,
                destination.Name,
                destination.Slug,
                destination.Country,
                destination.HeroImageUrl,
                destination.ShortDescription));

        var response = await query.ToPagedResultAsync(pagination, cancellationToken);
        return HttpCache.OkOrNotModified(this, response);
    }
}

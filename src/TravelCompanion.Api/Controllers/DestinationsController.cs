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
    public async Task<ActionResult<IReadOnlyList<DestinationSummaryDto>>> GetDestinations()
    {
        var destinations = await dbContext.Destinations
            .AsNoTracking()
            .OrderBy(destination => destination.Name)
            .Select(destination => new DestinationSummaryDto(
                destination.Id,
                destination.Name,
                destination.Slug,
                destination.Country,
                destination.HeroImageUrl,
                destination.ShortDescription))
            .ToListAsync();

        return Ok(destinations);
    }
}

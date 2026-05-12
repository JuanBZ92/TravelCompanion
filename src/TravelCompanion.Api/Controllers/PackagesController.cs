using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TravelCompanion.Api.Data;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class PackagesController(TravelCompanionDbContext dbContext) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<TravelPackageDto>>> GetPackages(
        [FromQuery] string? destinationSlug = null)
    {
        var query = dbContext.TravelPackages.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(destinationSlug))
        {
            query = query.Where(package => package.Destination != null
                && package.Destination.Slug == destinationSlug);
        }

        var packages = await query
            .OrderBy(package => package.Price)
            .Select(package => new TravelPackageDto(
                package.Id,
                package.DestinationId,
                package.Name,
                package.Slug,
                package.Description,
                package.Price,
                package.Currency,
                package.IsSubscription))
            .ToListAsync();

        return Ok(packages);
    }
}

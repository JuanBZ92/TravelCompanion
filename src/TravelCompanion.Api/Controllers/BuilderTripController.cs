using Microsoft.AspNetCore.Mvc;
using TravelCompanion.Api.Services;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Api.Controllers;

[ApiController]
[Route("api/mobile/builder/setup")]
public sealed class BuilderTripController(BuilderTripService service) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<BuilderTripSetupDto>> Get(CancellationToken cancellationToken)
    {
        var result = await service.GetAsync(HttpContext, cancellationToken);
        return result is null ? Forbid() : Ok(result);
    }

    [HttpPut]
    public async Task<ActionResult<BuilderTripSetupDto>> Save(SaveBuilderTripSetupRequest request, CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await service.SaveAsync(HttpContext, request, cancellationToken));
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (BuilderRevisionConflictException exception)
        {
            return Conflict(new { message = exception.Message, currentRevision = exception.CurrentRevision });
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new { message = exception.Message });
        }
        catch (InvalidOperationException exception)
        {
            return BadRequest(new { message = exception.Message });
        }
    }
}

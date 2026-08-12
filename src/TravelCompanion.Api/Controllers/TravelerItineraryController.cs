using Microsoft.AspNetCore.Mvc;
using TravelCompanion.Api.Services;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Api.Controllers;

[ApiController]
[Route("api/mobile/itinerary")]
public sealed class TravelerItineraryController(TravelerItineraryService service) : ControllerBase
{
    [HttpPost]
    public Task<ActionResult<ItineraryItemMutationResponse>> Create(ItineraryItemMutationRequest request, CancellationToken cancellationToken) =>
        Execute(() => service.CreateAsync(HttpContext, request, cancellationToken));

    [HttpPatch("{id:guid}")]
    public Task<ActionResult<ItineraryItemMutationResponse>> Update(Guid id, ItineraryItemMutationRequest request, CancellationToken cancellationToken) =>
        Execute(() => service.UpdateAsync(HttpContext, id, request, cancellationToken));

    [HttpDelete("{id:guid}")]
    public Task<ActionResult<ItineraryItemMutationResponse>> Delete(Guid id, [FromQuery] int expectedRevision, CancellationToken cancellationToken) =>
        Execute(() => service.DeleteAsync(HttpContext, id, expectedRevision, cancellationToken));

    private async Task<ActionResult<ItineraryItemMutationResponse>> Execute(Func<Task<ItineraryItemMutationResponse>> action)
    {
        try
        {
            var result = await action();
            return result.HasOverlap ? Conflict(result) : Ok(result);
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (BuilderRevisionConflictException exception)
        {
            return Conflict(new ItineraryItemMutationResponse(false, exception.Message, exception.CurrentRevision));
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            return BadRequest(new { message = exception.Message });
        }
    }
}

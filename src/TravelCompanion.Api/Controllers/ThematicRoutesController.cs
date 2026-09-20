using Microsoft.AspNetCore.Mvc;
using TravelCompanion.Api.Services;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Api.Controllers;

[ApiController]
[RetiredPlanningFeature]
[Route("api/mobile/thematic-routes")]
public sealed class ThematicRoutesController(ThematicRouteService service) : ControllerBase
{
    [HttpGet] public Task<ActionResult<IReadOnlyList<ThematicRouteDto>>> List(CancellationToken ct) => Execute(() => service.ListAsync(HttpContext, ct));
    [HttpPost] public Task<ActionResult<ThematicRouteDto>> Create(CreateThematicRouteDto request, CancellationToken ct) => Execute(() => service.CreateAsync(HttpContext, request, ct));
    [HttpPut("{id:guid}")] public Task<ActionResult<ThematicRouteDto>> Update(Guid id, UpdateThematicRouteDto request, CancellationToken ct) => Execute(() => service.UpdateAsync(HttpContext, id, request, ct));
    [HttpPost("{id:guid}/copy")] public Task<ActionResult<ThematicRouteDto>> Copy(Guid id, CancellationToken ct) => Execute(() => service.CopyTemplateAsync(HttpContext, id, ct));
    [HttpPost("{id:guid}/prepare-application")] public Task<ActionResult<DayProposalDto>> Apply(Guid id, ApplyThematicRouteDto request, CancellationToken ct) => Execute(() => service.PrepareApplicationAsync(HttpContext, id, request, ct));
    [HttpDelete("{id:guid}")] public async Task<ActionResult> Delete(Guid id, [FromQuery] bool removeActivities,
        [FromQuery] int? expectedRevision, CancellationToken ct)
    {
        try { await service.DeleteAsync(HttpContext, id, removeActivities, expectedRevision, ct); return NoContent(); }
        catch (UnauthorizedAccessException) { return Unauthorized(); }
        catch (KeyNotFoundException) { return NotFound(); }
        catch (BuilderRevisionConflictException exception) { return Conflict(new { message = exception.Message, currentRevision = exception.CurrentRevision }); }
        catch (InvalidOperationException exception) { return BadRequest(new { message = exception.Message }); }
    }
    private async Task<ActionResult<T>> Execute<T>(Func<Task<T>> action)
    {
        try { return Ok(await action()); }
        catch (UnauthorizedAccessException) { return Unauthorized(); }
        catch (KeyNotFoundException) { return NotFound(); }
        catch (BuilderRevisionConflictException exception) { return Conflict(new { message = exception.Message, currentRevision = exception.CurrentRevision }); }
        catch (TrialUpgradeRequiredException exception) { return StatusCode(StatusCodes.Status402PaymentRequired, exception.Status); }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException) { return BadRequest(new { message = exception.Message }); }
    }
}

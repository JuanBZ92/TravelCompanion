using Microsoft.AspNetCore.Mvc;
using TravelCompanion.Api.Services;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Api.Controllers;

[ApiController]
[RetiredPlanningFeature]
[Route("api/mobile/proposals")]
public sealed class DayProposalsController(DayProposalService service) : ControllerBase
{
    [HttpPost]
    public Task<ActionResult<DayProposalDto>> Create(DayProposalRequestDto request, CancellationToken ct) =>
        Execute(() => service.CreateAsync(HttpContext, request, ct));
    [HttpGet("{id:guid}")]
    public Task<ActionResult<DayProposalDto>> Get(Guid id, CancellationToken ct) =>
        Execute(() => service.GetAsync(HttpContext, id, ct));
    [HttpPatch("{id:guid}")]
    public Task<ActionResult<DayProposalDto>> Revise(Guid id, ReviseDayProposalDto request, CancellationToken ct) =>
        Execute(() => service.ReviseAsync(HttpContext, id, request, ct));
    [HttpPost("{id:guid}/apply")]
    public Task<ActionResult<ItineraryChangeSetDto>> Apply(Guid id, ApplyDayProposalDto request, CancellationToken ct) =>
        Execute(() => service.ApplyAsync(HttpContext, id, request, ct));
    [HttpPost("operations/{id:guid}/undo")]
    public Task<ActionResult<ItineraryChangeSetDto>> Undo(Guid id, CancellationToken ct) =>
        Execute(() => service.UndoAsync(HttpContext, id, ct));

    private async Task<ActionResult<T>> Execute<T>(Func<Task<T>> action)
    {
        try { return Ok(await action()); }
        catch (UnauthorizedAccessException) { return Unauthorized(); }
        catch (KeyNotFoundException) { return NotFound(); }
        catch (BuilderRevisionConflictException exception) { return Conflict(new { message = exception.Message, currentRevision = exception.CurrentRevision }); }
        catch (TrialUpgradeRequiredException exception) { return StatusCode(StatusCodes.Status402PaymentRequired, exception.Status); }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException)
        { return BadRequest(new { message = exception.Message }); }
    }
}

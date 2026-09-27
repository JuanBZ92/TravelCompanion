using Microsoft.AspNetCore.Mvc;
using TravelCompanion.Api.Services;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Api.Controllers;

[ApiController]
[Route("api/mobile/trips/{tripId:guid}/journal")]
public sealed class JournalController(JournalService journal) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(Guid tripId, CancellationToken ct)
    {
        try { return Ok(await journal.ListAsync(HttpContext, tripId, ct)); }
        catch (UnauthorizedAccessException) { return Unauthorized(); }
    }

    [HttpPut("{activityId:guid}")]
    public async Task<IActionResult> Save(Guid tripId, Guid activityId, SaveJournalNoteRequest request, CancellationToken ct)
    {
        try
        {
            var result = await journal.SaveAsync(HttpContext, tripId, activityId, request, ct);
            return result.Saved ? Ok(result) : Conflict(result);
        }
        catch (UnauthorizedAccessException) { return Unauthorized(); }
        catch (KeyNotFoundException) { return NotFound(); }
        catch (ArgumentException ex) { return BadRequest(new { message = ex.Message }); }
    }
}

using Microsoft.AspNetCore.Mvc;
using TravelCompanion.Api.Services;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Api.Controllers;

[ApiController]
[Route("api/mobile/trips/{tripId:guid}/journal/free-entries")]
public sealed class JournalFreeEntriesController(JournalService journal) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(Guid tripId, CancellationToken ct)
    {
        try { return Ok(await journal.ListFreeAsync(HttpContext, tripId, ct)); }
        catch (UnauthorizedAccessException) { return Unauthorized(); }
    }

    [HttpPut("{id:guid}")]
    public Task<IActionResult> Save(Guid tripId, Guid id, SaveJournalFreeEntryRequest request, CancellationToken ct) =>
        Mutate(() => journal.SaveFreeAsync(HttpContext, tripId, id, request, ct));

    [HttpDelete("{id:guid}")]
    public Task<IActionResult> Delete(Guid tripId, Guid id, [FromBody] DeleteJournalFreeEntryRequest request, CancellationToken ct) =>
        Mutate(() => journal.DeleteFreeAsync(HttpContext, tripId, id, request, ct));

    private async Task<IActionResult> Mutate(Func<Task<JournalFreeSaveResult>> action)
    {
        try { var result = await action(); return result.Saved ? Ok(result) : Conflict(result); }
        catch (UnauthorizedAccessException) { return Unauthorized(); }
        catch (KeyNotFoundException) { return NotFound(); }
        catch (ArgumentException) { return BadRequest(new { message = "Invalid journal entry." }); }
    }
}

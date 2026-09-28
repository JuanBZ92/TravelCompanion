using Microsoft.AspNetCore.Mvc;
using TravelCompanion.Api.Services;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Api.Controllers;

[ApiController]
[Route("api/mobile/trips/{tripId:guid}/expenses")]
public sealed class ExpensesController(ExpenseService service) : ControllerBase
{
    private async Task<IActionResult> Run(Func<Task<IActionResult>> action)
    {
        try { return await action(); }
        catch (UnauthorizedAccessException) { return Unauthorized(); }
        catch (ExpensePremiumException) { return StatusCode(403, new { code = "expense_pass_required" }); }
        catch (ArgumentException exception) { return BadRequest(new { message = exception.Message }); }
    }
    [HttpGet]
    public Task<IActionResult> List(Guid tripId, CancellationToken ct) => Run(async () => Ok(await service.GetAsync(HttpContext, tripId, ct)));
    [HttpPut("{id:guid}")]
    public Task<IActionResult> Save(Guid tripId, Guid id, SaveExpenseRequest request, CancellationToken ct) => Run(async () =>
    {
        var result = await service.SaveAsync(HttpContext, tripId, id, request, ct);
        return result.Saved ? Ok(result) : Conflict(result);
    });
    [HttpPut("settings")]
    public Task<IActionResult> Settings(Guid tripId, SaveExpenseSettingsRequest request, CancellationToken ct) => Run(async () =>
    {
        var result = await service.SaveSettingsAsync(HttpContext, tripId, request, ct);
        return result is null ? Conflict() : Ok(result);
    });
    [HttpGet("rate")]
    public Task<IActionResult> Rate(Guid tripId, string currency, string target, DateOnly date, CancellationToken ct) => Run(async () =>
    {
        var rate = await service.RateAsync(HttpContext, tripId, currency, target, date, ct);
        return rate is null ? NoContent() : Ok(rate);
    });
    [HttpGet("breakdown")]
    public Task<IActionResult> Breakdown(Guid tripId, CancellationToken ct) => Run(async () => Ok(await service.BreakdownAsync(HttpContext, tripId, ct)));
    [HttpGet("export")]
    public Task<IActionResult> Export(Guid tripId, CancellationToken ct) => Run(async () => File(
        System.Text.Encoding.UTF8.GetPreamble().Concat(System.Text.Encoding.UTF8.GetBytes(await service.ExportAsync(HttpContext, tripId, ct))).ToArray(),
        "text/csv", "yuku-gastos.csv"));
}

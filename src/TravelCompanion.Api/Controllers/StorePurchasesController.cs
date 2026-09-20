using Microsoft.AspNetCore.Mvc;
using TravelCompanion.Api.Services;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Api.Controllers;

[ApiController]
[Route("api/mobile/purchases")]
public sealed class StorePurchasesController(StorePurchaseService service) : ControllerBase
{
    [HttpPost("intents")]
    public Task<ActionResult<PurchaseIntentDto>> Create(CreatePurchaseIntentDto request, CancellationToken ct) => Execute(() => service.CreateIntentAsync(HttpContext, request, ct));
    [HttpGet("intents/{intentId:guid}")]
    public Task<ActionResult<PurchaseIntentDto>> Get(Guid intentId, CancellationToken ct) => Execute(() => service.GetIntentAsync(HttpContext, intentId, ct));
    [HttpPost("intents/{intentId:guid}/cancel")]
    public Task<ActionResult<PurchaseIntentDto>> Cancel(Guid intentId, CancellationToken ct) => Execute(() => service.CancelIntentAsync(HttpContext, intentId, ct));
    [HttpPost("verify")]
    public Task<ActionResult<PassAccessDto>> Verify(VerifyPurchaseDto request, CancellationToken ct) => Execute(() => service.VerifyAsync(HttpContext, request, ct));
    [HttpPost("restore")]
    public async Task<ActionResult<IReadOnlyList<PassAccessDto>>> Restore(CancellationToken ct)
    {
        try { return Ok(await service.RestoreAsync(HttpContext, ct)); }
        catch (UnauthorizedAccessException) { return Unauthorized(); }
    }
    private async Task<ActionResult<T>> Execute<T>(Func<Task<T>> action)
    {
        try { return Ok(await action()); }
        catch (UnauthorizedAccessException) { return Unauthorized(); }
        catch (KeyNotFoundException) { return NotFound(); }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException)
        { return BadRequest(new { message = exception.Message }); }
    }
}

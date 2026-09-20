using Microsoft.AspNetCore.Mvc;
using TravelCompanion.Api.Services;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Api.Controllers;

[ApiController]
[Route("api/mobile/conversion")]
public sealed class ConversionController(
    ProductAnalyticsService analytics,
    PaywallOfferService paywall,
    UserSessionService sessions) : ControllerBase
{
    [HttpPost("events")]
    public async Task<ActionResult> Events(ProductAnalyticsBatchDto request, CancellationToken ct)
    {
        try { return Ok(new { accepted = await analytics.IngestAsync(HttpContext, request, sessions, ct) }); }
        catch (UnauthorizedAccessException) { return Unauthorized(); }
    }

    [HttpGet("paywall/{tripId:guid}")]
    public async Task<ActionResult<PaywallOfferDto>> Offer(Guid tripId, [FromQuery] PaywallEntryPoint entryPoint,
        [FromQuery] string platform, CancellationToken ct)
    {
        try { return Ok(await paywall.GetAsync(HttpContext, tripId, entryPoint, platform, ct)); }
        catch (UnauthorizedAccessException) { return Unauthorized(); }
        catch (KeyNotFoundException) { return NotFound(); }
    }
}

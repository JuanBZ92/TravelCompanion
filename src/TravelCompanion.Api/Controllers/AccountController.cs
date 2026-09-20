using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using TravelCompanion.Api.Services;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Api.Controllers;

[ApiController]
[Route("api/mobile/account")]
public sealed class AccountController(EmailAccountService service) : ControllerBase
{
    [HttpPost("email/code")]
    [EnableRateLimiting("EmailCode")]
    public async Task<ActionResult<EmailCodeRequestedDto>> RequestCode(RequestEmailCodeDto request, CancellationToken cancellationToken)
    {
        try { return Ok(await service.RequestCodeAsync(HttpContext, request, cancellationToken)); }
        catch (UnauthorizedAccessException) { return Unauthorized(); }
        catch (InvalidOperationException exception) { return BadRequest(new { message = exception.Message }); }
    }

    [HttpPost("email/verify")]
    [EnableRateLimiting("EmailCode")]
    public async Task<ActionResult<AuthSessionDto>> Verify(VerifyEmailCodeDto request, CancellationToken cancellationToken)
    {
        try { return Ok(await service.VerifyCodeAsync(HttpContext, request, cancellationToken)); }
        catch (UnauthorizedAccessException) { return Unauthorized(); }
        catch (InvalidOperationException exception) { return BadRequest(new { message = exception.Message }); }
    }

    [HttpGet]
    public async Task<ActionResult<TravelerAccountDto>> Get(CancellationToken cancellationToken)
    {
        try { return Ok(await service.GetAccountAsync(HttpContext, cancellationToken)); }
        catch (UnauthorizedAccessException) { return Unauthorized(); }
    }

    [HttpPut("analytics-consent")]
    public async Task<ActionResult> UpdateAnalyticsConsent(UpdateAnalyticsConsentDto request, CancellationToken cancellationToken)
    {
        try { await service.UpdateAnalyticsConsentAsync(HttpContext, request.Granted, cancellationToken); return NoContent(); }
        catch (UnauthorizedAccessException) { return Unauthorized(); }
    }

    [HttpPost("select-trip")]
    public async Task<ActionResult<AuthSessionDto>> SelectTrip(SelectAccountTripDto request, CancellationToken cancellationToken)
    {
        try { return Ok(await service.SelectTripAsync(HttpContext, request.TripId, cancellationToken)); }
        catch (UnauthorizedAccessException) { return Unauthorized(); }
        catch (KeyNotFoundException) { return NotFound(); }
    }

    [HttpPost("trips/{tripId:guid}/archive")]
    public async Task<ActionResult<AuthSessionDto>> ArchiveTrip(Guid tripId, CancellationToken cancellationToken)
    {
        try { return Ok(await service.ArchiveTripAsync(HttpContext, tripId, cancellationToken)); }
        catch (UnauthorizedAccessException) { return Unauthorized(); }
        catch (KeyNotFoundException) { return NotFound(); }
        catch (InvalidOperationException exception) { return Conflict(new { message = exception.Message }); }
    }

    [HttpDelete]
    public async Task<ActionResult> Delete(CancellationToken cancellationToken)
    {
        try { await service.DeleteAccountAsync(HttpContext, cancellationToken); return NoContent(); }
        catch (UnauthorizedAccessException) { return Unauthorized(); }
    }
}

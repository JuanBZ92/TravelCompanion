using Microsoft.AspNetCore.Mvc;
using TravelCompanion.Api.Services;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Api.Controllers;

[ApiController]
[Route("api/ai/day-plans")]
public sealed class DayPlansController(TravelerAccessService accessService, DayPlanService plans) : ControllerBase
{
    [HttpGet("options")]
    public async Task<ActionResult<DayPlanOptionsDto>> Options(CancellationToken ct)
    {
        var access = await accessService.GetAsync(HttpContext, ct);
        if (access is null) return Unauthorized();
        try { return Ok(await plans.OptionsAsync(access, ct)); }
        catch (DayPlanException exception) { return Error(exception, Request.Headers.AcceptLanguage.ToString()); }
    }

    [HttpPost]
    public async Task<ActionResult<DayPlanResponse>> Generate(DayPlanRequest request, CancellationToken ct)
    {
        var access = await accessService.GetAsync(HttpContext, ct);
        if (access is null) return Unauthorized();
        try { return Ok(await plans.GenerateAsync(access, request, ct)); }
        catch (DayPlanException exception) { return Error(exception, request.Locale); }
        catch (TrialUpgradeRequiredException) { return Error(new(403, "upgrade", "Activá el pase para continuar planificando."), request.Locale); }
    }

    [HttpPost("apply")]
    public async Task<ActionResult<DayPlanApplyResponse>> Apply(DayPlanApplyRequest request, CancellationToken ct)
    {
        var access = await accessService.GetAsync(HttpContext, ct);
        if (access is null) return Unauthorized();
        try { return Ok(await plans.ApplyAsync(access, request, ct)); }
        catch (DayPlanException exception) { return Error(exception, request.Locale); }
        catch (TrialUpgradeRequiredException) { return Error(new(403, "upgrade", "Activá el pase para guardar estos planes."), request.Locale); }
    }

    private ObjectResult Error(DayPlanException exception, string? locale)
    {
        var message = locale?.StartsWith("en", StringComparison.OrdinalIgnoreCase) == true ? exception.Code switch
        {
            "stale" => "Your itinerary changed. Refresh the trip before continuing.",
            "operation" => "This operation has already been used with different input, or it has expired. Try again.",
            "selection" => "Choose valid days, preferences and ideas within your trip.",
            "quota" => "Your planning allowance has been reached. Activate your pass or try again after the daily reset at 00:00 UTC.",
            "upgrade" => "Activate your pass to continue planning these days.",
            "access" => "Select your trip again to continue with its current access.",
            _ => exception.Message
        } : exception.Message;
        return StatusCode(exception.StatusCode, new DayPlanErrorDto(exception.Code, message));
    }
}

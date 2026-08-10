using Microsoft.AspNetCore.Mvc;
using TravelCompanion.Api.Services;
using TravelCompanion.Shared;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Api.Controllers;

[ApiController]
[Route("api/mobile/free-map")]
public sealed class FreeMapController(
    UserSessionService sessionService,
    FreeMapPreviewService previewService) : ControllerBase
{
    [HttpGet("cities")]
    public async Task<ActionResult<IReadOnlyList<FreeMapCityDto>>> GetCities(
        CancellationToken cancellationToken)
    {
        var authorization = await AuthorizePreviewAsync(cancellationToken);
        if (authorization is not null)
        {
            return authorization;
        }

        return Ok(await previewService.GetCitiesAsync(cancellationToken));
    }

    [HttpGet("{citySlug}")]
    public async Task<ActionResult<FreeMapPreviewDto>> GetCity(
        string citySlug,
        CancellationToken cancellationToken)
    {
        var authorization = await AuthorizePreviewAsync(cancellationToken);
        if (authorization is not null)
        {
            return authorization;
        }

        var preview = await previewService.GetCityAsync(citySlug, cancellationToken);
        return preview is null ? NotFound() : Ok(preview);
    }

    private async Task<ActionResult?> AuthorizePreviewAsync(CancellationToken cancellationToken)
    {
        var session = await sessionService.GetSessionContextAsync(HttpContext, cancellationToken);
        if (session is null)
        {
            return Unauthorized();
        }

        return session.AccessMode == SessionAccessMode.FreeMapPreview
            ? null
            : Forbid();
    }
}

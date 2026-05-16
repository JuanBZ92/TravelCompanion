using Microsoft.AspNetCore.Mvc;
using TravelCompanion.Api.Services;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Api.Controllers;

[ApiController]
[Route("api/ai")]
public sealed class AiController(
    UserSessionService sessionService,
    ITravelChatService travelChatService) : ControllerBase
{
    [HttpPost("travel-chat")]
    public async Task<ActionResult<TravelChatResponse>> TravelChat(
        [FromBody] TravelChatRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Message))
        {
            return this.ValidationError(nameof(request.Message), "Message is required.");
        }

        var user = await sessionService.GetUserAsync(HttpContext, cancellationToken);
        if (user is null)
        {
            return Unauthorized();
        }

        var response = await travelChatService.CreatePlanAsync(user, request, cancellationToken);
        return Ok(response);
    }
}

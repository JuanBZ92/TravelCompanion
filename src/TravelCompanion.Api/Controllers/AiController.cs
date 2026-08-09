using Microsoft.AspNetCore.Mvc;
using TravelCompanion.Api.Services;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Api.Controllers;

[ApiController]
[Route("api/ai")]
public sealed class AiController(
    UserSessionService sessionService,
    ITravelChatService travelChatService,
    IItineraryService itineraryService,
    ITravelAssistantFeedbackService feedbackService) : ControllerBase
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

    [HttpPost("save-itinerary-item")]
    [HttpPost("save_itinerary_item")]
    public async Task<ActionResult<SaveItineraryItemResponse>> SaveItineraryItem(
        [FromBody] SaveItineraryItemRequest request,
        CancellationToken cancellationToken)
    {
        if (request.RecommendationId == Guid.Empty)
        {
            return this.ValidationError(nameof(request.RecommendationId), "RecommendationId is required.");
        }

        var user = await sessionService.GetUserAsync(HttpContext, cancellationToken);
        if (user is null)
        {
            return Unauthorized();
        }

        var response = await itineraryService.SaveItineraryItemAsync(user, request, cancellationToken);
        return response.Saved ? Ok(response) : BadRequest(response);
    }

    [HttpPost("feedback")]
    public async Task<ActionResult<TravelAssistantFeedbackResponse>> Feedback(
        [FromBody] TravelAssistantFeedbackRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.ConversationId))
        {
            return this.ValidationError(nameof(request.ConversationId), "ConversationId is required.");
        }

        if (request.RecommendationId == Guid.Empty)
        {
            return this.ValidationError(nameof(request.RecommendationId), "RecommendationId is required.");
        }

        var user = await sessionService.GetUserAsync(HttpContext, cancellationToken);
        if (user is null)
        {
            return Unauthorized();
        }

        var response = await feedbackService.RecordAsync(user, request, cancellationToken);
        return response.Accepted ? Ok(response) : BadRequest(response);
    }
}

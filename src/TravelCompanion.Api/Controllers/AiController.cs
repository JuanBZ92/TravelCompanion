using Microsoft.AspNetCore.Mvc;
using TravelCompanion.Api.Services;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Api.Controllers;

[ApiController]
[Route("api/ai")]
public sealed class AiController(
    UserSessionService sessionService,
    TravelerAccessService accessService,
    ITravelChatIntentClassifier intentClassifier,
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

        var access = await accessService.GetAsync(HttpContext, cancellationToken);
        var intent = intentClassifier.Classify(request.Message);
        if (access?.ExperienceMode == ExperienceMode.SelfServiceBuilder
            && access.Capabilities.RequiresTripSetup
            && (intent.IsPlanning || intent.Intent == TravelChatIntents.SaveItinerary))
        {
            return Ok(new TravelChatResponse(
                request.ConversationId ?? Guid.NewGuid().ToString("N"),
                "Primero necesito las fechas y ciudades de tu viaje para ubicar el plan.",
                intent.Intent,
                [],
                ["Configurar mi viaje"],
                new MissingContextDto("tripSetup", "Configura las fechas y ciudades de tu viaje para continuar.", ["Configurar mi viaje"])));
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

        var access = await accessService.GetAsync(HttpContext, cancellationToken);
        if (access is null || !access.Capabilities.CanEditItinerary || access.Capabilities.RequiresTripSetup)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new SaveItineraryItemResponse(false, "Este tipo de viaje no permite editar el itinerario desde la app.", null));
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

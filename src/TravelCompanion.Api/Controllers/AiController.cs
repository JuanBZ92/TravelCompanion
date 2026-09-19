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
    ITravelAssistantFeedbackService feedbackService,
    FreeTrialAccessService freeTrialAccessService) : ControllerBase
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

        TrialAccessStatusDto? trialStatus = null;
        if (access?.Session.AccessMode == TravelCompanion.Shared.SessionAccessMode.FreeMapPreview)
        {
            try
            {
                trialStatus = await freeTrialAccessService.RequireAssistantQuotaAsync(user.Id, cancellationToken);
            }
            catch (TrialUpgradeRequiredException exception)
            {
                return Ok(new TravelChatResponse(
                    request.ConversationId ?? Guid.NewGuid().ToString("N"),
                    "Ya probaste las 3 consultas gratuitas. Activa tu pase para seguir planificando con YUKU.",
                    "upgrade_required",
                    [],
                    ["Activar mi pase"],
                    new MissingContextDto("upgrade", "Activa tu pase para continuar con el asistente.", ["Activar mi pase"]),
                    TrialAccess: exception.Status));
            }
        }

        var response = await travelChatService.CreatePlanAsync(user, request, cancellationToken);
        if (trialStatus is not null && response.Cards.Count > 0)
        {
            trialStatus = await freeTrialAccessService.RecordSuccessfulAssistantRequestAsync(user.Id, cancellationToken);
        }
        return Ok(response with { TrialAccess = trialStatus });
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
        if (access?.Session.AccessMode == TravelCompanion.Shared.SessionAccessMode.FreeMapPreview)
        {
            try
            {
                await freeTrialAccessService.RequireEditingAsync(user.Id, startIfNeeded: false, cancellationToken);
            }
            catch (TrialUpgradeRequiredException exception)
            {
                return StatusCode(StatusCodes.Status402PaymentRequired,
                    new SaveItineraryItemResponse(false, exception.Message, null));
            }
        }
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

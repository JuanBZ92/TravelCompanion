using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using TravelCompanion.Api.Options;
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
    FreeTrialAccessService freeTrialAccessService,
    AssistantUsageService assistantUsageService,
    ProductAnalyticsService analytics,
    IUserProfileService profiles,
    IOptions<ProductFeatureOptions> features) : ControllerBase
{
    [HttpGet("day-personalization")]
    public async Task<ActionResult<DayPersonalizationOptionsDto>> DayPersonalization(CancellationToken cancellationToken)
    {
        var user = await sessionService.GetUserAsync(HttpContext, cancellationToken);
        if (user is null) return Unauthorized();
        var access = await accessService.GetAsync(HttpContext, cancellationToken);
        var trial = access?.Session.AccessMode == TravelCompanion.Shared.SessionAccessMode.FreeMapPreview
            ? await freeTrialAccessService.GetStatusAsync(user.Id, cancellationToken) : null;
        return Ok(new DayPersonalizationOptionsDto(
            features.Value.PersonalizedDayEnabled && access?.Session.AccessMode is
                TravelCompanion.Shared.SessionAccessMode.Builder or TravelCompanion.Shared.SessionAccessMode.FreeMapPreview,
            trial?.PersonalizedDayTrialAvailable == true,
            await profiles.GetProfileDtoAsync(user.Id, cancellationToken)));
    }

    [HttpPost("travel-chat")]
    public async Task<ActionResult<TravelChatResponse>> TravelChat(
        [FromBody] TravelChatRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Message))
        {
            return this.ValidationError(nameof(request.Message), "Message is required.");
        }

        if (request.GuidedAction?.Action == TravelCompanion.Shared.GuidedTravelActions.FullDay && request.Date is null)
            return this.ValidationError(nameof(request.Date), "Select a day to improve.");

        var user = await sessionService.GetUserAsync(HttpContext, cancellationToken);
        if (user is null)
        {
            return Unauthorized();
        }

        var access = await accessService.GetAsync(HttpContext, cancellationToken);
        var personalized = request.GuidedAction?.PlanningMode == "personalized";
        if (request.GuidedAction?.PlanningMode is not (null or "personalized")
            || personalized && request.GuidedAction?.Action != TravelCompanion.Shared.GuidedTravelActions.FullDay)
            return this.ValidationError(nameof(request.GuidedAction), "Invalid day planning mode.");
        if (personalized && !features.Value.PersonalizedDayEnabled)
            return Ok(new TravelChatResponse(request.ConversationId ?? Guid.NewGuid().ToString("N"),
                "La personalización todavía no está disponible.", "unavailable", [], [],
                new MissingContextDto("unavailable", "La personalización todavía no está disponible.", [])));
        if (personalized && access?.TripId != request.GuidedAction?.TripId)
            return Ok(new TravelChatResponse(request.ConversationId ?? Guid.NewGuid().ToString("N"),
                "El viaje cambió. Actualizá el itinerario y generá otra propuesta.", "stale", [], [],
                new MissingContextDto("stale", "El viaje cambió. Actualizá el itinerario y generá otra propuesta.", [])));
        if (request.GuidedAction?.AdaptationReason is not null
            && request.GuidedAction.Action != TravelCompanion.Shared.GuidedTravelActions.FullDay)
            return this.ValidationError(nameof(request.GuidedAction), "Adaptation requires a full-day action.");
        if (request.GuidedAction?.AdaptationReason is not null
            && access?.Session.AccessMode != TravelCompanion.Shared.SessionAccessMode.Builder)
            return Ok(new TravelChatResponse(request.ConversationId ?? Guid.NewGuid().ToString("N"),
                "Activa el Pase Japón para adaptar tu día.", "upgrade_required", [], ["Activar mi pase"],
                new MissingContextDto("upgrade", "La adaptación del día requiere el pase.", ["Activar mi pase"])));
        var intent = intentClassifier.Classify(request.Message);
        if (access?.Session.AccessMode == TravelCompanion.Shared.SessionAccessMode.BuilderReadOnly)
        {
            return Ok(new TravelChatResponse(
                request.ConversationId ?? Guid.NewGuid().ToString("N"),
                "Tu itinerario sigue disponible para consulta. Activa un pase para volver a usar el Assistant.",
                "upgrade_required", [], ["Activar mi pase"],
                new MissingContextDto("upgrade", "Este viaje está en modo de solo lectura.", ["Activar mi pase"])));
        }
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

        TrialAccessStatusDto? trialStatus = access?.Session.AccessMode == TravelCompanion.Shared.SessionAccessMode.FreeMapPreview
            ? await freeTrialAccessService.GetStatusAsync(user.Id, cancellationToken) : null;
        AssistantUsageLeaseResult? usageLease = null;
        if (access?.Session.AccessMode is TravelCompanion.Shared.SessionAccessMode.Builder or TravelCompanion.Shared.SessionAccessMode.FreeMapPreview)
        {
            try
            {
                if (access.Session.AccessMode == TravelCompanion.Shared.SessionAccessMode.FreeMapPreview)
                {
                    if (request.Date is { } date)
                        await freeTrialAccessService.RequirePlanningDateAsync(user.Id, access.TripId, date, cancellationToken);
                    if (request.GuidedAction?.Action == TravelCompanion.Shared.GuidedTravelActions.FullDay)
                        await freeTrialAccessService.RequireEditingAsync(user.Id, false, cancellationToken);
                }
                // Bind retry identity to the complete input: reusing an id for different requests must not bypass quota.
                var fingerprint = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(request)))[..24];
                var operationId = request.OperationId ?? Guid.NewGuid();
                usageLease = await assistantUsageService.ReserveAsync(user.Id, access.TripId,
                    request.GuidedAction?.Action == TravelCompanion.Shared.GuidedTravelActions.FullDay
                        ? $"{(personalized ? AssistantUsageService.PersonalizedDayPrefix : AssistantUsageService.FullDayPrefix)}{operationId:N}:{fingerprint}"
                        : $"chat:{operationId:N}:{fingerprint}", cancellationToken);
            }
            catch (TrialUpgradeRequiredException exception)
            {
                var paidDailyLimit = access.Session.AccessMode == TravelCompanion.Shared.SessionAccessMode.Builder;
                return Ok(new TravelChatResponse(
                    request.ConversationId ?? Guid.NewGuid().ToString("N"),
                    paidDailyLimit
                        ? "Has alcanzado el límite diario del Assistant. Se reinicia a las 00:00 UTC."
                        : personalized
                        ? "La prueba personalizada ya se utilizó o no quedan mejoras del día. Activá el Pase Japón para continuar."
                        : access.Session.AccessMode == TravelCompanion.Shared.SessionAccessMode.FreeMapPreview
                        ? "Activa tu pase para continuar. La prueba incluye tres días, tres mejoras del día y tres consultas del Assistant, con 30 minutos de edición."
                        : "No quedan consultas disponibles por ahora.",
                    paidDailyLimit ? "daily_limit" : "upgrade_required", [],
                    paidDailyLimit ? [] : ["Activar mi pase"],
                    new MissingContextDto(paidDailyLimit ? "daily_limit" : "upgrade",
                        paidDailyLimit ? "Tu cuota diaria se renovará a las 00:00 UTC." : "No quedan consultas disponibles por ahora.",
                        paidDailyLimit ? [] : ["Activar mi pase"]),
                    TrialAccess: trialStatus is null ? exception.Status : await freeTrialAccessService.GetStatusAsync(user.Id, cancellationToken)));
            }
        }

        TravelChatResponse response;
        try
        {
            response = await travelChatService.CreatePlanAsync(user, request, cancellationToken);
            if (usageLease is not null && response.Cards.Count > 0 && response.Intent != "day_complete")
            {
                await assistantUsageService.CompleteAsync(usageLease.LeaseId, cancellationToken);
                await analytics.RecordServerEventAsync(user.Id, access?.TripId, "first_useful_response",
                    "assistant", null, cancellationToken);
                if (trialStatus is not null) trialStatus = await freeTrialAccessService.GetStatusAsync(user.Id, cancellationToken);
            }
            else if (usageLease is not null) await assistantUsageService.CancelAsync(usageLease.LeaseId, cancellationToken);
        }
        catch
        {
            if (usageLease is not null) await assistantUsageService.CancelAsync(usageLease.LeaseId, cancellationToken);
            throw;
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

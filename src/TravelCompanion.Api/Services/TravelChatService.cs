using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TravelCompanion.Api.Data;
using TravelCompanion.Api.Models;
using TravelCompanion.Api.Options;
using TravelCompanion.Shared;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Api.Services;

public sealed class TravelChatService(
    TravelCompanionDbContext dbContext,
    IUserProfileService userProfileService,
    ITravelAssistantActionPlanner actionPlanner,
    ITravelAssistantTextProvider textProvider,
    ITravelAssistantConversationStateService conversationStateService,
    ITravelChatResponseComposer responseComposer,
    ITravelRecommendationPlanningService recommendationPlanningService,
    ITravelAiModelClient modelClient,
    IOptions<OpenAiTravelOptions> openAiOptions,
    TravelAssistantTelemetry telemetry,
    ILogger<TravelChatService> logger) : ITravelChatService
{
    private const string Intent = "plan_between_reservations";
    private const string ViewScheduleIntent = "view_schedule";
    private const string ViewPreferencesIntent = "view_preferences";
    private const string UpdatePreferencesIntent = "update_preferences";
    private const string LessWalkingMode = "less_walking";
    private const string ShorterMode = "shorter";
    private const string FoodMode = "food";
    private const string FoodBreakfastMode = "food_breakfast";
    private const string FoodLunchMode = "food_lunch";
    private const string FoodDinnerMode = "food_dinner";
    private const string FoodBrunchMode = "food_brunch";
    private const string CultureMode = "culture";
    private const string CultureMuseumMode = "culture_museum";
    private const string CultureTempleMode = "culture_temple";
    private const string CultureArtMode = "culture_art";
    private const string CultureHistoryMode = "culture_history";
    private const string NatureMode = "nature";
    private const string NatureGardenMode = "nature_garden";
    private const string NatureParkMode = "nature_park";
    private const string NatureCoastMode = "nature_coast";
    private const string NatureOnsenMode = "nature_onsen";
    private const string ShoppingMode = "shopping";
    private const string ShoppingMarketMode = "shopping_market";
    private const string ShoppingVintageMode = "shopping_vintage";
    private const string ShoppingSouvenirMode = "shopping_souvenir";
    private const string ViewpointMode = "viewpoint";
    private const string ViewpointSunsetMode = "viewpoint_sunset";
    private const string ViewpointPhotoMode = "viewpoint_photo";
    private const string NightlifeMode = "nightlife";
    private const string NightlifeBarMode = "nightlife_bar";
    private const string NightlifeKaraokeMode = "nightlife_karaoke";
    private const string NightlifeLiveMusicMode = "nightlife_live_music";
    private const string DanceMode = "dance";
    private const string NeighborhoodMode = "neighborhood";
    private const string CheaperMode = "cheaper";
    private const string MediumCostMode = "medium_cost";
    private const string HighCostMode = "high_cost";
    private const string BalancedMode = "balanced";

    public async Task<TravelChatResponse> CreatePlanAsync(
        AppUser user,
        TravelChatRequest request,
        CancellationToken cancellationToken)
    {
        var locale = textProvider.NormalizeLocale(request.Locale);
        var promptVersion = string.IsNullOrWhiteSpace(openAiOptions.Value.PromptVersion)
            ? "travel-chat.v1"
            : openAiOptions.Value.PromptVersion.Trim();
        using var chatTiming = telemetry.StartChatRequest(locale, promptVersion);
        var conversationId = string.IsNullOrWhiteSpace(request.ConversationId)
            ? Guid.NewGuid().ToString("N")
            : request.ConversationId.Trim();
        var conversation = await conversationStateService.LoadAsync(conversationId, user.Id, cancellationToken);
        if (conversation is not null && conversation.UserId != user.Id)
        {
            conversationId = Guid.NewGuid().ToString("N");
            conversation = null;
        }

        var conversationState = conversationStateService.ReadState(conversation);
        var guidedCriteria = NormalizeGuidedCriteria(request.Criteria ?? conversationState.GuidedCriteria);
        var isFullDayRequest = request.GuidedAction?.Action == GuidedTravelActions.FullDay;
        var isGuidedRequest = request.GuidedAction?.Action is GuidedTravelActions.Recommend
                or GuidedTravelActions.Alternative
                or GuidedTravelActions.FullDay
            && guidedCriteria is not null;
        var isAlternativeRequest = request.GuidedAction?.Action == GuidedTravelActions.Alternative
            || IsAlternativeRequest(request.Message);
        var targetedReplacementId = isGuidedRequest
            && isAlternativeRequest
            && Guid.TryParse(request.GuidedAction?.RecommendationId, out var replacementId)
                ? replacementId.ToString()
                : null;
        var isTargetedReplacement = targetedReplacementId is not null;
        if (isGuidedRequest)
        {
            request = request with
            {
                Message = isFullDayRequest
                    ? CreateFullDayPlanningMessage(locale)
                    : CreateGuidedPlanningMessage(guidedCriteria!, isAlternativeRequest, locale),
                Criteria = guidedCriteria
            };
        }

        var actionPlan = await actionPlanner.CreateAsync(request, conversation, cancellationToken);
        TravelPreferenceProfilePatchDto? temporaryPreferencePatch = null;

        if (actionPlan.ShouldApplyPendingPreference && actionPlan.PendingPreferencePatch is not null)
        {
            var updatedProfile = await ApplyPreferencePatchAsync(
                user.Id,
                actionPlan.PendingPreferencePatch,
                cancellationToken);
            await conversationStateService.ClearPendingPreferencePatchAsync(conversation, cancellationToken);

            if (!actionPlan.Intent.IsPlanning)
            {
                return TrackOutcome(new TravelChatResponse(
                    conversationId,
                    textProvider.PreferenceConfirmedMessage(updatedProfile, locale),
                    UpdatePreferencesIntent,
                    [],
                    textProvider.PreferenceAfterChangeReplies(locale),
                    null),
                    eventName: "preference_confirmed",
                    locale: locale,
                    promptVersion: promptVersion);
            }

            request = request with { Message = actionPlan.MessageForExecution, ConversationId = conversationId };
        }
        else if (actionPlan.ShouldRejectPendingPreference)
        {
            await conversationStateService.ClearPendingPreferencePatchAsync(conversation, cancellationToken);

            if (!actionPlan.Intent.IsPlanning)
            {
                return TrackOutcome(new TravelChatResponse(
                    conversationId,
                    textProvider.PreferenceRejectedMessage(locale),
                    ViewPreferencesIntent,
                    [],
                    textProvider.PreferenceAfterChangeReplies(locale),
                    null),
                    eventName: "preference_rejected",
                    locale: locale,
                    promptVersion: promptVersion);
            }

            request = request with { Message = actionPlan.MessageForExecution, ConversationId = conversationId };
            temporaryPreferencePatch = actionPlan.TemporaryPreferencePatch;
        }
        else
        {
            request = request with { Message = actionPlan.MessageForExecution, ConversationId = conversationId };
        }

        var intent = actionPlan.Intent;
        LogIntentClassification(intent, request.Message);

        if (intent.Intent == TravelChatIntents.SaveItinerary)
        {
            return TrackOutcome(responseComposer.MissingContext(
                conversationId,
                "confirmation",
                textProvider.SaveRequiresConfirmationMessage(locale),
                textProvider.SaveRequiresConfirmationReplies(locale)),
                eventName: "save_requires_confirmation",
                locale: locale,
                promptVersion: promptVersion);
        }

        var date = actionPlan.Date;

        if (intent.Intent == TravelChatIntents.Help)
        {
            return TrackOutcome(
                responseComposer.CreateHelpResponse(conversationId, locale),
                eventName: "help",
                locale: locale,
                promptVersion: promptVersion);
        }

        if (intent.Intent == TravelChatIntents.ViewSchedule)
        {
            return await CreateScheduleResponseAsync(
                user,
                conversationId,
                date,
                locale,
                promptVersion,
                cancellationToken);
        }

        if (!actionPlan.SuppressPreferenceConfirmation && intent.Intent == TravelChatIntents.ViewPreferences)
        {
            if (actionPlan.RequestedPreferencePatch is not null)
            {
                return await CreatePreferenceConfirmationResponseAsync(
                    conversation,
                    conversationId,
                    user.Id,
                    request.Message,
                    actionPlan.RequestedPreferencePatch,
                    locale,
                    promptVersion,
                    cancellationToken);
            }

            return await CreatePreferenceResponseAsync(
                user.Id,
                conversationId,
                locale,
                promptVersion,
                cancellationToken);
        }

        if (!intent.IsSupported)
        {
            return TrackOutcome(responseComposer.MissingContext(
                conversationId,
                "assistantCommand",
                textProvider.UnsupportedMessage(locale),
                textProvider.UnsupportedReplies(locale)),
                eventName: "unsupported_command",
                locale: locale,
                promptVersion: promptVersion);
        }

        var preferences = await userProfileService.GetProfileAsync(user.Id, cancellationToken);
        var effectivePreferences = temporaryPreferencePatch is null
            ? preferences
            : await CreateEffectivePreferenceProfileAsync(user, preferences, temporaryPreferencePatch, cancellationToken);

        if (!isGuidedRequest
            && !userProfileService.HasMinimumPreferences(effectivePreferences, out var missingPreferenceFields))
        {
            return TrackOutcome(responseComposer.MissingContext(
                conversationId,
                "preferences",
                textProvider.MinimumPreferencesMissingMessage(locale),
                responseComposer.CreatePreferenceSuggestions(missingPreferenceFields, locale)),
                eventName: "missing_context",
                locale: locale,
                promptVersion: promptVersion);
        }

        if (!intent.IsPlanning)
        {
            return TrackOutcome(responseComposer.MissingContext(
                conversationId,
                "assistantCommand",
                textProvider.UnsupportedMessage(locale),
                textProvider.UnsupportedReplies(locale)),
                eventName: "unsupported_command",
                locale: locale,
                promptVersion: promptVersion);
        }

        var trips = await dbContext.Trips
            .AsNoTracking()
            .Include(trip => trip.Destination)
            .Include(trip => trip.Reservations)
            .Where(trip =>
                trip.PublicationStatus == TripPublicationStatus.Published
                && trip.AppUserId == user.Id
                && trip.StartsOn <= date
                && trip.EndsOn >= date)
            .ToListAsync(cancellationToken);

        if (trips.Count == 0)
        {
            return TrackOutcome(responseComposer.MissingContext(
                conversationId,
                "date",
                textProvider.NoActiveTripMessage(date, locale),
                textProvider.NoActiveTripReplies(locale)),
                eventName: "missing_context",
                locale: locale,
                promptVersion: promptVersion);
        }

        var reservations = trips
            .SelectMany(trip => trip.Reservations)
            .Where(reservation => IsReservationOnDate(reservation, date))
            .OrderBy(reservation => GetStartForDate(reservation, date))
            .ToList();

        var city = ResolveCity(request.City ?? conversation?.LastCity, reservations, trips);
        var planningWindow = FindPlanningWindow(reservations, date);
        if (planningWindow is null)
        {
            return TrackOutcome(new TravelChatResponse(
                conversationId,
                textProvider.NoPlanningWindowMessage(date, locale),
                Intent,
                [],
                textProvider.NoPlanningWindowReplies(locale),
                null),
                eventName: "no_planning_window",
                locale: locale,
                promptVersion: promptVersion);
        }

        var responseMode = isFullDayRequest
            ? BalancedMode
            : isGuidedRequest
            ? ResolveGuidedResponseMode(guidedCriteria!.Category)
            : isAlternativeRequest
            ? string.IsNullOrWhiteSpace(conversationState.LastResponseMode) ? BalancedMode : conversationState.LastResponseMode
            : actionPlan.ResponseMode;
        var explicitRecommendationIds = ParseRecommendationIds(request.Message);
        if (targetedReplacementId is not null)
        {
            explicitRecommendationIds.Add(targetedReplacementId);
        }
        var samePlanningContext = conversationState.LastDate == date
            && string.Equals(conversationState.LastCity, city, StringComparison.OrdinalIgnoreCase);
        HashSet<string> previousRecommendationIds = isAlternativeRequest && samePlanningContext
            ? conversationState.LastRecommendationIds.ToHashSet(StringComparer.OrdinalIgnoreCase)
            : [];
        var excludedRecommendationIds = previousRecommendationIds
            .Concat(explicitRecommendationIds)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (isFullDayRequest)
        {
            excludedRecommendationIds.UnionWith(trips
                .SelectMany(trip => trip.Reservations)
                .Where(item => item.RecommendationId.HasValue)
                .Select(item => item.RecommendationId!.Value.ToString()));
        }
        var profile = CreateProfile(user, effectivePreferences, responseMode, request.Message, guidedCriteria);
        ApplyHiddenConversationTags(profile, conversationState.HiddenTags);
        if (isGuidedRequest
            && guidedCriteria!.MaxWalkingMinutes.HasValue
            && request.CurrentLocation is null)
        {
            conversationState.GuidedCriteria = guidedCriteria;
            conversationState.LastCity = city;
            conversationState.LastDate = date;
            await conversationStateService.SavePlanningStateAsync(
                conversation,
                conversationId,
                user.Id,
                conversationState,
                cancellationToken);

            return TrackOutcome(new TravelChatResponse(
                conversationId,
                IsEnglish(locale)
                    ? "I need your current location to prioritize walking distance."
                    : "Necesito tu ubicación actual para priorizar la distancia a pie.",
                Intent,
                [],
                [],
                null,
                CreateLocationQuestion(locale),
                guidedCriteria),
                responseMode,
                eventName: "guided_location_required",
                locale: locale,
                promptVersion: promptVersion);
        }

        var context = new TravelPlanningContext(
            city,
            date,
            planningWindow.Value.Start,
            planningWindow.Value.End,
            planningWindow.Value.AvailableMinutes,
            request.CurrentLocation);

        var planningResult = await recommendationPlanningService.RankAsync(
            user,
            trips.Select(trip => trip.DestinationId).Distinct().ToList(),
            city,
            profile,
            reservations,
            context,
            responseMode,
            isFullDayRequest ? guidedCriteria! with { Category = null, Categories = [] } : guidedCriteria,
            excludedRecommendationIds,
            cancellationToken);

        if (planningResult.UnlockedRecommendationCount == 0)
        {
            return TrackOutcome(responseComposer.MissingContext(
                conversationId,
                "city",
                textProvider.NoRecommendationsMessage(city, locale),
                textProvider.NoRecommendationsReplies(locale)),
                eventName: "missing_context",
                locale: locale,
                promptVersion: promptVersion);
        }

        if (isGuidedRequest && planningResult.RankedRecommendations.Count == 0)
        {
            conversationState.GuidedCriteria = guidedCriteria;
            conversationState.LastCity = city;
            conversationState.LastDate = date;
            await conversationStateService.SavePlanningStateAsync(
                conversation,
                conversationId,
                user.Id,
                conversationState,
                cancellationToken);

            return TrackOutcome(new TravelChatResponse(
                conversationId,
                IsEnglish(locale)
                    ? "I couldn't find another option with those filters. Adjust one criterion to broaden the search."
                    : "No encontré otra opción con esos filtros. Ajustá un criterio para ampliar la búsqueda.",
                Intent,
                [],
                [],
                null,
                CreateAdjustQuestion(locale),
                guidedCriteria),
                responseMode,
                eventName: "guided_no_results",
                locale: locale,
                promptVersion: promptVersion);
        }

        var fullDayStops = isFullDayRequest
            ? SelectFullDayStops(
                planningResult.RankedRecommendations,
                guidedCriteria!,
                request.GuidedAction?.OptionId ?? conversationId,
                locale)
            : [];
        var ranked = isFullDayRequest
            ? fullDayStops.Select(stop => stop.Recommendation).ToList()
            : planningResult.RankedRecommendations
                .Take(isTargetedReplacement ? 1 : isGuidedRequest ? 2 : 3)
                .ToList();
        var cards = isFullDayRequest
            ? fullDayStops.Select(stop =>
            {
                var cardContext = context with
                {
                    WindowStart = stop.StartsAt,
                    WindowEnd = stop.StartsAt.AddMinutes(stop.Recommendation.Recommendation.SuggestedDurationMinutes),
                    AvailableMinutes = stop.Recommendation.Recommendation.SuggestedDurationMinutes
                };
                var card = responseComposer.ToRecommendationCard(stop.Recommendation, cardContext);
                return card with
                {
                    Subtitle = $"{stop.Label} · {card.Subtitle}",
                    Description = RecommendationPresentation.ToDto(
                        stop.Recommendation.Recommendation,
                        locale: locale).DisplayDescription
                };
            }).ToList()
            : ranked.Select(scored => responseComposer.ToRecommendationCard(scored, context) with
            {
                Description = RecommendationPresentation.ToDto(scored.Recommendation, locale: locale).DisplayDescription
            }).ToList();
        var defaultSuggestedReplies = responseComposer.CreateSuggestedReplies(responseMode, locale);
        var defaultMessage = isFullDayRequest
            ? CreateFullDayResponseMessage(cards.Count, locale)
            : responseComposer.CreateAssistantMessage(city, planningWindow.Value, ranked, responseMode, locale);
        var modelResult = isFullDayRequest
            ? null
            : await CreateModelResponseAsync(
                conversationId,
                request,
                profile,
                context,
                reservations,
                cards,
                defaultSuggestedReplies,
                promptVersion,
                cancellationToken);

        var useModelResponse = !isFullDayRequest
            && responseMode == BalancedMode
            && modelResult is not null
            && !string.IsNullOrWhiteSpace(modelResult.Message)
            && !MentionsSavedState(modelResult.Message);
        var topRecommendation = ranked.FirstOrDefault()?.Recommendation;
        var diagnostics = new TravelAssistantDiagnostics(
            planningResult.UnlockedRecommendationCount,
            planningResult.RankedCandidateCount,
            planningResult.DislikedFilteredCandidateCount,
            planningResult.ExcludedRecommendationCount,
            ranked.Count,
            topRecommendation?.Id.ToString(),
            topRecommendation?.Title);

        conversationState.LastIntent = Intent;
        conversationState.LastLocale = locale;
        conversationState.LastResponseMode = responseMode;
        conversationState.LastCity = city;
        conversationState.LastDate = date;
        conversationState.PromptVersion = promptVersion;
        conversationState.GuidedCriteria = isGuidedRequest ? guidedCriteria : conversationState.GuidedCriteria;
        var returnedRecommendationIds = cards
            .Select(card => card.RecommendationId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id!)
            .ToList();
        conversationState.LastRecommendationIds = isGuidedRequest && isAlternativeRequest && samePlanningContext
            ? conversationState.LastRecommendationIds
                .Concat(returnedRecommendationIds)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList()
            : returnedRecommendationIds;

        await conversationStateService.SavePlanningStateAsync(
            conversation,
            conversationId,
            user.Id,
            conversationState,
            cancellationToken);

        return TrackOutcome(new TravelChatResponse(
            conversationId,
            useModelResponse ? modelResult!.Message : defaultMessage,
            Intent,
            cards,
            useModelResponse && modelResult!.SuggestedReplies.Count > 0
                ? modelResult.SuggestedReplies
                : defaultSuggestedReplies,
            null,
            null,
            isGuidedRequest ? guidedCriteria : null),
            responseMode,
            useModelResponse,
            modelResult is null ? "model_fallback" : "plan_response",
            diagnostics,
            locale,
            promptVersion);
    }

    private async Task<TravelChatResponse> CreateScheduleResponseAsync(
        AppUser user,
        string conversationId,
        DateOnly date,
        string locale,
        string promptVersion,
        CancellationToken cancellationToken)
    {
        var trips = await dbContext.Trips
            .AsNoTracking()
            .Include(trip => trip.Destination)
            .Include(trip => trip.Reservations)
            .Where(trip =>
                trip.PublicationStatus == TripPublicationStatus.Published
                && trip.AppUserId == user.Id
                && trip.StartsOn <= date
                && trip.EndsOn >= date)
            .ToListAsync(cancellationToken);

        if (trips.Count == 0)
        {
            return TrackOutcome(responseComposer.MissingContext(
                conversationId,
                "date",
                textProvider.ScheduleNoActiveTripMessage(date, locale),
                textProvider.ScheduleNoActiveTripReplies(locale)),
                eventName: "missing_context",
                locale: locale,
                promptVersion: promptVersion);
        }

        var reservations = trips
            .SelectMany(trip => trip.Reservations)
            .Where(reservation => IsReservationOnDate(reservation, date))
            .OrderBy(reservation => GetStartForDate(reservation, date))
            .ToList();

        var destinationName = trips
            .Select(trip => trip.Destination?.Name)
            .FirstOrDefault(name => !string.IsNullOrWhiteSpace(name))
            ?? "tu viaje";

        if (reservations.Count == 0)
        {
            return TrackOutcome(new TravelChatResponse(
                conversationId,
                textProvider.EmptyScheduleMessage(date, destinationName, locale),
                ViewScheduleIntent,
                [],
                textProvider.EmptyScheduleReplies(date, locale),
                null),
                eventName: "schedule_empty",
                locale: locale,
                promptVersion: promptVersion);
        }

        return TrackOutcome(new TravelChatResponse(
            conversationId,
            textProvider.ScheduleSummaryMessage(date, destinationName, reservations, locale),
            ViewScheduleIntent,
            [],
            textProvider.ScheduleReplies(date, locale),
            null),
            eventName: "schedule_response",
            locale: locale,
            promptVersion: promptVersion);
    }

    private async Task<TravelChatResponse> CreatePreferenceResponseAsync(
        Guid userId,
        string conversationId,
        string locale,
        string promptVersion,
        CancellationToken cancellationToken)
    {
        var currentProfile = await userProfileService.GetProfileDtoAsync(userId, cancellationToken);

        return TrackOutcome(new TravelChatResponse(
            conversationId,
            textProvider.PreferencesMessage(currentProfile, locale),
            ViewPreferencesIntent,
            [],
            textProvider.PreferencesReplies(locale),
            null),
            eventName: "preferences_viewed",
            locale: locale,
            promptVersion: promptVersion);
    }

    private async Task<TravelChatResponse> CreatePreferenceConfirmationResponseAsync(
        TravelChatConversation? conversation,
        string conversationId,
        Guid userId,
        string? message,
        TravelPreferenceProfilePatchDto patch,
        string locale,
        string promptVersion,
        CancellationToken cancellationToken)
    {
        await conversationStateService.SavePendingPreferencePatchAsync(
            conversation,
            conversationId,
            userId,
            message,
            patch,
            cancellationToken);

        return TrackOutcome(new TravelChatResponse(
            conversationId,
            textProvider.PreferenceConfirmationMessage(patch, locale),
            UpdatePreferencesIntent,
            [],
            textProvider.PreferenceConfirmationReplies(locale),
            new MissingContextDto(
                "preferenceConfirmation",
                textProvider.PreferenceConfirmationMissingMessage(locale),
                textProvider.PreferenceConfirmationReplies(locale))),
            eventName: "preference_confirmation_requested",
            locale: locale,
            promptVersion: promptVersion);
    }

    private async Task<TravelPreferenceProfileDto> ApplyPreferencePatchAsync(
        Guid userId,
        TravelPreferenceProfilePatchDto patch,
        CancellationToken cancellationToken)
    {
        var currentProfile = await userProfileService.GetProfileDtoAsync(userId, cancellationToken);
        return await userProfileService.PatchProfileAsync(
            userId,
            MergePreferencePatch(currentProfile, patch),
            cancellationToken);
    }

    private async Task<TravelPreferenceProfile?> CreateEffectivePreferenceProfileAsync(
        AppUser user,
        TravelPreferenceProfile? preferences,
        TravelPreferenceProfilePatchDto patch,
        CancellationToken cancellationToken)
    {
        var currentProfile = await userProfileService.GetProfileDtoAsync(user.Id, cancellationToken);
        var mergedPatch = MergePreferencePatch(currentProfile, patch);
        var effective = new TravelPreferenceProfile
        {
            UserId = user.Id,
            Interests = preferences?.Interests.ToList() ?? [],
            FoodPreferences = preferences?.FoodPreferences.ToList() ?? [],
            DietaryRestrictions = preferences?.DietaryRestrictions.ToList() ?? [],
            BudgetLevel = preferences?.BudgetLevel ?? "medium",
            TravelPace = preferences?.TravelPace ?? "balanced",
            Dislikes = preferences?.Dislikes.ToList() ?? [],
            AvoidTouristTraps = preferences?.AvoidTouristTraps ?? true,
            MaxWalkingMinutes = preferences?.MaxWalkingMinutes ?? 25
        };

        ApplyPatch(effective, mergedPatch);
        return effective;
    }

    private async Task<TravelAiModelResult?> CreateModelResponseAsync(
        string conversationId,
        TravelChatRequest request,
        TravelPreferenceProfile profile,
        TravelPlanningContext context,
        IReadOnlyList<Reservation> reservations,
        IReadOnlyList<TravelCardDto> cards,
        IReadOnlyList<string> suggestedReplies,
        string promptVersion,
        CancellationToken cancellationToken)
    {
        try
        {
            return await modelClient.CreateStructuredResponseAsync(
                new TravelAiModelRequest(
                    conversationId,
                    Intent,
                    request.Message,
                    request.Locale,
                    profile,
                    context,
                    reservations,
                    cards,
                    suggestedReplies,
                    promptVersion),
                cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "Travel AI model client failed; using deterministic chat response. PromptVersion={PromptVersion}; Locale={Locale}; Intent={Intent}.",
                promptVersion,
                request.Locale ?? "none",
                Intent);
            return null;
        }
    }

    private static TravelPreferenceProfile CreateProfile(
        AppUser user,
        TravelPreferenceProfile? preferences,
        string responseMode,
        string? message,
        GuidedPlanCriteriaDto? guidedCriteria)
    {
        var profile = new TravelPreferenceProfile
        {
            UserId = user.Id,
            Interests = preferences?.Interests.ToList() ?? [],
            FoodPreferences = preferences?.FoodPreferences.ToList() ?? [],
            DietaryRestrictions = preferences?.DietaryRestrictions.ToList() ?? [],
            BudgetLevel = guidedCriteria?.Budget ?? preferences?.BudgetLevel ?? "medium",
            TravelPace = preferences?.TravelPace ?? "balanced",
            Dislikes = preferences?.Dislikes.ToList() ?? [],
            AvoidTouristTraps = preferences?.AvoidTouristTraps ?? true,
            MaxWalkingMinutes = responseMode == LessWalkingMode
                ? Math.Min(preferences?.MaxWalkingMinutes ?? 25, 12)
                : guidedCriteria?.MaxWalkingMinutes ?? preferences?.MaxWalkingMinutes ?? 25
        };

        ApplyRequestSignals(profile, message, responseMode);
        return profile;
    }

    private static void ApplyHiddenConversationTags(
        TravelPreferenceProfile preferences,
        IReadOnlyList<string> hiddenTags)
    {
        foreach (var tag in hiddenTags)
        {
            if (!string.IsNullOrWhiteSpace(tag))
            {
                AddUnique(preferences.Dislikes, tag.Trim());
            }
        }
    }

    private static void ApplyRequestSignals(
        TravelPreferenceProfile preferences,
        string? message,
        string responseMode)
    {
        var normalized = TravelChatIntentClassifier.Normalize(message);

        if (IsFoodResponseMode(responseMode))
        {
            AddUnique(preferences.Interests, "Food");
            AddUnique(preferences.FoodPreferences, "local food");
        }

        if (responseMode == FoodBreakfastMode)
        {
            AddUnique(preferences.Interests, "breakfast");
        }

        if (responseMode == FoodLunchMode)
        {
            AddUnique(preferences.Interests, "lunch");
            AddUnique(preferences.Interests, "almuerzo");
        }

        if (responseMode == FoodDinnerMode)
        {
            AddUnique(preferences.Interests, "dinner");
            AddUnique(preferences.Interests, "cena");
        }

        if (responseMode == FoodBrunchMode)
        {
            AddUnique(preferences.Interests, "brunch");
        }

        if (IsCultureResponseMode(responseMode))
        {
            AddUnique(preferences.Interests, "Culture");
        }

        if (responseMode == CultureMuseumMode)
        {
            AddUnique(preferences.Interests, "museum");
        }

        if (responseMode == CultureTempleMode)
        {
            AddUnique(preferences.Interests, "temple");
            AddUnique(preferences.Interests, "shrine");
        }

        if (responseMode == CultureArtMode)
        {
            AddUnique(preferences.Interests, "art");
        }

        if (responseMode == CultureHistoryMode)
        {
            AddUnique(preferences.Interests, "history");
        }

        if (IsNatureResponseMode(responseMode))
        {
            AddUnique(preferences.Interests, "Nature");
        }

        if (responseMode == NatureGardenMode)
        {
            AddUnique(preferences.Interests, "garden");
        }

        if (responseMode == NatureParkMode)
        {
            AddUnique(preferences.Interests, "park");
        }

        if (responseMode == NatureCoastMode)
        {
            AddUnique(preferences.Interests, "coast");
            AddUnique(preferences.Interests, "river");
        }

        if (responseMode == NatureOnsenMode)
        {
            AddUnique(preferences.Interests, "onsen");
        }

        if (IsShoppingResponseMode(responseMode))
        {
            AddUnique(preferences.Interests, "Shopping");
        }

        if (responseMode == ShoppingMarketMode)
        {
            AddUnique(preferences.Interests, "market");
        }

        if (responseMode == ShoppingVintageMode)
        {
            AddUnique(preferences.Interests, "vintage");
        }

        if (responseMode == ShoppingSouvenirMode)
        {
            AddUnique(preferences.Interests, "souvenir");
        }

        if (IsViewpointResponseMode(responseMode))
        {
            AddUnique(preferences.Interests, "Viewpoint");
        }

        if (responseMode == ViewpointSunsetMode)
        {
            AddUnique(preferences.Interests, "sunset");
        }

        if (responseMode == ViewpointPhotoMode)
        {
            AddUnique(preferences.Interests, "photo");
        }

        if (IsNightlifeResponseMode(responseMode))
        {
            AddUnique(preferences.Interests, "nightlife");
            AddUnique(preferences.Interests, "bar");
            AddUnique(preferences.Interests, "music");
        }

        if (responseMode == NightlifeKaraokeMode)
        {
            AddUnique(preferences.Interests, "karaoke");
        }

        if (responseMode == NightlifeLiveMusicMode)
        {
            AddUnique(preferences.Interests, "live music");
            AddUnique(preferences.Interests, "jazz");
        }

        if (responseMode == DanceMode)
        {
            AddUnique(preferences.Interests, "dance");
            AddUnique(preferences.Interests, "nightlife");
            AddUnique(preferences.Interests, "club");
            AddUnique(preferences.Interests, "music");
        }

        if (responseMode == NeighborhoodMode)
        {
            AddUnique(preferences.Interests, "neighborhood");
            AddUnique(preferences.Interests, "local");
        }

        if (ContainsAny(normalized, "caminar", "caminata", "paseo", "walk", "walking"))
        {
            AddUnique(preferences.Interests, "walking");
            AddUnique(preferences.Interests, "walk");
            AddUnique(preferences.Interests, "paseo");
        }

        if (ContainsAny(normalized, "pareja", "cita", "romantico", "romance", "couple", "date"))
        {
            AddUnique(preferences.Interests, "romantic");
            AddUnique(preferences.Interests, "couple");
            AddUnique(preferences.Interests, "pareja");
        }

        if (ContainsAny(normalized, "nocturno", "noche", "night", "nightlife"))
        {
            AddUnique(preferences.Interests, "nightlife");
            AddUnique(preferences.Interests, "night");
            AddUnique(preferences.Interests, "noche");
        }

        if (ContainsAny(normalized, "bailar", "baile", "dance", "club", "boliche"))
        {
            AddUnique(preferences.Interests, "dance");
            AddUnique(preferences.Interests, "bailar");
            AddUnique(preferences.Interests, "nightlife");
        }

        if (responseMode == LessWalkingMode)
        {
            if (preferences.MaxWalkingMinutes > 12)
            {
                preferences.MaxWalkingMinutes = 12;
            }

            if (!string.Equals(preferences.TravelPace, "relaxed", StringComparison.Ordinal))
            {
                preferences.TravelPace = "relaxed";
            }
        }

        if (responseMode == ShorterMode
            && !string.Equals(preferences.TravelPace, "efficient", StringComparison.Ordinal))
        {
            preferences.TravelPace = "efficient";
        }

        if (responseMode == CheaperMode
            && !string.Equals(preferences.BudgetLevel, "low", StringComparison.Ordinal))
        {
            preferences.BudgetLevel = "low";
        }

        if (responseMode == MediumCostMode
            && !string.Equals(preferences.BudgetLevel, "medium", StringComparison.Ordinal))
        {
            preferences.BudgetLevel = "medium";
        }

        if (responseMode == HighCostMode
            && !string.Equals(preferences.BudgetLevel, "high", StringComparison.Ordinal))
        {
            preferences.BudgetLevel = "high";
        }

        if (ContainsAny(normalized, "vegetariano", "vegetariana", "vegetarian"))
        {
            AddUnique(preferences.DietaryRestrictions, "vegetarian");
        }

        if (ContainsAny(normalized, "sin gluten", "gluten free", "celiaco", "celiaca", "celiac"))
        {
            AddUnique(preferences.DietaryRestrictions, "gluten-free");
        }

        if (ContainsAny(normalized, "sin museos", "no museos", "evitar museos"))
        {
            AddUnique(preferences.Dislikes, "museum");
        }

        if (ContainsAny(normalized, "sin shopping", "no shopping", "evitar compras"))
        {
            AddUnique(preferences.Dislikes, "shopping");
        }
    }

    private static bool AddUnique(List<string> values, string value)
    {
        if (values.Any(existing => string.Equals(existing, value, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        values.Add(value);
        return true;
    }

    private static bool ContainsAny(string value, params string[] candidates)
    {
        return candidates.Any(candidate => value.Contains(candidate, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsFoodResponseMode(string responseMode)
    {
        return responseMode is FoodMode
            or FoodBreakfastMode
            or FoodLunchMode
            or FoodDinnerMode
            or FoodBrunchMode;
    }

    private static bool IsCultureResponseMode(string responseMode)
    {
        return responseMode is CultureMode
            or CultureMuseumMode
            or CultureTempleMode
            or CultureArtMode
            or CultureHistoryMode;
    }

    private static bool IsNatureResponseMode(string responseMode)
    {
        return responseMode is NatureMode
            or NatureGardenMode
            or NatureParkMode
            or NatureCoastMode
            or NatureOnsenMode;
    }

    private static bool IsShoppingResponseMode(string responseMode)
    {
        return responseMode is ShoppingMode
            or ShoppingMarketMode
            or ShoppingVintageMode
            or ShoppingSouvenirMode;
    }

    private static bool IsViewpointResponseMode(string responseMode)
    {
        return responseMode is ViewpointMode
            or ViewpointSunsetMode
            or ViewpointPhotoMode;
    }

    private static bool IsNightlifeResponseMode(string responseMode)
    {
        return responseMode is NightlifeMode
            or NightlifeBarMode
            or NightlifeKaraokeMode
            or NightlifeLiveMusicMode
            or DanceMode;
    }

    private static GuidedPlanCriteriaDto? NormalizeGuidedCriteria(GuidedPlanCriteriaDto? criteria)
    {
        if (criteria is null)
        {
            return null;
        }

        var categories = criteria.Categories.Append(criteria.Category)
            .Where(GuidedTravelCategories.IsValid)
            .Select(value => value!)
            .Distinct(StringComparer.OrdinalIgnoreCase).Take(9).ToList();
        if (categories.Count == 0) return null;

        var budgets = criteria.Budgets.Append(criteria.Budget)
            .Where(value => value is "low" or "medium" or "high")
            .Select(value => value!)
            .Distinct(StringComparer.OrdinalIgnoreCase).Take(3).ToList();
        var walkingOptions = criteria.WalkingMinuteOptions.Append(criteria.MaxWalkingMinutes ?? 0)
            .Where(value => value is 15 or 30).Distinct().Order().ToList();

        return new GuidedPlanCriteriaDto(
            categories[0],
            GuidedTravelPriorities.IsValid(criteria.Priority) ? criteria.Priority : GuidedTravelPriorities.Direct,
            budgets.FirstOrDefault(),
            walkingOptions.Count == 0 ? null : walkingOptions.Max(),
            criteria.MaxDurationMinutes is 60 or 120 ? criteria.MaxDurationMinutes : null)
        {
            Categories = categories,
            Budgets = budgets,
            WalkingMinuteOptions = walkingOptions
        };
    }

    private static string ResolveGuidedResponseMode(string? category) => category switch
    {
        GuidedTravelCategories.Food => FoodMode,
        GuidedTravelCategories.Relax => NatureOnsenMode,
        GuidedTravelCategories.Culture => CultureMode,
        GuidedTravelCategories.Walk => NeighborhoodMode,
        GuidedTravelCategories.Dance => DanceMode,
        GuidedTravelCategories.Nature => NatureMode,
        GuidedTravelCategories.Shopping => ShoppingMode,
        GuidedTravelCategories.Viewpoint => ViewpointMode,
        GuidedTravelCategories.Nightlife => NightlifeMode,
        _ => BalancedMode
    };

    private static string CreateGuidedPlanningMessage(
        GuidedPlanCriteriaDto criteria,
        bool alternative,
        string locale)
    {
        var category = criteria.Category switch
        {
            GuidedTravelCategories.Food => IsEnglish(locale) ? "food" : "comida",
            GuidedTravelCategories.Relax => IsEnglish(locale) ? "relax" : "relajar",
            GuidedTravelCategories.Culture => IsEnglish(locale) ? "culture" : "cultura",
            GuidedTravelCategories.Walk => IsEnglish(locale) ? "walking" : "pasear",
            GuidedTravelCategories.Dance => IsEnglish(locale) ? "dance" : "bailar",
            GuidedTravelCategories.Nature => IsEnglish(locale) ? "nature" : "naturaleza",
            GuidedTravelCategories.Shopping => IsEnglish(locale) ? "shopping" : "compras",
            GuidedTravelCategories.Viewpoint => IsEnglish(locale) ? "viewpoint" : "mirador",
            GuidedTravelCategories.Nightlife => IsEnglish(locale) ? "nightlife" : "vida nocturna",
            _ => IsEnglish(locale) ? "activity" : "actividad"
        };
        return alternative
            ? IsEnglish(locale) ? $"Another option for {category}" : $"Otra opción de {category}"
            : $"{(IsEnglish(locale) ? "Plan for" : "Plan para")} {category}";
    }

    private static string CreateFullDayPlanningMessage(string locale)
    {
        return IsEnglish(locale)
            ? "Build a day with morning coffee, a visit, lunch, and dinner"
            : "Armá un día con café por la mañana, una visita, almuerzo y cena";
    }

    private static string CreateFullDayResponseMessage(int stopCount, string locale)
    {
        if (IsEnglish(locale))
        {
            return stopCount == 4
                ? "I prepared four different plans for this day."
                : $"I found {stopCount} compatible stops for this day. I left out slots without a suitable place.";
        }

        return stopCount == 4
            ? "Preparé cuatro planes diferentes para este día."
            : $"Encontré {stopCount} paradas compatibles para este día. Dejé libres los momentos sin un lugar adecuado.";
    }

    private static List<FullDayStop> SelectFullDayStops(
        IReadOnlyList<ScoredRecommendation> ranked,
        GuidedPlanCriteriaDto criteria,
        string requestSeed,
        string locale)
    {
        var english = IsEnglish(locale);
        var used = new HashSet<Guid>();
        var stops = new List<FullDayStop>(4);

        AddFullDayStop(stops, used, ranked, requestSeed, "coffee", new TimeOnly(9, 0),
            english ? "Morning coffee" : "Café de mañana",
            recommendation => ContainsRecommendationTerms(recommendation, "cafe", "café", "coffee", "cafeteria", "breakfast", "desayuno"),
            IsFoodRecommendation);
        AddFullDayStop(stops, used, ranked, requestSeed, "visit", new TimeOnly(10, 30),
            english ? "Morning visit" : "Visita de mañana",
            recommendation => !IsFoodRecommendation(recommendation) && MatchesAnyInterest(recommendation, criteria),
            recommendation => !IsFoodRecommendation(recommendation) && IsSightseeingRecommendation(recommendation));
        AddFullDayStop(stops, used, ranked, requestSeed, "lunch", new TimeOnly(13, 0),
            english ? "Lunch" : "Almuerzo",
            recommendation => IsFoodRecommendation(recommendation)
                && ContainsRecommendationTerms(recommendation, "lunch", "almuerzo", "ramen", "sushi"),
            IsFoodRecommendation);
        AddFullDayStop(stops, used, ranked, requestSeed, "dinner", new TimeOnly(19, 30),
            english ? "Dinner" : "Cena",
            recommendation => IsFoodRecommendation(recommendation)
                && ContainsRecommendationTerms(recommendation, "dinner", "cena", "izakaya", "yakitori", "omakase"),
            IsFoodRecommendation);

        return stops.OrderBy(stop => stop.StartsAt).ToList();
    }

    private static bool MatchesAnyInterest(Recommendation recommendation, GuidedPlanCriteriaDto criteria)
    {
        var categories = criteria.Categories.Append(criteria.Category)
            .Where(GuidedTravelCategories.IsValid)
            .Select(value => value!)
            .Distinct(StringComparer.OrdinalIgnoreCase);
        return categories.Any(category => MatchesInterest(recommendation, category));
    }

    private static void AddFullDayStop(
        ICollection<FullDayStop> stops,
        ISet<Guid> used,
        IReadOnlyList<ScoredRecommendation> ranked,
        string requestSeed,
        string slotId,
        TimeOnly startsAt,
        string label,
        Func<Recommendation, bool> preferred,
        Func<Recommendation, bool> fallback)
    {
        var available = ranked.Where(candidate => !used.Contains(candidate.Recommendation.Id)).ToList();
        var candidate = ChooseStableRandom(available.Where(item => preferred(item.Recommendation)), requestSeed, slotId)
            ?? ChooseStableRandom(available.Where(item => fallback(item.Recommendation)), requestSeed, slotId)
            ?? ChooseStableRandom(available, requestSeed, slotId);
        if (candidate is null)
        {
            return;
        }

        used.Add(candidate.Recommendation.Id);
        stops.Add(new FullDayStop(candidate, startsAt, label));
    }

    private static ScoredRecommendation? ChooseStableRandom(
        IEnumerable<ScoredRecommendation> candidates,
        string requestSeed,
        string slotId)
    {
        return candidates
            .Take(20)
            .OrderBy(candidate => StableRandomOrder(requestSeed, slotId, candidate.Recommendation.Id))
            .FirstOrDefault();
    }

    private static ulong StableRandomOrder(string seed, string slotId, Guid recommendationId)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"{seed}|{slotId}|{recommendationId:N}"));
        return BitConverter.ToUInt64(bytes, 0);
    }

    private static bool IsFoodRecommendation(Recommendation recommendation)
    {
        return ContainsRecommendationTerms(
            recommendation,
            "food", "comida", "restaurant", "restaurante", "cafe", "café", "coffee", "ramen", "sushi",
            "izakaya", "yakitori", "omakase", "breakfast", "desayuno", "lunch", "almuerzo", "dinner", "cena");
    }

    private static bool IsSightseeingRecommendation(Recommendation recommendation)
    {
        return ContainsRecommendationTerms(
            recommendation,
            "culture", "cultura", "museum", "museo", "temple", "templo", "shrine", "santuario", "garden",
            "jardin", "park", "parque", "history", "historia", "art", "arte", "viewpoint", "mirador",
            "neighborhood", "barrio", "market", "mercado", "shopping", "compras", "walk", "paseo");
    }

    private static bool MatchesInterest(Recommendation recommendation, string? category)
    {
        return category switch
        {
            GuidedTravelCategories.Food => IsFoodRecommendation(recommendation),
            GuidedTravelCategories.Relax => ContainsRecommendationTerms(recommendation, "relax", "spa", "onsen", "quiet", "tranquilo", "garden", "jardin"),
            GuidedTravelCategories.Culture => ContainsRecommendationTerms(recommendation, "culture", "cultura", "museum", "museo", "temple", "templo", "history", "historia", "art", "arte"),
            GuidedTravelCategories.Walk => ContainsRecommendationTerms(recommendation, "walk", "walking", "paseo", "neighborhood", "barrio"),
            GuidedTravelCategories.Dance => ContainsRecommendationTerms(recommendation, "dance", "baile", "club", "music", "musica"),
            GuidedTravelCategories.Nature => ContainsRecommendationTerms(recommendation, "nature", "naturaleza", "garden", "jardin", "park", "parque", "river", "rio"),
            GuidedTravelCategories.Shopping => ContainsRecommendationTerms(recommendation, "shopping", "compras", "shop", "tienda", "market", "mercado"),
            GuidedTravelCategories.Viewpoint => ContainsRecommendationTerms(recommendation, "viewpoint", "mirador", "view", "vista", "observatory", "observatorio"),
            GuidedTravelCategories.Nightlife => ContainsRecommendationTerms(recommendation, "nightlife", "noche", "bar", "karaoke", "club", "music", "musica"),
            _ => IsSightseeingRecommendation(recommendation)
        };
    }

    private static bool ContainsRecommendationTerms(Recommendation recommendation, params string[] terms)
    {
        var searchable = string.Join(' ',
            recommendation.Title,
            recommendation.Category,
            recommendation.RefinedType,
            recommendation.Description,
            recommendation.DescriptionEn,
            string.Join(' ', recommendation.Tags)).ToLowerInvariant();
        return terms.Any(term => searchable.Contains(term, StringComparison.OrdinalIgnoreCase));
    }

    private sealed record FullDayStop(ScoredRecommendation Recommendation, TimeOnly StartsAt, string Label);

    private static GuidedQuestionDto CreateAdjustQuestion(string locale)
    {
        var english = IsEnglish(locale);
        return new GuidedQuestionDto(
            "adjust",
            english ? "What would you like to adjust?" : "¿Qué querés ajustar?",
            [
                new GuidedOptionDto("adjust.category", english ? "Plan type" : "Tipo de plan"),
                new GuidedOptionDto("adjust.budget", english ? "Budget" : "Presupuesto"),
                new GuidedOptionDto("adjust.distance", english ? "Distance" : "Cercanía"),
                new GuidedOptionDto("adjust.duration", english ? "Duration" : "Duración")
            ]);
    }

    private static GuidedQuestionDto CreateLocationQuestion(string locale)
    {
        var english = IsEnglish(locale);
        return new GuidedQuestionDto(
            "location",
            english ? "How should I continue?" : "¿Cómo querés continuar?",
            [
                new GuidedOptionDto("location.retry", english ? "Try location again" : "Intentar ubicación otra vez"),
                new GuidedOptionDto("location.skip", english ? "Continue without distance" : "Continuar sin distancia")
            ]);
    }

    private static bool IsEnglish(string? locale) =>
        locale?.StartsWith("en", StringComparison.OrdinalIgnoreCase) == true;

    private static bool IsAlternativeRequest(string? message)
    {
        return !string.IsNullOrWhiteSpace(message)
            && ContainsAny(
                message.Trim().ToLowerInvariant(),
                "otra opcion",
                "otra opción",
                "otra alternativa",
                "algo distinto",
                "reemplazar",
                "replace");
    }

    private static bool MentionsSavedState(string message)
    {
        return ContainsAny(
            message,
            "guardado",
            "guardada",
            "saved",
            "lo guarde",
            "lo guardé");
    }

    private static TravelPreferenceProfilePatchDto MergePreferencePatch(
        TravelPreferenceProfileDto current,
        TravelPreferenceProfilePatchDto patch)
    {
        var mergedDislikes = patch.Dislikes is null
            ? null
            : MergeLists(current.Dislikes, patch.Dislikes);
        var mergedInterests = patch.Interests is null
            ? current.Interests
            : MergeLists(current.Interests, patch.Interests);
        var updatedInterests = mergedDislikes is null
            ? (patch.Interests is null ? null : mergedInterests)
            : RemoveDislikedInterests(mergedInterests, mergedDislikes);

        return new TravelPreferenceProfilePatchDto(
            patch.FoodPreferences is null ? null : MergeLists(current.FoodPreferences, patch.FoodPreferences),
            patch.DietaryRestrictions is null ? null : MergeLists(current.DietaryRestrictions, patch.DietaryRestrictions),
            patch.BudgetLevel,
            patch.TravelPace,
            updatedInterests,
            mergedDislikes,
            patch.AvoidTouristTraps,
            patch.MaxWalkingMinutes);
    }

    private static IReadOnlyList<string> MergeLists(
        IReadOnlyList<string> current,
        IReadOnlyList<string> incoming)
    {
        return current
            .Concat(incoming)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static IReadOnlyList<string> RemoveDislikedInterests(
        IReadOnlyList<string> interests,
        IReadOnlyList<string> dislikes)
    {
        return interests
            .Where(interest => !dislikes.Any(dislike =>
                string.Equals(interest, dislike, StringComparison.OrdinalIgnoreCase)))
            .ToList();
    }

    private static void ApplyPatch(TravelPreferenceProfile profile, TravelPreferenceProfilePatchDto patch)
    {
        if (patch.FoodPreferences is not null)
        {
            profile.FoodPreferences = patch.FoodPreferences.ToList();
        }

        if (patch.DietaryRestrictions is not null)
        {
            profile.DietaryRestrictions = patch.DietaryRestrictions.ToList();
        }

        if (!string.IsNullOrWhiteSpace(patch.BudgetLevel))
        {
            profile.BudgetLevel = patch.BudgetLevel;
        }

        if (!string.IsNullOrWhiteSpace(patch.TravelPace))
        {
            profile.TravelPace = patch.TravelPace;
        }

        if (patch.Interests is not null)
        {
            profile.Interests = patch.Interests.ToList();
        }

        if (patch.Dislikes is not null)
        {
            profile.Dislikes = patch.Dislikes.ToList();
        }

        if (patch.AvoidTouristTraps.HasValue)
        {
            profile.AvoidTouristTraps = patch.AvoidTouristTraps.Value;
        }

        if (patch.MaxWalkingMinutes.HasValue)
        {
            profile.MaxWalkingMinutes = patch.MaxWalkingMinutes.Value;
        }
    }

    private static HashSet<string> ParseRecommendationIds(string? recommendationIds)
    {
        if (string.IsNullOrWhiteSpace(recommendationIds))
        {
            return [];
        }

        var guidMatches = Regex.Matches(
            recommendationIds,
            @"[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}");
        if (guidMatches.Count > 0)
        {
            return guidMatches
                .Select(match => match.Value)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }

        return recommendationIds
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private TravelChatResponse TrackOutcome(
        TravelChatResponse response,
        string? responseMode = null,
        bool usedModelResponse = false,
        string? eventName = null,
        TravelAssistantDiagnostics? diagnostics = null,
        string? locale = null,
        string? promptVersion = null)
    {
        logger.LogInformation(
            "Travel assistant outcome. Event={EventName}; Intent={Intent}; Locale={Locale}; PromptVersion={PromptVersion}; Cards={CardCount}; SuggestedReplies={SuggestedReplyCount}; MissingContextField={MissingContextField}; ResponseMode={ResponseMode}; UsedModelResponse={UsedModelResponse}; RecommendationsUnlocked={RecommendationsUnlocked}; RankedCandidates={RankedCandidates}; DislikedFilteredCandidates={DislikedFilteredCandidates}; ExcludedRecommendations={ExcludedRecommendations}; ReturnedRecommendations={ReturnedRecommendations}; TopRecommendationId={TopRecommendationId}; TopRecommendationTitle={TopRecommendationTitle}.",
            eventName ?? "response",
            response.Intent,
            locale ?? "none",
            promptVersion ?? "none",
            response.Cards.Count,
            response.SuggestedReplies.Count,
            response.MissingContext?.Field ?? "none",
            responseMode ?? "none",
            usedModelResponse,
            diagnostics?.RecommendationsUnlocked,
            diagnostics?.RankedCandidates,
            diagnostics?.DislikedFilteredCandidates,
            diagnostics?.ExcludedRecommendations,
            diagnostics?.ReturnedRecommendations,
            diagnostics?.TopRecommendationId ?? "none",
            diagnostics?.TopRecommendationTitle ?? "none");

        telemetry.RecordChatOutcome(
            response,
            responseMode,
            usedModelResponse,
            eventName,
            locale,
            promptVersion,
            diagnostics);

        return response;
    }

    private void LogIntentClassification(TravelChatIntentResult intent, string? message)
    {
        logger.LogInformation(
            "Travel assistant intent classified. Intent={Intent}; Confidence={Confidence}; ResponseMode={ResponseMode}; MatchedSignals={MatchedSignals}; HasPlanningSignal={HasPlanningSignal}; UnsupportedSample={UnsupportedSample}.",
            intent.Intent,
            Math.Round(intent.Confidence, 2),
            intent.ResponseMode,
            intent.MatchedSignals.Count == 0 ? "none" : string.Join(',', intent.MatchedSignals),
            intent.HasPlanningSignal,
            intent.IsSupported ? "none" : CreateUnsupportedIntentSample(message));
    }

    private static string CreateUnsupportedIntentSample(string? message)
    {
        var normalized = TravelChatIntentClassifier.Normalize(message);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return "empty";
        }

        var tokens = normalized
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Take(8)
            .Select(token =>
            {
                if (token.Any(char.IsDigit))
                {
                    return "<num>";
                }

                return token.Length > 24 ? "<long>" : token;
            });

        return string.Join(' ', tokens);
    }

    private static string ResolveCity(
        string? requestedCity,
        IReadOnlyList<Reservation> reservations,
        IReadOnlyList<Trip> trips)
    {
        if (!string.IsNullOrWhiteSpace(requestedCity))
        {
            return requestedCity.Trim();
        }

        return reservations
            .Select(reservation => reservation.City)
            .FirstOrDefault(city => !string.IsNullOrWhiteSpace(city))
            ?? trips.Select(trip => trip.Destination?.Name)
                .FirstOrDefault(name => !string.IsNullOrWhiteSpace(name))
            ?? "tu destino";
    }

    private static (TimeOnly Start, TimeOnly End, int AvailableMinutes)? FindPlanningWindow(
        IReadOnlyList<Reservation> reservations,
        DateOnly date)
    {
        var ordered = reservations
            .OrderBy(reservation => GetStartForDate(reservation, date))
            .ToList();

        if (ordered.Count == 0)
        {
            return (new TimeOnly(10, 0), new TimeOnly(13, 0), 180);
        }

        for (var i = 0; i < ordered.Count - 1; i++)
        {
            var end = GetEndForDate(ordered[i], date);
            var nextStart = GetStartForDate(ordered[i + 1], date);
            var availableMinutes = (int)(nextStart.ToTimeSpan() - end.ToTimeSpan()).TotalMinutes;
            if (availableMinutes >= 60)
            {
                return (end, nextStart, availableMinutes);
            }
        }

        var firstStart = GetStartForDate(ordered[0], date);
        var morningStart = new TimeOnly(9, 0);
        var morningMinutes = (int)(firstStart.ToTimeSpan() - morningStart.ToTimeSpan()).TotalMinutes;
        if (morningMinutes >= 90)
        {
            return (morningStart, firstStart, morningMinutes);
        }

        var lastEnd = GetEndForDate(ordered[^1], date);
        var eveningEnd = new TimeOnly(21, 0);
        var eveningMinutes = (int)(eveningEnd.ToTimeSpan() - lastEnd.ToTimeSpan()).TotalMinutes;
        if (eveningMinutes >= 90)
        {
            return (lastEnd, eveningEnd, eveningMinutes);
        }

        return null;
    }

    private static bool IsReservationOnDate(Reservation reservation, DateOnly date)
    {
        return reservation.Date <= date && (reservation.EndsOn ?? reservation.Date) >= date;
    }

    private static TimeOnly GetStartForDate(Reservation reservation, DateOnly date)
    {
        return reservation.Date == date ? reservation.StartsAt : TimeOnly.MinValue;
    }

    private static TimeOnly GetEndForDate(Reservation reservation, DateOnly date)
    {
        if (reservation.EndsOn == date && reservation.EndsAt.HasValue)
        {
            return reservation.EndsAt.Value;
        }

        if (reservation.Date < date)
        {
            return TimeOnly.MinValue;
        }

        if (reservation.EndsAt.HasValue && reservation.EndsOn is null)
        {
            return reservation.EndsAt.Value;
        }

        var fallbackMinutes = reservation.Type switch
        {
            ReservationType.Flight => 120,
            ReservationType.Lodging => 45,
            _ => 90
        };

        return reservation.StartsAt.AddMinutes(fallbackMinutes);
    }

}

public sealed record TravelAssistantDiagnostics(
    int RecommendationsUnlocked,
    int RankedCandidates,
    int DislikedFilteredCandidates,
    int ExcludedRecommendations,
    int ReturnedRecommendations,
    string? TopRecommendationId,
    string? TopRecommendationTitle);

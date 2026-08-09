using System.Globalization;
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

        if (!userProfileService.HasMinimumPreferences(effectivePreferences, out var missingPreferenceFields))
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
                trip.AppUserId == user.Id
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

        var responseMode = IsAlternativeRequest(request.Message)
            ? string.IsNullOrWhiteSpace(conversationState.LastResponseMode) ? BalancedMode : conversationState.LastResponseMode
            : actionPlan.ResponseMode;
        var explicitRecommendationIds = ParseRecommendationIds(request.Message);
        HashSet<string> previousRecommendationIds = IsAlternativeRequest(request.Message)
            ? conversationState.LastRecommendationIds.ToHashSet(StringComparer.OrdinalIgnoreCase)
            : [];
        var excludedRecommendationIds = previousRecommendationIds
            .Concat(explicitRecommendationIds)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var profile = CreateProfile(user, effectivePreferences!, responseMode, request.Message);
        ApplyHiddenConversationTags(profile, conversationState.HiddenTags);
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

        var ranked = planningResult.RankedRecommendations
            .Take(3)
            .ToList();
        var cards = ranked.Select(scored => responseComposer.ToRecommendationCard(scored, context)).ToList();
        var defaultSuggestedReplies = responseComposer.CreateSuggestedReplies(responseMode, locale);
        var defaultMessage = responseComposer.CreateAssistantMessage(city, planningWindow.Value, ranked, responseMode, locale);
        var modelResult = await CreateModelResponseAsync(
            conversationId,
            request,
            profile,
            context,
            reservations,
            cards,
            defaultSuggestedReplies,
            promptVersion,
            cancellationToken);

        var useModelResponse = responseMode == BalancedMode
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
        conversationState.LastRecommendationIds = cards
            .Select(card => card.RecommendationId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id!)
            .ToList();

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
            null),
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
                trip.AppUserId == user.Id
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
        TravelPreferenceProfile preferences,
        string responseMode,
        string? message)
    {
        var profile = new TravelPreferenceProfile
        {
            UserId = user.Id,
            Interests = preferences.Interests.ToList(),
            FoodPreferences = preferences.FoodPreferences.ToList(),
            DietaryRestrictions = preferences.DietaryRestrictions.ToList(),
            BudgetLevel = preferences.BudgetLevel,
            TravelPace = preferences.TravelPace,
            Dislikes = preferences.Dislikes.ToList(),
            AvoidTouristTraps = preferences.AvoidTouristTraps,
            MaxWalkingMinutes = responseMode == LessWalkingMode
                ? Math.Min(preferences.MaxWalkingMinutes, 12)
                : preferences.MaxWalkingMinutes
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

using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using TravelCompanion.Api.Data;
using TravelCompanion.Api.Models;
using TravelCompanion.Shared;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Api.Services;

public sealed class TravelChatService(
    TravelCompanionDbContext dbContext,
    IUserProfileService userProfileService,
    IRecommendationRanker ranker,
    ITravelAiModelClient modelClient,
    ILogger<TravelChatService> logger) : ITravelChatService
{
    private const string Intent = "plan_between_reservations";
    private const string ViewScheduleIntent = "view_schedule";
    private const string ViewPreferencesIntent = "view_preferences";
    private const string UpdatePreferencesIntent = "update_preferences";
    private const string HelpIntent = "help";
    private const string LessWalkingMode = "less_walking";
    private const string ShorterMode = "shorter";
    private const string FoodMode = "food";
    private const string CultureMode = "culture";
    private const string CheaperMode = "cheaper";
    private const string BalancedMode = "balanced";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<TravelChatResponse> CreatePlanAsync(
        AppUser user,
        TravelChatRequest request,
        CancellationToken cancellationToken)
    {
        var conversationId = string.IsNullOrWhiteSpace(request.ConversationId)
            ? Guid.NewGuid().ToString("N")
            : request.ConversationId.Trim();
        var conversation = await LoadConversationAsync(conversationId, user.Id, cancellationToken);
        if (conversation is not null && conversation.UserId != user.Id)
        {
            conversationId = Guid.NewGuid().ToString("N");
            conversation = null;
        }

        var suppressPreferenceConfirmation = false;
        TravelPreferenceProfilePatchDto? temporaryPreferencePatch = null;
        var pendingPreferencePatch = ReadPendingPreferencePatch(conversation);
        if (pendingPreferencePatch is not null && IsPreferenceConfirmationReply(request.Message))
        {
            var originalMessage = conversation?.PendingPreferenceOriginalMessage ?? request.Message;
            if (IsPositiveConfirmation(request.Message))
            {
                var updatedProfile = await ApplyPreferencePatchAsync(user.Id, pendingPreferencePatch, cancellationToken);
                await ClearPendingPreferencePatchAsync(conversation, cancellationToken);

                if (!IsPlanningRequest(originalMessage))
                {
                    return new TravelChatResponse(
                        conversationId,
                        $"Listo, actualice tus preferencias:\n{FormatPreferenceProfile(updatedProfile)}",
                        UpdatePreferencesIntent,
                        [],
                        ["Ver mis preferencias", "Proponeme un plan"],
                        null);
                }

                request = request with { Message = originalMessage, ConversationId = conversationId };
                suppressPreferenceConfirmation = true;
            }
            else
            {
                await ClearPendingPreferencePatchAsync(conversation, cancellationToken);

                if (!IsPlanningRequest(originalMessage))
                {
                    return new TravelChatResponse(
                        conversationId,
                        "Ok, no modifique tus preferencias.",
                        ViewPreferencesIntent,
                        [],
                        ["Ver mis preferencias", "Proponeme un plan"],
                        null);
                }

                request = request with { Message = originalMessage, ConversationId = conversationId };
                suppressPreferenceConfirmation = true;
                temporaryPreferencePatch = pendingPreferencePatch;
            }
        }

        if (IsSaveIntent(request.Message))
        {
            return MissingContext(
                conversationId,
                "confirmation",
                "Para guardar un plan necesito tu confirmacion en la tarjeta recomendada.",
                ["Confirmar en la tarjeta", "Ver otro plan"]);
        }

        var baseDate = request.Date
            ?? conversation?.LastDate
            ?? DateOnly.FromDateTime(DateTime.UtcNow);
        var date = ResolveRequestedDate(request.Message, baseDate);

        if (IsHelpIntent(request.Message))
        {
            return CreateHelpResponse(conversationId);
        }

        if (IsScheduleIntent(request.Message))
        {
            return await CreateScheduleResponseAsync(user, conversationId, date, cancellationToken);
        }

        if (!suppressPreferenceConfirmation && IsPreferenceIntent(request.Message))
        {
            var preferencePatch = await CreatePreferencePatchFromMessageAsync(request.Message, cancellationToken);
            if (preferencePatch is not null)
            {
                return await CreatePreferenceConfirmationResponseAsync(
                    conversation,
                    conversationId,
                    user.Id,
                    request.Message,
                    preferencePatch,
                    cancellationToken);
            }

            return await CreatePreferenceResponseAsync(user.Id, conversationId, request.Message, cancellationToken);
        }

        var preferences = await userProfileService.GetProfileAsync(user.Id, cancellationToken);
        var effectivePreferences = temporaryPreferencePatch is null
            ? preferences
            : await CreateEffectivePreferenceProfileAsync(user, preferences, temporaryPreferencePatch, cancellationToken);

        if (!userProfileService.HasMinimumPreferences(effectivePreferences, out var missingPreferenceFields))
        {
            return MissingContext(
                conversationId,
                "preferences",
                "Antes de proponerte un plan necesito guardar al menos tus intereses, presupuesto y ritmo de viaje.",
                CreatePreferenceSuggestions(missingPreferenceFields));
        }

        if (!IsSupportedPlanningRequest(request.Message))
        {
            return MissingContext(
                conversationId,
                "assistantCommand",
                "No entendi ese pedido. Puedo proponerte planes, revisar tu agenda o ayudarte a ajustar preferencias.",
                [
                    "Que puedo pedirte",
                    "Proponeme un plan",
                    "Ver mi agenda",
                    "Ver mis preferencias",
                    "Algo con menos caminata"
                ]);
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
            return MissingContext(
                conversationId,
                "date",
                "No encontre un viaje activo para esa fecha.",
                ["Elegir otra fecha", "Ver mi agenda"]);
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
            return new TravelChatResponse(
                conversationId,
                $"No veo un espacio comodo en tu agenda del {date:dd/MM} para sumar una actividad sin apurarte.",
                Intent,
                [],
                ["Ver mi agenda", "Probar otro dia", "Ver mis preferencias"],
                null);
        }

        var unlockedRecommendations = await LoadUnlockedRecommendationsAsync(
            user,
            trips.Select(trip => trip.DestinationId).Distinct().ToList(),
            city,
            cancellationToken);

        if (unlockedRecommendations.Count == 0)
        {
            return MissingContext(
                conversationId,
                "city",
                $"No encontre recomendaciones disponibles para {city}.",
                ["Probar otra ciudad", "Ver recomendaciones"]);
        }

        var responseMode = ResolveResponseMode(request.Message, conversation?.LastResponseMode);
        var explicitRecommendationIds = ParseRecommendationIds(request.Message);
        var previousRecommendationIds = IsAlternativeRequest(request.Message)
            ? ParseRecommendationIds(conversation?.LastRecommendationIds)
            : [];
        var excludedRecommendationIds = previousRecommendationIds
            .Concat(explicitRecommendationIds)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var profile = CreateProfile(user, effectivePreferences!, responseMode, request.Message);
        var context = new TravelPlanningContext(
            city,
            date,
            planningWindow.Value.Start,
            planningWindow.Value.End,
            planningWindow.Value.AvailableMinutes,
            request.CurrentLocation);
        var rankedCandidates = ApplyResponseMode(
                ranker.Rank(profile, reservations, unlockedRecommendations, context),
                responseMode)
            .ToList();
        var dislikedFilteredCandidates = RemoveDislikedCandidates(rankedCandidates, profile.Dislikes);
        if (dislikedFilteredCandidates.Count > 0)
        {
            rankedCandidates = dislikedFilteredCandidates;
        }

        if (excludedRecommendationIds.Count > 0)
        {
            var freshCandidates = rankedCandidates
                .Where(scored => !excludedRecommendationIds.Contains(scored.Recommendation.Id.ToString()))
                .ToList();
            if (freshCandidates.Count > 0)
            {
                rankedCandidates = freshCandidates;
            }
        }

        var ranked = rankedCandidates
            .Take(3)
            .ToList();
        var cards = ranked.Select(scored => ToCard(scored, context)).ToList();
        var defaultSuggestedReplies = CreateSuggestedReplies(responseMode);
        var defaultMessage = CreateAssistantMessage(city, planningWindow.Value, ranked, responseMode);
        var modelResult = await CreateModelResponseAsync(
            conversationId,
            request,
            profile,
            context,
            reservations,
            cards,
            defaultSuggestedReplies,
            cancellationToken);

        var useModelResponse = responseMode == BalancedMode
            && modelResult is not null
            && !string.IsNullOrWhiteSpace(modelResult.Message)
            && !MentionsSavedState(modelResult.Message);

        await SaveConversationAsync(
            conversation,
            conversationId,
            user.Id,
            city,
            date,
            responseMode,
            cards,
            cancellationToken);

        return new TravelChatResponse(
            conversationId,
            useModelResponse ? modelResult!.Message : defaultMessage,
            Intent,
            cards,
            useModelResponse && modelResult!.SuggestedReplies.Count > 0
                ? modelResult.SuggestedReplies
                : defaultSuggestedReplies,
            null);
    }

    private async Task<TravelChatResponse> CreateScheduleResponseAsync(
        AppUser user,
        string conversationId,
        DateOnly date,
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
            return MissingContext(
                conversationId,
                "date",
                $"No encontre un viaje activo para el {date:dd/MM}.",
                ["Elegir otra fecha", "Proponeme un plan"]);
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
            return new TravelChatResponse(
                conversationId,
                $"El {date:dd/MM} no tenes reservas guardadas en {destinationName}. Puedo proponerte un plan libre para anticipar ese dia.",
                ViewScheduleIntent,
                [],
                [$"Proponeme planes para {date:yyyy-MM-dd}", "Ver mis preferencias"],
                null);
        }

        var lines = reservations
            .Take(5)
            .Select(reservation =>
                $"- {GetStartForDate(reservation, date):HH\\:mm}: {reservation.Title} ({reservation.City})");
        var extra = reservations.Count > 5
            ? $" Tambien hay {reservations.Count - 5} reserva(s) mas."
            : string.Empty;

        return new TravelChatResponse(
            conversationId,
            $"Tu agenda del {date:dd/MM} en {destinationName}:\n{string.Join('\n', lines)}{extra}",
            ViewScheduleIntent,
            [],
            [$"Proponeme planes para {date:yyyy-MM-dd}", "Algo con menos caminata", "Ver mis preferencias"],
            null);
    }

    private static TravelChatResponse CreateHelpResponse(string conversationId)
    {
        return new TravelChatResponse(
            conversationId,
            "Puedo ayudarte en 5 modos:\n1. Planificar: Proponeme un plan para hoy o para una fecha.\n2. Ajustar: Algo con menos caminata, mas corto u otra opcion.\n3. Agenda: Ver mi agenda.\n4. Preferencias: Ver mis preferencias o Evitar #culture.\n5. Ayuda: Que puedo pedirte.",
            HelpIntent,
            [],
            ["Proponeme un plan", "Algo con menos caminata", "Ver mi agenda", "Ver mis preferencias", "Evitar culture"],
            null);
    }

    private async Task<TravelChatResponse> CreatePreferenceResponseAsync(
        Guid userId,
        string conversationId,
        string? message,
        CancellationToken cancellationToken)
    {
        var patch = await CreatePreferencePatchFromMessageAsync(message, cancellationToken);
        var currentProfile = await userProfileService.GetProfileDtoAsync(userId, cancellationToken);
        var profile = patch is null
            ? currentProfile
            : await userProfileService.PatchProfileAsync(
                userId,
                MergePreferencePatch(currentProfile, patch),
                cancellationToken);

        var prefix = patch is null
            ? "Estas son tus preferencias guardadas:"
            : "Listo, actualice tus preferencias:";

        return new TravelChatResponse(
            conversationId,
            $"{prefix}\n{FormatPreferenceProfile(profile)}",
            patch is null ? ViewPreferencesIntent : UpdatePreferencesIntent,
            [],
            ["Cambiar intereses", "Presupuesto bajo", "Ritmo tranquilo", "Proponeme un plan"],
            null);
    }

    private async Task<TravelChatResponse> CreatePreferenceConfirmationResponseAsync(
        TravelChatConversation? conversation,
        string conversationId,
        Guid userId,
        string? message,
        TravelPreferenceProfilePatchDto patch,
        CancellationToken cancellationToken)
    {
        var isNewConversation = conversation is null;
        conversation = EnsureConversation(conversation, conversationId, userId);
        if (isNewConversation)
        {
            dbContext.TravelChatConversations.Add(conversation);
        }

        conversation.PendingPreferencePatchJson = JsonSerializer.Serialize(patch, JsonOptions);
        conversation.PendingPreferenceOriginalMessage = string.IsNullOrWhiteSpace(message)
            ? null
            : message.Trim();
        conversation.PendingPreferenceRequestedAt = DateTimeOffset.UtcNow;
        conversation.UpdatedAt = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);

        return new TravelChatResponse(
            conversationId,
            $"Detecte este posible cambio de preferencias:\n{FormatPreferencePatch(patch)}\nQueres guardarlo en tu perfil?",
            UpdatePreferencesIntent,
            [],
            ["Si, guardar preferencia", "No, solo este pedido"],
            new MissingContextDto(
                "preferenceConfirmation",
                "Confirma si queres guardar este cambio como preferencia permanente.",
                ["Si, guardar preferencia", "No, solo este pedido"]));
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

    private async Task ClearPendingPreferencePatchAsync(
        TravelChatConversation? conversation,
        CancellationToken cancellationToken)
    {
        if (conversation is null)
        {
            return;
        }

        conversation.PendingPreferencePatchJson = null;
        conversation.PendingPreferenceOriginalMessage = null;
        conversation.PendingPreferenceRequestedAt = null;
        conversation.UpdatedAt = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
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

    private static TravelChatConversation EnsureConversation(
        TravelChatConversation? conversation,
        string conversationId,
        Guid userId)
    {
        if (conversation is not null)
        {
            return conversation;
        }

        return new TravelChatConversation
        {
            Id = conversationId,
            UserId = userId,
            CreatedAt = DateTimeOffset.UtcNow
        };
    }

    private async Task<TravelAiModelResult?> CreateModelResponseAsync(
        string conversationId,
        TravelChatRequest request,
        TravelPreferenceProfile profile,
        TravelPlanningContext context,
        IReadOnlyList<Reservation> reservations,
        IReadOnlyList<TravelCardDto> cards,
        IReadOnlyList<string> suggestedReplies,
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
                    suggestedReplies),
                cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Travel AI model client failed; using deterministic chat response.");
            return null;
        }
    }

    private async Task<TravelChatConversation?> LoadConversationAsync(
        string conversationId,
        Guid userId,
        CancellationToken cancellationToken)
    {
        var conversation = await dbContext.TravelChatConversations
            .FirstOrDefaultAsync(existing => existing.Id == conversationId, cancellationToken);

        if (conversation is null || conversation.UserId == userId)
        {
            return conversation;
        }

        logger.LogWarning(
            "Ignoring travel chat conversation {ConversationId} because it belongs to another user.",
            conversationId);
        return conversation;
    }

    private async Task SaveConversationAsync(
        TravelChatConversation? conversation,
        string conversationId,
        Guid userId,
        string city,
        DateOnly date,
        string responseMode,
        IReadOnlyList<TravelCardDto> cards,
        CancellationToken cancellationToken)
    {
        if (conversation is null)
        {
            conversation = new TravelChatConversation
            {
                Id = conversationId,
                UserId = userId,
                CreatedAt = DateTimeOffset.UtcNow
            };
            dbContext.TravelChatConversations.Add(conversation);
        }

        conversation.LastCity = city;
        conversation.LastDate = date;
        conversation.LastResponseMode = responseMode;
        conversation.LastRecommendationIds = string.Join(
            ",",
            cards
                .Select(card => card.RecommendationId)
                .Where(id => !string.IsNullOrWhiteSpace(id)));
        conversation.UpdatedAt = DateTimeOffset.UtcNow;

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task<IReadOnlyList<Recommendation>> LoadUnlockedRecommendationsAsync(
        AppUser user,
        IReadOnlyCollection<Guid> destinationIds,
        string city,
        CancellationToken cancellationToken)
    {
        var recommendations = await dbContext.Recommendations
            .AsNoTracking()
            .Include(recommendation => recommendation.Packages)
            .Where(recommendation => destinationIds.Contains(recommendation.DestinationId))
            .OrderBy(recommendation => recommendation.Title)
            .ToListAsync(cancellationToken);

        var entitlements = ToEntitlementsDto(user);
        var unlocked = recommendations
            .Where(recommendation => ContentAccessPolicy.IsRecommendationUnlocked(
                entitlements,
                recommendation.AccessLevel,
                recommendation.DestinationId,
                recommendation.Packages.Select(package => package.Id).ToList()))
            .ToList();
        var cityMatches = unlocked
            .Where(recommendation =>
                recommendation.Neighborhood.Contains(city, StringComparison.OrdinalIgnoreCase)
                || recommendation.Description.Contains(city, StringComparison.OrdinalIgnoreCase))
            .ToList();

        return cityMatches.Count > 0 ? cityMatches : unlocked;
    }

    private static UserEntitlementsDto ToEntitlementsDto(AppUser user)
    {
        var now = DateTimeOffset.UtcNow;
        var activeEntitlements = user.Entitlements
            .Where(entitlement => entitlement.ExpiresAt is null || entitlement.ExpiresAt > now)
            .ToList();

        return new UserEntitlementsDto(
            user.Id,
            user.Email,
            user.DisplayName,
            activeEntitlements.Select(entitlement => entitlement.AccessLevel).Distinct().ToList(),
            activeEntitlements
                .Where(entitlement => entitlement.DestinationId.HasValue)
                .Select(entitlement => entitlement.DestinationId!.Value)
                .Distinct()
                .ToList(),
            activeEntitlements
                .Where(entitlement => entitlement.TravelPackageId.HasValue)
                .Select(entitlement => entitlement.TravelPackageId!.Value)
                .Distinct()
                .ToList(),
            activeEntitlements
                .Select(entitlement => new UserEntitlementDto(
                    entitlement.Id,
                    entitlement.AccessLevel,
                    entitlement.DestinationId,
                    entitlement.TravelPackageId,
                    entitlement.GrantedAt,
                    entitlement.ExpiresAt,
                    entitlement.Source))
                .ToList());
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

    private static void ApplyRequestSignals(
        TravelPreferenceProfile preferences,
        string? message,
        string responseMode)
    {
        var normalized = message?.Trim().ToLowerInvariant() ?? string.Empty;

        if (responseMode == FoodMode)
        {
            AddUnique(preferences.Interests, "Food");
            AddUnique(preferences.FoodPreferences, "local food");
        }

        if (responseMode == CultureMode)
        {
            AddUnique(preferences.Interests, "Culture");
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

    private static TravelCardDto ToCard(ScoredRecommendation scored, TravelPlanningContext context)
    {
        var recommendation = scored.Recommendation;
        return new TravelCardDto(
            "recommendation",
            recommendation.Title,
            FormatSubtitle(scored, recommendation),
            recommendation.Description,
            context.WindowStart?.ToString("HH:mm", CultureInfo.InvariantCulture),
            CalculateEndTime(context.WindowStart, recommendation.SuggestedDurationMinutes),
            recommendation.PriceLevel,
            scored.DistanceKm,
            scored.WalkingMinutes,
            scored.PositiveReasons.Take(3).ToList(),
            scored.NegativeReasons.ToList(),
            recommendation.Id.ToString(),
            null)
        {
            Tags = recommendation.Tags.ToList()
        };
    }

    private static string FormatSubtitle(ScoredRecommendation scored, Recommendation recommendation)
    {
        if (scored.WalkingMinutes.HasValue)
        {
            return $"{scored.WalkingMinutes.Value} min caminando · {recommendation.Neighborhood}";
        }

        return $"{recommendation.SuggestedDurationMinutes} min · {recommendation.Neighborhood}";
    }

    private static string? CalculateEndTime(TimeOnly? start, int durationMinutes)
    {
        return start?.AddMinutes(durationMinutes).ToString("HH:mm", CultureInfo.InvariantCulture);
    }

    private static string CreateAssistantMessage(
        string city,
        (TimeOnly Start, TimeOnly End, int AvailableMinutes) planningWindow,
        IReadOnlyList<ScoredRecommendation> ranked,
        string responseMode)
    {
        if (ranked.Count == 0)
        {
            return $"Tenes {planningWindow.AvailableMinutes} minutos libres en {city}, pero no encontre una opcion clara para ese pedido.";
        }

        var top = ranked[0];
        var prefix = responseMode switch
        {
            LessWalkingMode => "Busque una opcion con menos caminata",
            ShorterMode => "Busque una opcion mas corta",
            FoodMode => "Busque algo de comida local",
            CultureMode => "Busque una opcion mas cultural",
            CheaperMode => "Busque una opcion de bajo costo",
            _ => "Te propongo este plan"
        };
        var walking = top.WalkingMinutes.HasValue
            ? $" Queda a unos {top.WalkingMinutes.Value} min caminando."
            : string.Empty;

        return $"{prefix} para tu ventana de {planningWindow.Start:HH\\:mm} a {planningWindow.End:HH\\:mm} en {city}: {top.Recommendation.Title}. {top.PositiveReasons.First()}{walking}";
    }

    private static IReadOnlyList<string> CreateSuggestedReplies(string responseMode)
    {
        return responseMode switch
        {
            LessWalkingMode => ["Algo mas corto", "Algo de comida local", "Ver mi agenda", "Que puedo pedirte"],
            ShorterMode => ["Menos caminata", "Algo de comida local", "Ver mi agenda", "Otra opcion"],
            FoodMode => ["Menos caminata", "Algo cultural", "Algo mas corto", "Que puedo pedirte"],
            CultureMode => ["Algo de comida local", "Menos caminata", "Algo mas corto", "Ver mi agenda"],
            CheaperMode => ["Algo gratis", "Menos caminata", "Algo mas corto", "Ver mi agenda"],
            _ => ["Algo con menos caminata", "Algo mas corto", "Ver mi agenda", "Que puedo pedirte"]
        };
    }

    private static IEnumerable<ScoredRecommendation> ApplyResponseMode(
        IEnumerable<ScoredRecommendation> ranked,
        string responseMode)
    {
        return responseMode switch
        {
            LessWalkingMode => ranked
                .OrderBy(scored => scored.WalkingMinutes ?? scored.Recommendation.SuggestedDurationMinutes)
                .ThenByDescending(scored => scored.Score)
                .ThenBy(scored => scored.Recommendation.Title),
            ShorterMode => ranked
                .OrderBy(scored => scored.Recommendation.SuggestedDurationMinutes)
                .ThenByDescending(scored => scored.Score)
                .ThenBy(scored => scored.Recommendation.Title),
            FoodMode => ranked
                .OrderByDescending(scored => IsFoodRecommendation(scored.Recommendation))
                .ThenByDescending(scored => scored.Score)
                .ThenBy(scored => scored.Recommendation.Title),
            CultureMode => ranked
                .OrderByDescending(scored => IsCultureRecommendation(scored.Recommendation))
                .ThenByDescending(scored => scored.Score)
                .ThenBy(scored => scored.Recommendation.Title),
            CheaperMode => ranked
                .OrderBy(scored => scored.Recommendation.AccessLevel == ContentAccessLevel.Free ? 0 : 1)
                .ThenByDescending(scored => scored.Score)
                .ThenBy(scored => scored.Recommendation.Title),
            _ => ranked
        };
    }

    private static bool IsFoodRecommendation(Recommendation recommendation)
    {
        return ContainsAny(
            $"{recommendation.Category} {recommendation.Title} {recommendation.Description} {string.Join(' ', recommendation.Tags)}",
            "food",
            "comida",
            "snack",
            "restaurant",
            "restaurante",
            "cafe",
            "café",
            "sake");
    }

    private static bool IsCultureRecommendation(Recommendation recommendation)
    {
        return ContainsAny(
            $"{recommendation.Category} {recommendation.Title} {recommendation.Description} {string.Join(' ', recommendation.Tags)}",
            "culture",
            "cultura",
            "museum",
            "museo",
            "history",
            "historia",
            "arte");
    }

    private static string ResolveResponseMode(string? message, string? previousResponseMode)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return string.IsNullOrWhiteSpace(previousResponseMode)
                ? BalancedMode
                : previousResponseMode;
        }

        var normalized = message.Trim().ToLowerInvariant();
        if (IsAlternativeRequest(normalized))
        {
            return string.IsNullOrWhiteSpace(previousResponseMode)
                ? BalancedMode
                : previousResponseMode;
        }

        if (ContainsAny(normalized, "menos caminata", "caminar menos", "poca caminata", "cerca", "nearby"))
        {
            return LessWalkingMode;
        }

        if (ContainsAny(normalized, "mas corto", "más corto", "rapido", "rápido", "poco tiempo", "corta"))
        {
            return ShorterMode;
        }

        if (ContainsAny(normalized, "comida", "food", "local", "snack", "restaurante", "cafe", "café"))
        {
            return FoodMode;
        }

        if (ContainsAny(normalized, "cultura", "cultural", "museo", "historia", "arte"))
        {
            return CultureMode;
        }

        if (ContainsAny(normalized, "barato", "gratis", "economico", "económico", "free"))
        {
            return CheaperMode;
        }

        return BalancedMode;
    }

    private static bool ContainsAny(string value, params string[] candidates)
    {
        return candidates.Any(candidate => value.Contains(candidate, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsScheduleIntent(string? message)
    {
        return !string.IsNullOrWhiteSpace(message)
            && ContainsAny(
                message.Trim().ToLowerInvariant(),
                "ver mi agenda",
                "ver agenda",
                "mi agenda",
                "ver mis reservas",
                "mostrar mis reservas",
                "mis schedules",
                "schedule");
    }

    private static bool IsHelpIntent(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return false;
        }

        var normalized = RemoveDiacritics(message.Trim()).ToLowerInvariant();
        return ContainsAny(
            normalized,
            "que puedo pedirte",
            "ayuda",
            "comandos",
            "help",
            "what can i ask",
            "what can you do");
    }

    private static bool IsPreferenceIntent(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return false;
        }

        var normalized = message.Trim().ToLowerInvariant();
        return ContainsAny(
            normalized,
            "preferencias",
            "mis gustos",
            "mi perfil",
            "cambiar presupuesto",
            "cambia presupuesto",
            "actualizar presupuesto",
            "actualiza presupuesto",
            "mi presupuesto",
            "cambiar ritmo",
            "cambia ritmo",
            "actualizar ritmo",
            "actualiza ritmo",
            "mi ritmo",
            "cambiar intereses",
            "cambia intereses",
            "actualizar intereses",
            "actualiza intereses",
            "mis intereses",
            "prefiero",
            "me gusta",
            "no me gusta",
            "no quiero",
            "evita",
            "evite",
            "evitando",
            "evitar",
            "avoid",
            "soy vegetariano",
            "soy vegetariana",
            "soy celiaco",
            "soy celiaca",
            "tengo celiaquia",
            "sin gluten");
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

    private static bool IsSaveIntent(string? message)
    {
        return !string.IsNullOrWhiteSpace(message)
            && ContainsAny(
                message.Trim().ToLowerInvariant(),
                "guardar plan",
                "guarda el plan",
                "guardá el plan",
                "save plan",
                "save itinerary",
                "guardar itinerario");
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

    private async Task<TravelPreferenceProfilePatchDto?> CreatePreferencePatchFromMessageAsync(
        string? message,
        CancellationToken cancellationToken)
    {
        var knownTags = await LoadKnownRecommendationTagsAsync(cancellationToken);
        return CreatePreferencePatchFromMessage(message, knownTags);
    }

    private async Task<IReadOnlySet<string>> LoadKnownRecommendationTagsAsync(CancellationToken cancellationToken)
    {
        var recommendations = await dbContext.Recommendations
            .AsNoTracking()
            .Select(recommendation => new
            {
                recommendation.Category,
                recommendation.Tags
            })
            .ToListAsync(cancellationToken);
        var tags = recommendations
            .SelectMany(recommendation => recommendation.Tags.Append(recommendation.Category))
            .Select(NormalizePreferenceToken)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return tags;
    }

    private static TravelPreferenceProfilePatchDto? CreatePreferencePatchFromMessage(
        string? message,
        IReadOnlySet<string> knownRecommendationTags)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return null;
        }

        var normalized = RemoveDiacritics(message).ToLowerInvariant();
        var interests = new List<string>();
        var foodPreferences = new List<string>();
        var dietaryRestrictions = new List<string>();
        var dislikes = new List<string>();
        string? budgetLevel = null;
        string? travelPace = null;
        int? maxWalkingMinutes = null;
        var hasAvoidSignal = HasAvoidSignal(normalized);

        if (ContainsAny(normalized, "presupuesto bajo", "barato", "economico", "gratis"))
        {
            budgetLevel = ContainsAny(normalized, "gratis") ? "free" : "low";
        }
        else if (ContainsAny(normalized, "presupuesto alto", "premium", "caro"))
        {
            budgetLevel = "high";
        }
        else if (ContainsAny(normalized, "presupuesto medio", "moderado"))
        {
            budgetLevel = "medium";
        }

        if (ContainsAny(normalized, "ritmo tranquilo", "ritmo relajado", "sin apuro", "poca caminata"))
        {
            travelPace = "relaxed";
        }
        else if (ContainsAny(normalized, "ritmo rapido", "ritmo eficiente", "aprovechar mucho"))
        {
            travelPace = "efficient";
        }
        else if (ContainsAny(normalized, "ritmo balanceado", "ritmo equilibrado"))
        {
            travelPace = "balanced";
        }

        if (ContainsAny(normalized, "menos caminata", "poca caminata", "caminar poco"))
        {
            maxWalkingMinutes = 12;
        }

        if (!hasAvoidSignal && ContainsAny(normalized, "comida", "gastronomia", "restaurante", "cafe", "food"))
        {
            interests.Add("Food");
            foodPreferences.Add("local food");
        }

        if (!hasAvoidSignal && ContainsAny(normalized, "cultura", "culture", "museo", "museum", "historia", "history", "arte", "art"))
        {
            interests.Add("Culture");
        }

        if (!hasAvoidSignal && ContainsAny(normalized, "compras", "shopping", "tiendas"))
        {
            interests.Add("Shopping");
        }

        if (!hasAvoidSignal && ContainsAny(normalized, "barrio", "barrios", "neighborhood"))
        {
            interests.Add("Neighborhood");
        }

        if (ContainsAny(normalized, "vegetariano", "vegetariana", "vegetarian"))
        {
            dietaryRestrictions.Add("vegetarian");
        }

        if (ContainsAny(normalized, "sin gluten", "gluten free", "celiaco", "celiaca"))
        {
            dietaryRestrictions.Add("gluten-free");
        }

        if (ContainsAny(normalized, "no me gusta museos", "sin museos", "evitar museos"))
        {
            dislikes.Add("museum");
        }

        if (ContainsAny(normalized, "no me gusta shopping", "sin shopping", "evitar compras"))
        {
            dislikes.Add("shopping");
        }

        foreach (var dislikedTag in ExtractDislikedTags(normalized, knownRecommendationTags))
        {
            AddUnique(dislikes, dislikedTag);
        }

        if (budgetLevel is null
            && travelPace is null
            && maxWalkingMinutes is null
            && interests.Count == 0
            && foodPreferences.Count == 0
            && dietaryRestrictions.Count == 0
            && dislikes.Count == 0)
        {
            return null;
        }

        return new TravelPreferenceProfilePatchDto(
            foodPreferences.Count == 0 ? null : foodPreferences,
            dietaryRestrictions.Count == 0 ? null : dietaryRestrictions,
            budgetLevel,
            travelPace,
            interests.Count == 0 ? null : interests,
            dislikes.Count == 0 ? null : dislikes,
            null,
            maxWalkingMinutes);
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

    private static List<ScoredRecommendation> RemoveDislikedCandidates(
        IReadOnlyList<ScoredRecommendation> candidates,
        IReadOnlyList<string> dislikes)
    {
        if (dislikes.Count == 0)
        {
            return candidates.ToList();
        }

        return candidates
            .Where(candidate => !dislikes.Any(dislike =>
                !string.IsNullOrWhiteSpace(dislike)
                && CreateRecommendationSearchableText(candidate.Recommendation)
                    .Contains(dislike, StringComparison.OrdinalIgnoreCase)))
            .ToList();
    }

    private static string CreateRecommendationSearchableText(Recommendation recommendation)
    {
        return string.Join(
            ' ',
            [
                recommendation.Title,
                recommendation.Category,
                recommendation.Neighborhood,
                recommendation.Description,
                .. recommendation.Tags
            ]);
    }

    private static TravelPreferenceProfilePatchDto? ReadPendingPreferencePatch(TravelChatConversation? conversation)
    {
        if (string.IsNullOrWhiteSpace(conversation?.PendingPreferencePatchJson))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<TravelPreferenceProfilePatchDto>(
                conversation.PendingPreferencePatchJson,
                JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool IsPreferenceConfirmationReply(string? message)
    {
        return IsPositiveConfirmation(message) || IsNegativeConfirmation(message);
    }

    private static bool IsPositiveConfirmation(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return false;
        }

        var normalized = RemoveDiacritics(message).Trim().ToLowerInvariant();
        return normalized is "si" or "ok" or "dale" or "confirmo"
            || ContainsAny(normalized, "si guardar", "guardar preferencia", "confirmar", "confirmo", "yes", "save preference");
    }

    private static bool IsNegativeConfirmation(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return false;
        }

        var normalized = RemoveDiacritics(message).Trim().ToLowerInvariant();
        return normalized == "no"
            || normalized.StartsWith("no ", StringComparison.Ordinal)
            || ContainsAny(normalized, "solo este pedido", "no guardar", "no lo guardes", "dont save", "do not save");
    }

    private static bool IsPlanningRequest(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return false;
        }

        var normalized = RemoveDiacritics(message).ToLowerInvariant();
        return ContainsAny(
            normalized,
            "plan",
            "planes",
            "propon",
            "recomend",
            "suger",
            "que hago",
            "que puedo hacer",
            "algo para hacer");
    }

    private static bool IsSupportedPlanningRequest(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return false;
        }

        var normalized = RemoveDiacritics(message).ToLowerInvariant();
        return ContainsAny(
            normalized,
            "plan",
            "planes",
            "propon",
            "recomend",
            "suger",
            "que hago",
            "que puedo hacer",
            "algo para hacer",
            "que puedo pedirte",
            "ayuda",
            "comandos",
            "menos caminata",
            "caminar menos",
            "mas corto",
            "otra opcion",
            "otra alternativa",
            "reemplazar",
            "replace",
            "algo distinto",
            "comida",
            "food",
            "cultura",
            "culture",
            "barato",
            "gratis",
            "economico");
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

    private static string FormatPreferencePatch(TravelPreferenceProfilePatchDto patch)
    {
        var lines = new List<string>();
        if (patch.Interests is { Count: > 0 })
        {
            lines.Add($"- Intereses: {FormatList(patch.Interests)}");
        }

        if (patch.Dislikes is { Count: > 0 })
        {
            lines.Add($"- Evitar: {FormatList(patch.Dislikes)}");
        }

        if (patch.FoodPreferences is { Count: > 0 })
        {
            lines.Add($"- Comida: {FormatList(patch.FoodPreferences)}");
        }

        if (patch.DietaryRestrictions is { Count: > 0 })
        {
            lines.Add($"- Requisitos alimentarios: {FormatList(patch.DietaryRestrictions)}");
        }

        if (!string.IsNullOrWhiteSpace(patch.BudgetLevel))
        {
            lines.Add($"- Presupuesto: {patch.BudgetLevel}");
        }

        if (!string.IsNullOrWhiteSpace(patch.TravelPace))
        {
            lines.Add($"- Ritmo: {patch.TravelPace}");
        }

        if (patch.MaxWalkingMinutes.HasValue)
        {
            lines.Add($"- Caminata maxima: {patch.MaxWalkingMinutes.Value} min");
        }

        return lines.Count == 0
            ? "- Sin cambios detectados"
            : string.Join('\n', lines);
    }

    private static bool HasAvoidSignal(string normalized)
    {
        return ContainsAny(
            normalized,
            "evitar",
            "avoid",
            "no me gusta",
            "no quiero",
            "evita",
            "evite",
            "evitando",
            "sin museos",
            "sin museo",
            "sin cultura",
            "sin culture",
            "sin shopping",
            "sin compras");
    }

    private static IReadOnlyList<string> ExtractDislikedTags(
        string normalized,
        IReadOnlySet<string> knownRecommendationTags)
    {
        if (!HasAvoidSignal(normalized))
        {
            return [];
        }

        var dislikedTags = new List<string>();
        var aliases = new (string Tag, string[] Terms)[]
        {
            ("culture", ["culture", "cultura", "cultural"]),
            ("museum", ["museum", "museo", "museos"]),
            ("history", ["history", "historia"]),
            ("art", ["art", "arte"]),
            ("food", ["food", "comida", "gastronomia", "gastronomia", "restaurante", "restaurant"]),
            ("cafe", ["cafe"]),
            ("snacks", ["snack", "snacks"]),
            ("shopping", ["shopping", "compras", "tiendas"]),
            ("neighborhood", ["neighborhood", "barrio", "barrios"]),
            ("vegetarian", ["vegetarian", "vegetariano", "vegetariana"]),
            ("vegan", ["vegan", "vegano", "vegana"])
        };

        foreach (var (tag, terms) in aliases)
        {
            if (terms.Any(term => ContainsAvoidedTerm(normalized, term)))
            {
                AddUnique(dislikedTags, tag);
            }
        }

        foreach (var tag in knownRecommendationTags)
        {
            if (ContainsAvoidedTerm(normalized, tag))
            {
                AddUnique(dislikedTags, tag);
            }
        }

        return dislikedTags;
    }

    private static bool ContainsAvoidedTerm(string normalized, string term)
    {
        return ContainsAny(
            normalized,
            $"evitar {term}",
            $"evita {term}",
            $"evite {term}",
            $"evitando {term}",
            $"avoid {term}",
            $"no me gusta {term}",
            $"no quiero {term}",
            $"sin {term}");
    }

    private static string NormalizePreferenceToken(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return RemoveDiacritics(value)
            .Trim()
            .ToLowerInvariant();
    }

    private static string FormatPreferenceProfile(TravelPreferenceProfileDto profile)
    {
        return string.Join(
            '\n',
            [
                $"- Intereses: {FormatList(profile.Interests)}",
                $"- Comida: {FormatList(profile.FoodPreferences)}",
                $"- Restricciones: {FormatList(profile.DietaryRestrictions)}",
                $"- Presupuesto: {profile.BudgetLevel}",
                $"- Ritmo: {profile.TravelPace}",
                $"- Max. caminata: {profile.MaxWalkingMinutes} min",
                $"- Evitar: {FormatList(profile.Dislikes)}"
            ]);
    }

    private static string FormatList(IReadOnlyList<string> values)
    {
        return values.Count == 0 ? "sin definir" : string.Join(", ", values);
    }

    private static DateOnly ResolveRequestedDate(string? message, DateOnly fallbackDate)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return fallbackDate;
        }

        var normalized = RemoveDiacritics(message).ToLowerInvariant();
        if (ContainsAny(normalized, "pasado manana"))
        {
            return fallbackDate.AddDays(2);
        }

        if (ContainsAny(normalized, "manana"))
        {
            return fallbackDate.AddDays(1);
        }

        if (ContainsAny(normalized, "hoy"))
        {
            return fallbackDate;
        }

        var isoMatch = Regex.Match(normalized, @"\b(?<year>\d{4})-(?<month>\d{1,2})-(?<day>\d{1,2})\b");
        if (isoMatch.Success
            && TryCreateDate(
                int.Parse(isoMatch.Groups["year"].Value, CultureInfo.InvariantCulture),
                int.Parse(isoMatch.Groups["month"].Value, CultureInfo.InvariantCulture),
                int.Parse(isoMatch.Groups["day"].Value, CultureInfo.InvariantCulture),
                out var isoDate))
        {
            return isoDate;
        }

        var slashMatch = Regex.Match(normalized, @"\b(?<day>\d{1,2})[/-](?<month>\d{1,2})(?:[/-](?<year>\d{2,4}))?\b");
        if (slashMatch.Success)
        {
            var year = slashMatch.Groups["year"].Success
                ? NormalizeYear(int.Parse(slashMatch.Groups["year"].Value, CultureInfo.InvariantCulture))
                : fallbackDate.Year;
            if (TryCreateDate(
                year,
                int.Parse(slashMatch.Groups["month"].Value, CultureInfo.InvariantCulture),
                int.Parse(slashMatch.Groups["day"].Value, CultureInfo.InvariantCulture),
                out var slashDate))
            {
                return slashDate;
            }
        }

        var monthMatch = Regex.Match(
            normalized,
            @"\b(?<day>\d{1,2})\s+de\s+(?<month>[a-z]+)(?:\s+de\s+(?<year>\d{4}))?\b");
        if (monthMatch.Success
            && TryParseSpanishMonth(monthMatch.Groups["month"].Value, out var monthNumber))
        {
            var year = monthMatch.Groups["year"].Success
                ? int.Parse(monthMatch.Groups["year"].Value, CultureInfo.InvariantCulture)
                : fallbackDate.Year;
            if (TryCreateDate(
                year,
                monthNumber,
                int.Parse(monthMatch.Groups["day"].Value, CultureInfo.InvariantCulture),
                out var monthDate))
            {
                return monthDate;
            }
        }

        return fallbackDate;
    }

    private static bool TryCreateDate(int year, int month, int day, out DateOnly date)
    {
        try
        {
            date = new DateOnly(year, month, day);
            return true;
        }
        catch (ArgumentOutOfRangeException)
        {
            date = default;
            return false;
        }
    }

    private static int NormalizeYear(int year)
    {
        return year < 100 ? 2000 + year : year;
    }

    private static bool TryParseSpanishMonth(string value, out int month)
    {
        month = value switch
        {
            "enero" => 1,
            "febrero" => 2,
            "marzo" => 3,
            "abril" => 4,
            "mayo" => 5,
            "junio" => 6,
            "julio" => 7,
            "agosto" => 8,
            "septiembre" or "setiembre" => 9,
            "octubre" => 10,
            "noviembre" => 11,
            "diciembre" => 12,
            _ => 0
        };

        return month > 0;
    }

    private static string RemoveDiacritics(string value)
    {
        var normalized = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(capacity: normalized.Length);
        foreach (var character in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(character);
            }
        }

        return builder.ToString().Normalize(NormalizationForm.FormC);
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

    private static TravelChatResponse MissingContext(
        string conversationId,
        string field,
        string message,
        IReadOnlyList<string> suggestions)
    {
        return new TravelChatResponse(
            conversationId,
            message,
            Intent,
            [],
            suggestions,
            new MissingContextDto(field, message, suggestions));
    }

    private static IReadOnlyList<string> CreatePreferenceSuggestions(IReadOnlyList<string> missingFields)
    {
        var suggestions = new List<string>();

        if (missingFields.Contains("interests", StringComparer.OrdinalIgnoreCase))
        {
            suggestions.Add("Guardar intereses");
        }

        if (missingFields.Contains("budgetLevel", StringComparer.OrdinalIgnoreCase))
        {
            suggestions.Add("Definir presupuesto");
        }

        if (missingFields.Contains("travelPace", StringComparer.OrdinalIgnoreCase))
        {
            suggestions.Add("Definir ritmo");
        }

        suggestions.Add("Completar preferencias");
        return suggestions.Distinct(StringComparer.OrdinalIgnoreCase).Take(4).ToList();
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

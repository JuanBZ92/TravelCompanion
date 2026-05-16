using System.Globalization;
using Microsoft.EntityFrameworkCore;
using TravelCompanion.Api.Data;
using TravelCompanion.Api.Models;
using TravelCompanion.Shared;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Api.Services;

public sealed class TravelChatService(
    TravelCompanionDbContext dbContext,
    IRecommendationRanker ranker,
    ITravelAiModelClient modelClient,
    ILogger<TravelChatService> logger) : ITravelChatService
{
    private const string Intent = "plan_between_reservations";
    private const string LessWalkingMode = "less_walking";
    private const string ShorterMode = "shorter";
    private const string FoodMode = "food";
    private const string CultureMode = "culture";
    private const string CheaperMode = "cheaper";
    private const string BalancedMode = "balanced";

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

        var date = request.Date
            ?? conversation?.LastDate
            ?? DateOnly.FromDateTime(DateTime.UtcNow);

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

        if (reservations.Count == 0)
        {
            return MissingContext(
                conversationId,
                "date",
                "No encontre reservas existentes para planificar alrededor de esa fecha.",
                ["Probar otro dia", "Ver mi agenda"]);
        }

        var city = ResolveCity(request.City ?? conversation?.LastCity, reservations, trips);
        var planningWindow = FindPlanningWindow(reservations, date);
        if (planningWindow is null)
        {
            return new TravelChatResponse(
                conversationId,
                "No veo un espacio comodo entre tus reservas para sumar una actividad sin apurarte.",
                Intent,
                [],
                ["Ver agenda", "Probar otro dia"],
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
        var preferences = await LoadOrCreatePreferencesAsync(user.Id, cancellationToken);
        ApplyPreferenceSignals(preferences, request.Message, responseMode);
        var profile = CreateProfile(user, preferences, responseMode);
        var context = new TravelPlanningContext(
            city,
            date,
            planningWindow.Value.Start,
            planningWindow.Value.End,
            planningWindow.Value.AvailableMinutes,
            request.CurrentLocation);
        var ranked = ApplyResponseMode(
                ranker.Rank(profile, reservations, unlockedRecommendations, context),
                responseMode)
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
            && !string.IsNullOrWhiteSpace(modelResult.Message);

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

    private async Task<TravelerPreference> LoadOrCreatePreferencesAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        var preferences = await dbContext.TravelerPreferences
            .FirstOrDefaultAsync(preference => preference.UserId == userId, cancellationToken);

        if (preferences is not null)
        {
            return preferences;
        }

        preferences = new TravelerPreference
        {
            UserId = userId,
            Interests = ["Food", "Culture", "Coffee", "Shopping", "Neighborhood"],
            FoodPreferences = ["local food", "snacks"],
            BudgetLevel = "medium",
            TravelPace = "balanced",
            MaxWalkingMinutes = 25
        };
        dbContext.TravelerPreferences.Add(preferences);
        return preferences;
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
        TravelerPreference preferences,
        string responseMode)
    {
        return new TravelPreferenceProfile
        {
            UserId = user.Id.ToString(),
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
    }

    private static void ApplyPreferenceSignals(
        TravelerPreference preferences,
        string? message,
        string responseMode)
    {
        var changed = false;
        var normalized = message?.Trim().ToLowerInvariant() ?? string.Empty;

        if (responseMode == FoodMode)
        {
            changed |= AddUnique(preferences.Interests, "Food");
            changed |= AddUnique(preferences.FoodPreferences, "local food");
        }

        if (responseMode == CultureMode)
        {
            changed |= AddUnique(preferences.Interests, "Culture");
        }

        if (responseMode == LessWalkingMode)
        {
            if (preferences.MaxWalkingMinutes > 12)
            {
                preferences.MaxWalkingMinutes = 12;
                changed = true;
            }

            if (!string.Equals(preferences.TravelPace, "relaxed", StringComparison.Ordinal))
            {
                preferences.TravelPace = "relaxed";
                changed = true;
            }
        }

        if (responseMode == ShorterMode
            && !string.Equals(preferences.TravelPace, "efficient", StringComparison.Ordinal))
        {
            preferences.TravelPace = "efficient";
            changed = true;
        }

        if (responseMode == CheaperMode
            && !string.Equals(preferences.BudgetLevel, "low", StringComparison.Ordinal))
        {
            preferences.BudgetLevel = "low";
            changed = true;
        }

        if (ContainsAny(normalized, "vegetariano", "vegetariana", "vegetarian"))
        {
            changed |= AddUnique(preferences.DietaryRestrictions, "vegetarian");
        }

        if (ContainsAny(normalized, "sin gluten", "gluten free", "celiaco", "celiaca", "celiac"))
        {
            changed |= AddUnique(preferences.DietaryRestrictions, "gluten-free");
        }

        if (ContainsAny(normalized, "sin museos", "no museos", "evitar museos"))
        {
            changed |= AddUnique(preferences.Dislikes, "museum");
        }

        if (ContainsAny(normalized, "sin shopping", "no shopping", "evitar compras"))
        {
            changed |= AddUnique(preferences.Dislikes, "shopping");
        }

        if (changed)
        {
            preferences.UpdatedAt = DateTimeOffset.UtcNow;
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
            recommendation.AccessLevel == ContentAccessLevel.Free ? "free" : "medium",
            scored.DistanceKm,
            scored.WalkingMinutes,
            scored.PositiveReasons.Take(3).ToList(),
            scored.NegativeReasons.ToList(),
            recommendation.Id.ToString(),
            null);
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
            LessWalkingMode => ["Algo mas corto", "Algo de comida local", "Ver mi agenda", "Otra opcion"],
            ShorterMode => ["Menos caminata", "Algo de comida local", "Ver mi agenda", "Otra opcion"],
            FoodMode => ["Menos caminata", "Algo cultural", "Algo mas corto", "Ver mi agenda"],
            CultureMode => ["Algo de comida local", "Menos caminata", "Algo mas corto", "Ver mi agenda"],
            CheaperMode => ["Algo gratis", "Menos caminata", "Algo mas corto", "Ver mi agenda"],
            _ => ["Algo con menos caminata", "Algo mas corto", "Algo de comida local", "Ver mi agenda"]
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
            $"{recommendation.Category} {recommendation.Title} {recommendation.Description}",
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
            $"{recommendation.Category} {recommendation.Title} {recommendation.Description}",
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
        if (ContainsAny(normalized, "otra opcion", "otra opción", "otra alternativa", "algo distinto"))
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

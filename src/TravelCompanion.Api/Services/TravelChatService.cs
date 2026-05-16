using System.Globalization;
using Microsoft.EntityFrameworkCore;
using TravelCompanion.Api.Data;
using TravelCompanion.Api.Models;
using TravelCompanion.Shared;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Api.Services;

public sealed class TravelChatService(
    TravelCompanionDbContext dbContext,
    IRecommendationRanker ranker) : ITravelChatService
{
    private const string Intent = "plan_between_reservations";

    public async Task<TravelChatResponse> CreatePlanAsync(
        AppUser user,
        TravelChatRequest request,
        CancellationToken cancellationToken)
    {
        var conversationId = string.IsNullOrWhiteSpace(request.ConversationId)
            ? Guid.NewGuid().ToString("N")
            : request.ConversationId.Trim();
        var date = request.Date ?? DateOnly.FromDateTime(DateTime.UtcNow);

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

        var city = ResolveCity(request.City, reservations, trips);
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

        var profile = CreateDefaultProfile(user);
        var context = new TravelPlanningContext(
            city,
            date,
            planningWindow.Value.Start,
            planningWindow.Value.End,
            planningWindow.Value.AvailableMinutes,
            request.CurrentLocation);
        var ranked = ranker
            .Rank(profile, reservations, unlockedRecommendations, context)
            .Take(3)
            .ToList();
        var cards = ranked.Select(scored => ToCard(scored, context)).ToList();

        return new TravelChatResponse(
            conversationId,
            CreateAssistantMessage(city, planningWindow.Value, ranked),
            Intent,
            cards,
            ["Algo con menos caminata", "Algo mas corto", "Ver mi agenda"],
            null);
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

    private static TravelPreferenceProfile CreateDefaultProfile(AppUser user)
    {
        return new TravelPreferenceProfile
        {
            UserId = user.Id.ToString(),
            Interests = ["Food", "Culture", "Coffee", "Shopping", "Neighborhood"],
            FoodPreferences = ["local food", "snacks"],
            BudgetLevel = "medium",
            TravelPace = "balanced",
            MaxWalkingMinutes = 25
        };
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
        IReadOnlyList<ScoredRecommendation> ranked)
    {
        if (ranked.Count == 0)
        {
            return $"Tenes {planningWindow.AvailableMinutes} minutos libres en {city}, pero no encontre una opcion clara para recomendar.";
        }

        var top = ranked[0];
        return $"Tenes {planningWindow.AvailableMinutes} minutos libres en {city}, de {planningWindow.Start:HH\\:mm} a {planningWindow.End:HH\\:mm}. Te propongo {top.Recommendation.Title}: {top.PositiveReasons.First()}";
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

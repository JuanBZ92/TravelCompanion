using TravelCompanion.Api.Models;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Api.Services;

public sealed class DeterministicRecommendationRanker : IRecommendationRanker
{
    public IReadOnlyList<ScoredRecommendation> Rank(
        TravelPreferenceProfile profile,
        IReadOnlyList<Reservation> reservations,
        IReadOnlyList<Recommendation> recommendations,
        TravelPlanningContext context)
    {
        return recommendations
            .Select(recommendation => ScoreRecommendation(profile, reservations, recommendation, context))
            .OrderByDescending(scored => scored.Score)
            .ThenBy(scored => scored.WalkingMinutes ?? int.MaxValue)
            .ThenBy(scored => scored.Recommendation.Title)
            .ToList();
    }

    private static ScoredRecommendation ScoreRecommendation(
        TravelPreferenceProfile profile,
        IReadOnlyList<Reservation> reservations,
        Recommendation recommendation,
        TravelPlanningContext context)
    {
        var score = 0d;
        var positives = new List<string>();
        var negatives = new List<string>();
        var searchableText = CreateSearchableText(recommendation);

        if (MatchesCity(recommendation, context.City))
        {
            score += 25;
            positives.Add($"Esta en la zona de {context.City}.");
        }

        if (MatchesAny(searchableText, profile.Interests))
        {
            score += 16;
            positives.Add("Encaja con tus intereses guardados.");
        }

        if (MatchesAny(searchableText, profile.FoodPreferences))
        {
            score += 12;
            positives.Add("Coincide con tus preferencias de comida.");
        }

        if (IsFoodRecommendation(searchableText))
        {
            score += 10;
            positives.Add("Suma una pausa gastronomica liviana al plan.");
        }

        ApplyBudgetScore(profile, recommendation, positives, negatives, ref score);
        ApplyDietaryRestrictionScore(profile, recommendation, searchableText, positives, negatives, ref score);
        ApplyRatingScore(recommendation, positives, negatives, ref score);
        ApplyOpeningHoursScore(recommendation, context, positives, negatives, ref score);
        ApplyDuplicateScore(reservations, recommendation, negatives, ref score);

        if (context.AvailableMinutes.HasValue)
        {
            var usableMinutes = Math.Max(0, context.AvailableMinutes.Value - 20);
            if (recommendation.SuggestedDurationMinutes <= usableMinutes)
            {
                score += 18;
                positives.Add("Entra comodo en el tiempo libre disponible.");
            }
            else
            {
                score -= 22;
                negatives.Add("Puede quedar justo para el espacio entre reservas.");
            }
        }

        double? distanceKm = context.CurrentLocation is null
            ? null
            : CalculateDistanceKm(
                context.CurrentLocation.Latitude,
                context.CurrentLocation.Longitude,
                recommendation.Latitude,
                recommendation.Longitude);
        if (distanceKm > 100)
        {
            distanceKm = null;
        }

        int? walkingMinutes = distanceKm.HasValue
            ? Math.Max(1, (int)Math.Ceiling(distanceKm.Value * 12))
            : null;

        if (walkingMinutes.HasValue)
        {
            if (walkingMinutes.Value <= profile.MaxWalkingMinutes)
            {
                score += 20;
                positives.Add($"Queda a unos {walkingMinutes.Value} minutos caminando.");
            }
            else
            {
                score -= 12;
                negatives.Add($"Requiere cerca de {walkingMinutes.Value} minutos caminando.");
            }
        }

        if (MatchesAny(searchableText, profile.Dislikes))
        {
            score -= 30;
            negatives.Add("Coincide con algo que preferis evitar.");
        }

        if (positives.Count == 0)
        {
            positives.Add("Es una opcion disponible para tu viaje.");
        }

        return new ScoredRecommendation(
            recommendation,
            score,
            distanceKm,
            walkingMinutes,
            positives,
            negatives);
    }

    private static void ApplyBudgetScore(
        TravelPreferenceProfile profile,
        Recommendation recommendation,
        List<string> positives,
        List<string> negatives,
        ref double score)
    {
        var profileBudget = BudgetRank(profile.BudgetLevel);
        var recommendationBudget = BudgetRank(recommendation.PriceLevel);

        if (recommendationBudget <= profileBudget)
        {
            score += 10;
            positives.Add("Respeta tu presupuesto guardado.");
            return;
        }

        var penalty = recommendationBudget - profileBudget >= 2 ? 20 : 12;
        score -= penalty;
        negatives.Add("Puede quedar por encima de tu presupuesto.");
    }

    private static void ApplyDietaryRestrictionScore(
        TravelPreferenceProfile profile,
        Recommendation recommendation,
        string searchableText,
        List<string> positives,
        List<string> negatives,
        ref double score)
    {
        if (profile.DietaryRestrictions.Count == 0 || !IsFoodRecommendation(searchableText))
        {
            return;
        }

        if (MatchesAny(searchableText, profile.DietaryRestrictions))
        {
            score += 12;
            positives.Add("Tiene senales compatibles con tus restricciones alimentarias.");
            return;
        }

        var conflicts = profile.DietaryRestrictions.Any(restriction =>
            HasDietaryConflict(restriction, searchableText, recommendation.Tags));
        if (!conflicts)
        {
            return;
        }

        score -= 28;
        negatives.Add("Puede chocar con tus restricciones alimentarias.");
    }

    private static void ApplyRatingScore(
        Recommendation recommendation,
        List<string> positives,
        List<string> negatives,
        ref double score)
    {
        if (!recommendation.Rating.HasValue)
        {
            return;
        }

        if (recommendation.Rating.Value >= 4.5)
        {
            score += 8;
            positives.Add("Tiene muy buena valoracion.");
            return;
        }

        if (recommendation.Rating.Value >= 4)
        {
            score += 5;
            positives.Add("Tiene buena valoracion.");
            return;
        }

        if (recommendation.Rating.Value < 3.5)
        {
            score -= 8;
            negatives.Add("Su valoracion es mas baja que otras opciones.");
        }
    }

    private static void ApplyOpeningHoursScore(
        Recommendation recommendation,
        TravelPlanningContext context,
        List<string> positives,
        List<string> negatives,
        ref double score)
    {
        if (string.IsNullOrWhiteSpace(recommendation.OpeningHours)
            || !context.WindowStart.HasValue)
        {
            return;
        }

        var visitEnd = context.WindowStart.Value.AddMinutes(recommendation.SuggestedDurationMinutes);
        if (IsOpenDuring(recommendation.OpeningHours, context.WindowStart.Value, visitEnd))
        {
            score += 8;
            positives.Add("Esta abierto en la ventana disponible.");
            return;
        }

        score -= 24;
        negatives.Add("No parece abierto durante tu ventana disponible.");
    }

    private static void ApplyDuplicateScore(
        IReadOnlyList<Reservation> reservations,
        Recommendation recommendation,
        List<string> negatives,
        ref double score)
    {
        if (!reservations.Any(reservation => IsDuplicate(reservation, recommendation)))
        {
            return;
        }

        score -= 28;
        negatives.Add("Se parece a una reserva o item que ya tenes en el itinerario.");
    }

    private static bool MatchesCity(Recommendation recommendation, string city)
    {
        return recommendation.Neighborhood.Contains(city, StringComparison.OrdinalIgnoreCase)
            || recommendation.Description.Contains(city, StringComparison.OrdinalIgnoreCase);
    }

    private static bool MatchesAny(string value, IEnumerable<string> candidates)
    {
        return candidates.Any(candidate =>
            !string.IsNullOrWhiteSpace(candidate)
            && value.Contains(candidate, StringComparison.OrdinalIgnoreCase));
    }

    private static string CreateSearchableText(Recommendation recommendation)
    {
        return string.Join(
            ' ',
            [recommendation.Title, recommendation.Category, recommendation.Description, .. recommendation.Tags]);
    }

    private static bool IsFoodRecommendation(string searchableText)
    {
        return MatchesAny(
            searchableText,
            ["food", "comida", "snack", "restaurant", "restaurante", "cafe", "sake", "market"]);
    }

    private static int BudgetRank(string? value)
    {
        return value?.Trim().ToLowerInvariant() switch
        {
            "free" or "gratis" => 0,
            "low" or "budget" or "cheap" or "barato" => 1,
            "medium" or "moderate" or "medio" => 2,
            "high" or "expensive" or "premium" or "alto" => 3,
            _ => 2
        };
    }

    private static bool HasDietaryConflict(
        string restriction,
        string searchableText,
        IReadOnlyCollection<string> tags)
    {
        var normalizedRestriction = restriction.Trim().ToLowerInvariant();
        if (normalizedRestriction is "vegetarian" or "vegetariano" or "vegetariana")
        {
            return MatchesAny(
                searchableText,
                ["meat", "beef", "kobe", "steak", "pork", "chicken", "seafood", "sushi", "omakase"])
                || tags.Any(tag => tag.Contains("meat-heavy", StringComparison.OrdinalIgnoreCase));
        }

        if (normalizedRestriction is "vegan" or "vegano" or "vegana")
        {
            return MatchesAny(
                searchableText,
                ["meat", "beef", "kobe", "steak", "pork", "chicken", "seafood", "sushi", "omakase", "dairy", "cheese"])
                || tags.Any(tag => tag.Contains("meat-heavy", StringComparison.OrdinalIgnoreCase));
        }

        if (normalizedRestriction is "gluten-free" or "gluten free" or "sin gluten" or "celiac" or "celiaco" or "celiaca")
        {
            return MatchesAny(searchableText, ["gluten", "ramen", "udon", "noodle", "bread", "tempura"]);
        }

        return searchableText.Contains(restriction, StringComparison.OrdinalIgnoreCase)
            && !MatchesAny(searchableText, [$"{restriction} friendly", $"sin {restriction}", $"{restriction}-free"]);
    }

    private static bool IsOpenDuring(string openingHours, TimeOnly visitStart, TimeOnly visitEnd)
    {
        foreach (var segment in openingHours.Split([',', ';', '|'], StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = segment
                .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .SelectMany(part => part.Split('/', StringSplitOptions.RemoveEmptyEntries))
                .Where(part => part.Contains('-', StringComparison.Ordinal))
                .ToList();

            foreach (var part in parts)
            {
                var range = part.Split('-', 2, StringSplitOptions.TrimEntries);
                if (range.Length != 2
                    || !TimeOnly.TryParse(range[0], out var opensAt)
                    || !TimeOnly.TryParse(range[1], out var closesAt))
                {
                    continue;
                }

                if (closesAt < opensAt)
                {
                    if (visitStart >= opensAt || visitEnd <= closesAt)
                    {
                        return true;
                    }

                    continue;
                }

                if (opensAt <= visitStart && closesAt >= visitEnd)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool IsDuplicate(Reservation reservation, Recommendation recommendation)
    {
        var recommendationTitle = NormalizeComparableText(recommendation.Title);
        var reservationTitle = NormalizeComparableText(reservation.Title);
        var reservationLocation = NormalizeComparableText(reservation.LocationName);

        return !string.IsNullOrWhiteSpace(recommendationTitle)
            && (reservationTitle == recommendationTitle
                || reservationLocation == recommendationTitle
                || reservationTitle.Contains(recommendationTitle, StringComparison.Ordinal)
                || recommendationTitle.Contains(reservationTitle, StringComparison.Ordinal));
    }

    private static string NormalizeComparableText(string value)
    {
        return value.Trim().ToLowerInvariant();
    }

    private static double CalculateDistanceKm(
        decimal originLatitude,
        decimal originLongitude,
        decimal targetLatitude,
        decimal targetLongitude)
    {
        const double earthRadiusKm = 6371;

        static double ToRadians(decimal degrees) => (double)degrees * Math.PI / 180;

        var latitudeDelta = ToRadians(targetLatitude - originLatitude);
        var longitudeDelta = ToRadians(targetLongitude - originLongitude);
        var originLatitudeRadians = ToRadians(originLatitude);
        var targetLatitudeRadians = ToRadians(targetLatitude);

        var a = Math.Sin(latitudeDelta / 2) * Math.Sin(latitudeDelta / 2)
            + Math.Cos(originLatitudeRadians) * Math.Cos(targetLatitudeRadians)
            * Math.Sin(longitudeDelta / 2) * Math.Sin(longitudeDelta / 2);

        var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
        return Math.Round(earthRadiusKm * c, 2);
    }
}

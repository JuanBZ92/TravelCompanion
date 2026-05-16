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
            .Select(recommendation => ScoreRecommendation(profile, recommendation, context))
            .OrderByDescending(scored => scored.Score)
            .ThenBy(scored => scored.WalkingMinutes ?? int.MaxValue)
            .ThenBy(scored => scored.Recommendation.Title)
            .ToList();
    }

    private static ScoredRecommendation ScoreRecommendation(
        TravelPreferenceProfile profile,
        Recommendation recommendation,
        TravelPlanningContext context)
    {
        var score = 0d;
        var positives = new List<string>();
        var negatives = new List<string>();

        if (MatchesCity(recommendation, context.City))
        {
            score += 25;
            positives.Add($"Esta en la zona de {context.City}.");
        }

        if (MatchesAny(recommendation.Category, profile.Interests)
            || MatchesAny(recommendation.Description, profile.Interests))
        {
            score += 16;
            positives.Add("Encaja con tus intereses guardados.");
        }

        if (recommendation.Category.Contains("Food", StringComparison.OrdinalIgnoreCase)
            || recommendation.Description.Contains("comida", StringComparison.OrdinalIgnoreCase)
            || recommendation.Description.Contains("snack", StringComparison.OrdinalIgnoreCase))
        {
            score += 10;
            positives.Add("Suma una pausa gastronomica liviana al plan.");
        }

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

        if (profile.Dislikes.Any(dislike =>
            recommendation.Category.Contains(dislike, StringComparison.OrdinalIgnoreCase)
            || recommendation.Description.Contains(dislike, StringComparison.OrdinalIgnoreCase)))
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

    private static bool MatchesCity(Recommendation recommendation, string city)
    {
        return recommendation.Neighborhood.Contains(city, StringComparison.OrdinalIgnoreCase)
            || recommendation.Description.Contains(city, StringComparison.OrdinalIgnoreCase);
    }

    private static bool MatchesAny(string value, IEnumerable<string> candidates)
    {
        return candidates.Any(candidate => value.Contains(candidate, StringComparison.OrdinalIgnoreCase));
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

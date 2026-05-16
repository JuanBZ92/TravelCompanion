using TravelCompanion.Api.Models;

namespace TravelCompanion.Api.Services;

public sealed record ScoredRecommendation(
    Recommendation Recommendation,
    double Score,
    double? DistanceKm,
    int? WalkingMinutes,
    IReadOnlyList<string> PositiveReasons,
    IReadOnlyList<string> NegativeReasons);

# Deterministic Ranking

The model should not be the only ranking engine.

Use deterministic scoring first, then let the model explain the results.

## Suggested scoring factors

Positive:

```text
+ interest match
+ food preference match
+ compatible budget
+ compatible travel pace
+ close to reservation/current location
+ fits available time window
+ good rating/popularity
+ low walking time
```

Negative:

```text
- conflicts with dietary restrictions
- outside opening hours
- too expensive
- too far
- too long for the time slot
- duplicates previous itinerary items
- disliked category
```

## Example model

```csharp
public sealed record ScoredRecommendation(
    Recommendation Recommendation,
    double Score,
    IReadOnlyList<string> PositiveReasons,
    IReadOnlyList<string> NegativeReasons
);
```

## Example interface

```csharp
public interface IRecommendationRanker
{
    IReadOnlyList<ScoredRecommendation> Rank(
        TravelPreferenceProfile profile,
        IReadOnlyList<Reservation> reservations,
        IReadOnlyList<Recommendation> recommendations,
        TravelPlanningContext context);
}
```

## Principle

The explanation should be grounded in the score reasons.

Good:

> "Te recomiendo este lugar porque está a 12 minutos caminando, encaja con tu interés por comida local y no rompe tu presupuesto medio."

Bad:

> "Te recomiendo este lugar porque es perfecto para vos."

The second one is too generic and not explainable.

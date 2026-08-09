using Microsoft.EntityFrameworkCore;
using TravelCompanion.Api.Data;
using TravelCompanion.Api.Models;
using TravelCompanion.Shared;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Api.Services;

public interface ITravelRecommendationPlanningService
{
    Task<TravelRecommendationPlanningResult> RankAsync(
        AppUser user,
        IReadOnlyCollection<Guid> destinationIds,
        string city,
        TravelPreferenceProfile profile,
        IReadOnlyList<Reservation> reservations,
        TravelPlanningContext context,
        string responseMode,
        ISet<string> excludedRecommendationIds,
        CancellationToken cancellationToken);
}

public sealed record TravelRecommendationPlanningResult(
    int UnlockedRecommendationCount,
    int RankedCandidateCount,
    int DislikedFilteredCandidateCount,
    int ExcludedRecommendationCount,
    IReadOnlyList<ScoredRecommendation> RankedRecommendations);

public sealed class TravelRecommendationPlanningService(
    TravelCompanionDbContext dbContext,
    IRecommendationRanker ranker) : ITravelRecommendationPlanningService
{
    public async Task<TravelRecommendationPlanningResult> RankAsync(
        AppUser user,
        IReadOnlyCollection<Guid> destinationIds,
        string city,
        TravelPreferenceProfile profile,
        IReadOnlyList<Reservation> reservations,
        TravelPlanningContext context,
        string responseMode,
        ISet<string> excludedRecommendationIds,
        CancellationToken cancellationToken)
    {
        var unlockedRecommendations = await LoadUnlockedRecommendationsAsync(
            user,
            destinationIds,
            city,
            cancellationToken).ConfigureAwait(false);
        if (unlockedRecommendations.Count == 0)
        {
            return new TravelRecommendationPlanningResult(0, 0, 0, 0, []);
        }

        var rankedCandidates = ApplyResponseMode(
                ranker.Rank(profile, reservations, unlockedRecommendations, context),
                responseMode)
            .ToList();
        var rankedCandidateCount = rankedCandidates.Count;

        var dislikedFilteredCandidates = RemoveDislikedCandidates(rankedCandidates, profile.Dislikes);
        var dislikedFilteredCandidateCount = dislikedFilteredCandidates.Count;
        if (dislikedFilteredCandidates.Count > 0)
        {
            rankedCandidates = dislikedFilteredCandidates;
        }

        var excludedRecommendationCount = 0;
        if (excludedRecommendationIds.Count > 0)
        {
            var freshCandidates = rankedCandidates
                .Where(scored => !excludedRecommendationIds.Contains(scored.Recommendation.Id.ToString()))
                .ToList();
            excludedRecommendationCount = rankedCandidates.Count - freshCandidates.Count;
            if (freshCandidates.Count > 0)
            {
                rankedCandidates = freshCandidates;
            }
        }

        return new TravelRecommendationPlanningResult(
            unlockedRecommendations.Count,
            rankedCandidateCount,
            dislikedFilteredCandidateCount,
            excludedRecommendationCount,
            rankedCandidates);
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

    private static IEnumerable<ScoredRecommendation> ApplyResponseMode(
        IEnumerable<ScoredRecommendation> ranked,
        string responseMode)
    {
        return responseMode switch
        {
            TravelChatResponseModes.LessWalking => ranked
                .OrderBy(scored => scored.WalkingMinutes ?? scored.Recommendation.SuggestedDurationMinutes)
                .ThenByDescending(scored => scored.Score)
                .ThenBy(scored => scored.Recommendation.Title),
            TravelChatResponseModes.Shorter => ranked
                .OrderBy(scored => scored.Recommendation.SuggestedDurationMinutes)
                .ThenByDescending(scored => scored.Score)
                .ThenBy(scored => scored.Recommendation.Title),
            TravelChatResponseModes.Food => ranked
                .OrderByDescending(scored => IsFoodRecommendation(scored.Recommendation))
                .ThenByDescending(scored => scored.Score)
                .ThenBy(scored => scored.Recommendation.Title),
            TravelChatResponseModes.Culture => ranked
                .OrderByDescending(scored => IsCultureRecommendation(scored.Recommendation))
                .ThenByDescending(scored => scored.Score)
                .ThenBy(scored => scored.Recommendation.Title),
            TravelChatResponseModes.Cheaper => ranked
                .OrderBy(scored => PriceRank(scored.Recommendation.PriceLevel))
                .ThenBy(scored => scored.Recommendation.AccessLevel == ContentAccessLevel.Free ? 0 : 1)
                .ThenByDescending(scored => scored.Score)
                .ThenBy(scored => scored.Recommendation.Title),
            TravelChatResponseModes.MediumCost => ranked
                .OrderBy(scored => Math.Abs(PriceRank(scored.Recommendation.PriceLevel) - 2))
                .ThenByDescending(scored => scored.Score)
                .ThenBy(scored => scored.Recommendation.Title),
            TravelChatResponseModes.HighCost => ranked
                .OrderByDescending(scored => PriceRank(scored.Recommendation.PriceLevel))
                .ThenByDescending(scored => scored.Score)
                .ThenBy(scored => scored.Recommendation.Title),
            _ => ranked
        };
    }

    private static int PriceRank(string? value)
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

    private static bool ContainsAny(string value, params string[] candidates)
    {
        return candidates.Any(candidate => value.Contains(candidate, StringComparison.OrdinalIgnoreCase));
    }
}

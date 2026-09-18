using Microsoft.EntityFrameworkCore;
using TravelCompanion.Api.Models;
using TravelCompanion.Shared;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Api.Services;

internal static class RecommendationAccessQuery
{
    public static IQueryable<Recommendation> UnlockedFor(
        this IQueryable<Recommendation> query,
        Guid destinationId,
        UserEntitlementsDto entitlements)
    {
        var hasDestinationSubscription = ContentAccessPolicy.HasDestinationSubscription(entitlements, destinationId);
        var packageIds = entitlements.PackageIds?.ToArray() ?? [];

        return query.Where(recommendation =>
            recommendation.DestinationId == destinationId
            && recommendation.AccessLevel != ContentAccessLevel.AdminOnly
            && (hasDestinationSubscription
                || (!recommendation.Packages.Any() && recommendation.AccessLevel == ContentAccessLevel.Free)
                || recommendation.Packages.Any(package => packageIds.Contains(package.Id))));
    }
}

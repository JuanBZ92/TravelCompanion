namespace TravelCompanion.Shared;

public static class ContentAccessPolicy
{
    public static bool HasDestinationSubscription(
        Dtos.UserEntitlementsDto? entitlements,
        Guid destinationId)
    {
        return entitlements?.Entitlements.Any(entitlement =>
            entitlement.AccessLevel == ContentAccessLevel.Subscription
            && entitlement.DestinationId == destinationId) == true;
    }

    public static bool HasPackageAccess(
        Dtos.UserEntitlementsDto? entitlements,
        Guid packageId)
    {
        return entitlements?.PackageIds.Contains(packageId) == true;
    }

    public static bool IsPackageUnlocked(
        Dtos.UserEntitlementsDto? entitlements,
        Guid destinationId,
        Guid packageId)
    {
        return HasPackageAccess(entitlements, packageId)
            || HasDestinationSubscription(entitlements, destinationId);
    }

    public static bool IsRecommendationUnlocked(
        Dtos.UserEntitlementsDto? entitlements,
        ContentAccessLevel accessLevel,
        Guid destinationId,
        IReadOnlyList<Guid> packageIds)
    {
        if (accessLevel == ContentAccessLevel.AdminOnly)
        {
            return false;
        }

        if (packageIds.Count > 0)
        {
            return packageIds.Any(packageId => IsPackageUnlocked(entitlements, destinationId, packageId));
        }

        return accessLevel switch
        {
            ContentAccessLevel.Free => true,
            ContentAccessLevel.Subscription => HasDestinationSubscription(entitlements, destinationId),
            _ => false
        };
    }

    public static IReadOnlySet<ContentAccessLevel> GetUnlockedContentLevels(
        IEnumerable<ContentAccessLevel> activeAccessLevels,
        bool hasDestinationAccess = false,
        bool hasPackageAccess = false)
    {
        var unlocked = ProductAccessModel.ContentAccessOptions
            .Where(definition => IsUnlocked(
                definition.Level,
                activeAccessLevels,
                hasDestinationAccess,
                hasPackageAccess))
            .Select(definition => definition.Level)
            .ToHashSet();

        return unlocked;
    }

    public static bool IsUnlocked(
        ContentAccessLevel requiredAccess,
        IEnumerable<ContentAccessLevel> activeAccessLevels,
        bool hasDestinationAccess = false,
        bool hasPackageAccess = false)
    {
        var active = activeAccessLevels.ToHashSet();

        return requiredAccess switch
        {
            ContentAccessLevel.Free => true,
            ContentAccessLevel.Paid => active.Contains(ContentAccessLevel.Paid)
                || hasDestinationAccess
                || hasPackageAccess,
            ContentAccessLevel.Subscription => active.Contains(ContentAccessLevel.Subscription),
            ContentAccessLevel.AdminOnly => false,
            _ => false
        };
    }

    public static ContentAccessLevel GetPackageGrantLevel(bool isSubscription) =>
        ProductAccessModel.GetPackageGrantLevel(isSubscription);
}

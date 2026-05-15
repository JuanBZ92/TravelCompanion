namespace TravelCompanion.Shared;

public static class ContentAccessPolicy
{
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
                || active.Contains(ContentAccessLevel.Bundle)
                || hasDestinationAccess
                || hasPackageAccess,
            ContentAccessLevel.Subscription => active.Contains(ContentAccessLevel.Subscription),
            ContentAccessLevel.Bundle => active.Contains(ContentAccessLevel.Bundle)
                || hasDestinationAccess
                || hasPackageAccess,
            ContentAccessLevel.AdminOnly => false,
            _ => false
        };
    }

    public static ContentAccessLevel GetPackageGrantLevel(bool isSubscription) =>
        ProductAccessModel.GetPackageGrantLevel(isSubscription);
}

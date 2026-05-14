namespace TravelCompanion.Shared;

public static class ContentAccessPolicy
{
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
}

namespace TravelCompanion.Mobile.Services;

internal static class CacheFreshness
{
    public static bool IsFresh(
        DateTimeOffset? savedAt,
        TimeSpan maxAge,
        DateTimeOffset? now = null)
    {
        if (!savedAt.HasValue || maxAge < TimeSpan.Zero)
        {
            return false;
        }

        var age = (now ?? DateTimeOffset.UtcNow) - savedAt.Value;
        return age >= TimeSpan.Zero && age <= maxAge;
    }
}

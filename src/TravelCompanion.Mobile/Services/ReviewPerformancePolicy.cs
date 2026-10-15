namespace TravelCompanion.Mobile.Services;

// A review command never signs in, unlocks, changes an endpoint or grants permissions.
internal static class ReviewPerformancePolicy
{
    public static bool CanRun(bool enabled, string package, Uri? endpoint, bool hasSession, Guid? tripId, string? route) =>
        enabled && package.StartsWith("com.yuku.travelcompanion.", StringComparison.Ordinal)
        && package.EndsWith("review", StringComparison.Ordinal)
        && endpoint is { IsAbsoluteUri: true, IsLoopback: true }
        && (endpoint.Scheme is "http" or "https")
        && hasSession && tripId.HasValue
        && route?.StartsWith("//main/", StringComparison.Ordinal) == true;

    public static bool IsScenario(string? scenario) => scenario is "bootstrap" or "trip" or "journal" or "planning";

    public static bool IsCurrent(long expectedVersion, long version, Guid? expectedUser, Guid? user,
        Guid? expectedTrip, Guid? trip, bool hasSession) => hasSession && expectedVersion == version
        && expectedUser.HasValue && expectedUser == user && expectedTrip.HasValue && expectedTrip == trip;
}

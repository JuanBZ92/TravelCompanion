namespace TravelCompanion.Mobile.Services;

public sealed class AnalyticsConsentService(AuthSessionService sessions)
{
    public bool IsGranted
    {
        get => sessions.CurrentUserId is { } userId
            && Preferences.Default.Get(Key(userId), false);
        set
        {
            if (sessions.CurrentUserId is { } userId)
                Preferences.Default.Set(Key(userId), value);
        }
    }

    private static string Key(Guid userId) => $"analytics_consent_{userId:N}";
}

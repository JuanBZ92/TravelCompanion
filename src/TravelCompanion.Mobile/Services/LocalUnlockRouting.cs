namespace TravelCompanion.Mobile.Services;

public enum BiometricUnlockOutcome { Failed, Succeeded, UsePin, Rejected }

public static class LocalUnlockRouting
{
    public static string StartupRoute(AuthSessionService session, string authenticatedRoute) =>
        !session.HasSession ? "//login"
        : session.MustChangePassword ? "//change-password"
        : session.RequiresLocalUnlock ? "//biometric-unlock"
        : authenticatedRoute;
}

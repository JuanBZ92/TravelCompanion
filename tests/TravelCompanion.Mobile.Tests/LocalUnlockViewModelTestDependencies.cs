namespace TravelCompanion.Mobile
{
    // Native Shell is a platform boundary; authentication and routing remain production code.
    public class AppShell : Shell
    {
        public static string GetAuthenticatedLandingRoute(Services.AuthSessionService _) => "//main/schedule";
        public void ApplySessionTabs(Services.AuthSessionService _) { }
        public Task SignOutAsync() => Task.CompletedTask;
    }
}

namespace TravelCompanion.Mobile.Services
{
    public sealed class BiometricUnlockService
    {
        public Func<Task<bool>> Availability { get; set; } = () => Task.FromResult(true);
        public Func<Task<bool>> Verification { get; set; } = () => Task.FromResult(true);
        public Func<Task<BiometricUnlockOutcome>>? OutcomeVerification { get; set; }
        public int AvailabilityCalls { get; private set; }
        public int VerificationCalls { get; private set; }

        public Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            AvailabilityCalls++;
            return Availability();
        }

        public async Task<BiometricUnlockOutcome> AuthenticateForUnlockAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            VerificationCalls++;
            return OutcomeVerification is not null ? await OutcomeVerification()
                : await Verification() ? BiometricUnlockOutcome.Succeeded : BiometricUnlockOutcome.Rejected;
        }
    }
}

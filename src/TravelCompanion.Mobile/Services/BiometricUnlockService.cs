using Maui.Biometric;

namespace TravelCompanion.Mobile.Services;

public sealed class BiometricUnlockService(IBiometricAuthentication biometricAuthentication)
{
    public async Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
    {
        var availability = await biometricAuthentication.CheckAvailabilityAsync(
            Authenticator.Biometric,
            cancellationToken).ConfigureAwait(false);

        return availability.IsAvailable;
    }

    public async Task<bool> UnlockAsync(CancellationToken cancellationToken = default) =>
        await AuthenticateForUnlockAsync(cancellationToken) == BiometricUnlockOutcome.Succeeded;

    public async Task<BiometricUnlockOutcome> AuthenticateForUnlockAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var result = await biometricAuthentication.AuthenticateAsync(
            new AuthenticationRequest(
                LocalizationResourceManager.Instance["BiometricPromptTitle"],
                LocalizationResourceManager.Instance["BiometricPromptDescription"])
            {
                CancelTitle = LocalizationResourceManager.Instance["UnlockPinAction"],
                FallbackTitle = LocalizationResourceManager.Instance["UnlockPinAction"],
                Authenticators = Authenticator.Biometric
            },
            cancellationToken).ConfigureAwait(false);

        cancellationToken.ThrowIfCancellationRequested();
        return result.Status switch
        {
            AuthenticationStatus.Success => BiometricUnlockOutcome.Succeeded,
            // The native negative/fallback button is labelled as the PIN alternative.
            AuthenticationStatus.FallbackRequested or AuthenticationStatus.Canceled => BiometricUnlockOutcome.UsePin,
            AuthenticationStatus.Failed => BiometricUnlockOutcome.Rejected,
            _ => BiometricUnlockOutcome.Failed
        };
    }
}

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

    public async Task<bool> UnlockAsync(CancellationToken cancellationToken = default)
    {
        var result = await biometricAuthentication.AuthenticateAsync(
            new AuthenticationRequest(
                LocalizationResourceManager.Instance["BiometricPromptTitle"],
                LocalizationResourceManager.Instance["BiometricPromptDescription"])
            {
                CancelTitle = LocalizationResourceManager.Instance["BiometricPasswordAction"],
                FallbackTitle = LocalizationResourceManager.Instance["BiometricPasswordAction"],
                Authenticators = Authenticator.Biometric
            },
            cancellationToken).ConfigureAwait(false);

        return result.IsSuccessful;
    }
}

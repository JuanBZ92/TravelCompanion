using Maui.Biometric;

namespace TravelCompanion.Mobile.Services;

public sealed class BiometricUnlockService(IBiometricAuthentication biometricAuthentication)
{
    public async Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
    {
        var availability = await biometricAuthentication.CheckAvailabilityAsync(
            Authenticator.Biometric,
            cancellationToken);

        return availability.IsAvailable;
    }

    public async Task<bool> UnlockAsync(CancellationToken cancellationToken = default)
    {
        var result = await biometricAuthentication.AuthenticateAsync(
            new AuthenticationRequest(
                "Desbloquear Travel Companion",
                "Usa tu huella, Face ID o la biometria disponible en este dispositivo.")
            {
                CancelTitle = "Usar password",
                FallbackTitle = "Usar password",
                Authenticators = Authenticator.Biometric
            },
            cancellationToken);

        return result.IsSuccessful;
    }
}

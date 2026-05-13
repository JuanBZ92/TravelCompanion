using CommunityToolkit.Mvvm.Input;
using TravelCompanion.Mobile.Services;

namespace TravelCompanion.Mobile.ViewModels;

public sealed partial class BiometricUnlockViewModel(
    BiometricUnlockService biometricUnlockService,
    AuthSessionService sessionService) : ViewModelBase
{
    private string _unlockStatusMessage = "Desbloquea tu viaje con biometria.";
    private bool _hasTriedAutoUnlock;

    public string DisplayName => sessionService.CurrentDisplayName ?? "tu cuenta";

    public string UnlockStatusMessage
    {
        get => _unlockStatusMessage;
        set => SetProperty(ref _unlockStatusMessage, value);
    }

    public async Task TryAutoUnlockAsync()
    {
        if (_hasTriedAutoUnlock)
        {
            return;
        }

        _hasTriedAutoUnlock = true;
        await UnlockCommand.ExecuteAsync(null);
    }

    [RelayCommand]
    private Task UnlockAsync()
    {
        return LoadAsync(async () =>
        {
            if (!sessionService.HasSession)
            {
                await Shell.Current.GoToAsync("//login");
                return;
            }

            var token = await sessionService.GetTokenAsync();
            if (string.IsNullOrWhiteSpace(token))
            {
                sessionService.Clear();
                await Shell.Current.GoToAsync("//login");
                return;
            }

            if (!await biometricUnlockService.IsAvailableAsync())
            {
                UnlockStatusMessage = "Este dispositivo no tiene biometria disponible. Ingresa con password.";
                return;
            }

            if (await biometricUnlockService.UnlockAsync())
            {
                await Shell.Current.GoToAsync("//main/recommendations");
                return;
            }

            UnlockStatusMessage = "No pudimos desbloquear con biometria. Puedes usar tu password.";
        });
    }

    [RelayCommand]
    private async Task UsePasswordAsync()
    {
        await Shell.Current.GoToAsync("//login");
    }
}

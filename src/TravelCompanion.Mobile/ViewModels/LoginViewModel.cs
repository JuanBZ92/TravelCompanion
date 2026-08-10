using CommunityToolkit.Mvvm.Input;
using TravelCompanion.Mobile.Services;
using TravelCompanion.Shared;

namespace TravelCompanion.Mobile.ViewModels;

public sealed partial class LoginViewModel(
    TravelCompanionApiClient apiClient,
    AuthSessionService sessionService) : ViewModelBase
{
    private string _pin = string.Empty;

    public string Pin
    {
        get => _pin;
        set => SetProperty(ref _pin, value);
    }

    public bool CanUseBiometricUnlock => sessionService.HasSession && sessionService.IsBiometricEnabled;

    [RelayCommand]
    private Task LoginAsync()
    {
        return LoadAsync(async () =>
        {
            var pin = new string(Pin.Where(char.IsDigit).Take(4).ToArray());
            if (pin.Length != 4)
            {
                ErrorMessage = "Ingresa el PIN de 4 numeros de tu viaje.";
                return;
            }

            var session = await apiClient.LoginWithPinAsync(pin);
            if (session is null)
            {
                ErrorMessage = "No encontramos un viaje con ese PIN.";
                return;
            }

            await sessionService.SaveAsync(session);
            Pin = string.Empty;

            var route = session.AccessMode == SessionAccessMode.FreeMapPreview
                ? "//free-map"
                : session.MustChangePassword
                    ? "//change-password"
                    : "//main/schedule";
            await Shell.Current.GoToAsync(route);
        });
    }

    [RelayCommand]
    private async Task UnlockWithBiometricsAsync()
    {
        await Shell.Current.GoToAsync("//biometric-unlock");
    }
}

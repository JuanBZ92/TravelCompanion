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
        set
        {
            var normalized = new string((value ?? string.Empty)
                .Where(char.IsDigit)
                .Take(6)
                .ToArray());

            if (SetProperty(ref _pin, normalized))
            {
                OnPropertyChanged(nameof(PinLengthText));
            }
        }
    }

    public string PinLengthText => $"{Pin.Length} / 6";

    public bool CanUseBiometricUnlock => sessionService.HasSession && sessionService.IsBiometricEnabled;

    [RelayCommand]
    private Task LoginAsync()
    {
        return LoadAsync(async () =>
        {
            var pin = new string(Pin.Where(char.IsDigit).Take(6).ToArray());
            if (pin.Length is not (4 or 6))
            {
                ErrorMessage = "Ingresa tu PIN de 4 o 6 numeros.";
                return;
            }

            var session = await apiClient.LoginWithPinAsync(pin);
            if (session is null)
            {
                ErrorMessage = "No encontramos un viaje con ese PIN.";
                return;
            }

            await sessionService.SaveAsync(session);
            if (Shell.Current is AppShell appShell)
            {
                appShell.ApplySessionTabs(sessionService);
            }
            Pin = string.Empty;

            var route = session.AccessMode == SessionAccessMode.FreeMapPreview
                ? "//free-map"
                : session.AccessMode == SessionAccessMode.Builder
                    ? "//main/map"
                : session.MustChangePassword
                    ? "//change-password"
                    : "//main/map";
            await Shell.Current.GoToAsync(route);
        });
    }

    [RelayCommand]
    private async Task UnlockWithBiometricsAsync()
    {
        await Shell.Current.GoToAsync("//biometric-unlock");
    }
}

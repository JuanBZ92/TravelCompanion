using CommunityToolkit.Mvvm.Input;
using TravelCompanion.Mobile.Services;
using TravelCompanion.Shared;

namespace TravelCompanion.Mobile.ViewModels;

public sealed partial class LoginViewModel(
    TravelCompanionApiClient apiClient,
    AuthSessionService sessionService,
    SessionLogoutService logoutService) : ViewModelBase
{
    private string _pin = string.Empty;

    public string PageTitle => Resource("TabLogin");
    public string LoginTitle => Resource("LoginTitle");
    public string LoginDescription => Resource("LoginDescription");
    public string LoginFreePreview => Resource("LoginFreePreview");
    public string LoginOpenTrip => Resource("LoginOpenTrip");
    public string LoginBiometric => Resource("LoginBiometric");
    public string LoginRecoverEmail => Resource("LoginRecoverEmail");

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
                ErrorMessage = LocalizationResourceManager.Instance.GetString("LoginPinLengthError");
                return;
            }

            var session = await apiClient.LoginWithPinAsync(pin);
            if (session is null)
            {
                ErrorMessage = LocalizationResourceManager.Instance.GetString("LoginPinNotFound");
                return;
            }

            if (sessionService.HasSession)
            {
                await logoutService.LogoutAsync();
            }

            await sessionService.SaveAsync(session);
            // Clear snapshots after saving so capability-bound UI is reset using the
            // newly authenticated tier rather than the previous session.
            await logoutService.ResetContentAsync(session.UserId);
            if (Shell.Current is AppShell appShell)
            {
                appShell.ApplySessionTabs(sessionService);
            }
            Pin = string.Empty;

            var route = session.MustChangePassword
                    ? "//change-password"
                    : AppShell.GetAuthenticatedLandingRoute(sessionService);
            await Shell.Current.GoToAsync(route);
        });
    }

    [RelayCommand]
    private async Task UnlockWithBiometricsAsync()
    {
        if (!CanUseBiometricUnlock) return;
        await Shell.Current.GoToAsync("//biometric-unlock");
    }

    public void RefreshAuthenticationOptions() => OnPropertyChanged(nameof(CanUseBiometricUnlock));

    [RelayCommand]
    private Task RecoverWithEmailAsync() => LoadAsync(async cancellationToken =>
    {
        var email = await Shell.Current.DisplayPromptAsync(
            Resource("AccountRecoverTitle"),
            Resource("AccountRecoverPrompt"),
            Resource("AccountSendCode"),
            Resource("CommonCancel"),
            keyboard: Keyboard.Email);
        if (string.IsNullOrWhiteSpace(email)) return;

        var requested = await apiClient.RequestEmailCodeAsync(
            null,
            email,
            System.Globalization.CultureInfo.CurrentUICulture.Name,
            cancellationToken);
        if (requested is null)
            throw new InvalidOperationException(Resource("AccountCodeSendError"));

        var code = await Shell.Current.DisplayPromptAsync(
            Resource("AccountVerifyTitle"),
            Resource("AccountVerifyPrompt"),
            Resource("AccountVerify"),
            Resource("CommonCancel"),
            keyboard: Keyboard.Numeric,
            maxLength: 6);
        if (string.IsNullOrWhiteSpace(code)) return;

        var session = await apiClient.VerifyEmailCodeAsync(
            null,
            email,
            new string(code.Where(char.IsDigit).ToArray()),
            cancellationToken) ?? throw new InvalidOperationException(Resource("AccountCodeInvalid"));

        if (sessionService.HasSession)
            await logoutService.ResetContentAsync(sessionService.CurrentUserId);
        await sessionService.SaveAsync(session);
        if (Shell.Current is AppShell appShell) appShell.ApplySessionTabs(sessionService);
        await Shell.Current.GoToAsync(nameof(TravelCompanion.Mobile.Pages.AccountPage));
    });

    private static string Resource(string key) => LocalizationResourceManager.Instance.GetString(key);
}

using CommunityToolkit.Mvvm.Input;
using TravelCompanion.Mobile.Services;

namespace TravelCompanion.Mobile.ViewModels;

public sealed partial class LoginViewModel(
    TravelCompanionApiClient apiClient,
    AuthSessionService sessionService) : ViewModelBase
{
    private string _email = sessionService.CurrentEmail ?? "demo@travelcompanion.local";
    private string _password = "TravelDemo!2026";
    private bool _isPasswordHidden = true;

    public string Email
    {
        get => _email;
        set => SetProperty(ref _email, value);
    }

    public string Password
    {
        get => _password;
        set => SetProperty(ref _password, value);
    }

    public bool IsPasswordHidden
    {
        get => _isPasswordHidden;
        set => SetProperty(ref _isPasswordHidden, value);
    }

    public bool CanUseBiometricUnlock => sessionService.HasSession && sessionService.IsBiometricEnabled;

    [RelayCommand]
    private Task LoginAsync()
    {
        return LoadAsync(async () =>
        {
            var email = Email.Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(email))
            {
                ErrorMessage = "Ingresa el email de tu cuenta.";
                return;
            }

            if (string.IsNullOrWhiteSpace(Password))
            {
                ErrorMessage = "Ingresa tu password.";
                return;
            }

            var session = await apiClient.LoginAsync(email, Password);
            if (session is null)
            {
                ErrorMessage = "Email o password incorrectos.";
                return;
            }

            await sessionService.SaveAsync(session);
            Password = string.Empty;

            await Shell.Current.GoToAsync(session.MustChangePassword
                ? "//change-password"
                : "//main/recommendations");
        });
    }

    [RelayCommand]
    private void TogglePasswordVisibility()
    {
        IsPasswordHidden = !IsPasswordHidden;
    }

    [RelayCommand]
    private async Task UnlockWithBiometricsAsync()
    {
        await Shell.Current.GoToAsync("//biometric-unlock");
    }
}

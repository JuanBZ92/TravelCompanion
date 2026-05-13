using CommunityToolkit.Mvvm.Input;
using TravelCompanion.Mobile.Services;

namespace TravelCompanion.Mobile.ViewModels;

public sealed partial class SupportViewModel(
    TravelCompanionApiClient apiClient,
    AuthSessionService sessionService) : ViewModelBase
{
    public string DisplayName => sessionService.CurrentDisplayName ?? "Cuenta";

    public string Email => sessionService.CurrentEmail ?? string.Empty;

    public bool IsBiometricEnabled
    {
        get => sessionService.IsBiometricEnabled;
        set
        {
            if (sessionService.IsBiometricEnabled == value)
            {
                return;
            }

            sessionService.IsBiometricEnabled = value;
            OnPropertyChanged();
        }
    }

    [RelayCommand]
    private async Task LockAsync()
    {
        await Shell.Current.GoToAsync(sessionService.IsBiometricEnabled
            ? "//biometric-unlock"
            : "//login");
    }

    [RelayCommand]
    private async Task LogoutAsync()
    {
        var token = await sessionService.GetTokenAsync();
        if (!string.IsNullOrWhiteSpace(token))
        {
            await apiClient.LogoutAsync(token);
        }

        sessionService.Clear();
        await Shell.Current.GoToAsync("//login");
    }
}

using CommunityToolkit.Mvvm.Input;
using TravelCompanion.Mobile.Services;

namespace TravelCompanion.Mobile.ViewModels;

public sealed partial class ChangePasswordViewModel(
    TravelCompanionApiClient apiClient,
    AuthSessionService sessionService) : ViewModelBase
{
    private string _newPassword = string.Empty;
    private string _confirmPassword = string.Empty;

    public string NewPassword
    {
        get => _newPassword;
        set => SetProperty(ref _newPassword, value);
    }

    public string ConfirmPassword
    {
        get => _confirmPassword;
        set => SetProperty(ref _confirmPassword, value);
    }

    [RelayCommand]
    private Task ChangePasswordAsync()
    {
        return LoadAsync(async () =>
        {
            if (NewPassword.Length < 12)
            {
                ErrorMessage = "La nueva password debe tener al menos 12 caracteres.";
                return;
            }

            if (NewPassword != ConfirmPassword)
            {
                ErrorMessage = "Las passwords no coinciden.";
                return;
            }

            var token = await sessionService.GetTokenAsync();
            if (string.IsNullOrWhiteSpace(token))
            {
                sessionService.Clear();
                await Shell.Current.GoToAsync("//login");
                return;
            }

            await apiClient.ChangePasswordAsync(token, string.Empty, NewPassword);
            sessionService.MarkPasswordChanged();
            NewPassword = string.Empty;
            ConfirmPassword = string.Empty;

            await Shell.Current.GoToAsync("//main/recommendations");
        });
    }
}

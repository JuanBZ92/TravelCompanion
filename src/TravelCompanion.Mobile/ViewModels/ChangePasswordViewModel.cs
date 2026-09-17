using CommunityToolkit.Mvvm.Input;
using TravelCompanion.Mobile.Services;

namespace TravelCompanion.Mobile.ViewModels;

public sealed partial class ChangePasswordViewModel(
    TravelCompanionApiClient apiClient,
    AuthSessionService sessionService,
    SessionLogoutService logoutService) : ViewModelBase
{
    private string _newPassword = string.Empty;
    private string _confirmPassword = string.Empty;

    public string PageTitle => Resource("TabChangePassword");
    public string AccountLabel => Resource("PasswordAccountLabel");
    public string PasswordTitle => Resource("PasswordTitle");
    public string PasswordDescription => Resource("PasswordDescription");
    public string NewPasswordLabel => Resource("PasswordNew");
    public string ConfirmPasswordLabel => Resource("PasswordConfirm");
    public string SavePasswordLabel => Resource("PasswordSave");

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
                ErrorMessage = LocalizationResourceManager.Instance.GetString("PasswordLengthError");
                return;
            }

            if (NewPassword != ConfirmPassword)
            {
                ErrorMessage = LocalizationResourceManager.Instance.GetString("PasswordMismatchError");
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
            NewPassword = string.Empty;
            ConfirmPassword = string.Empty;
            var userId = sessionService.CurrentUserId;
            await logoutService.ResetContentAsync(userId);
            sessionService.Clear();
            await Shell.Current.GoToAsync("//login");
        });
    }

    private static string Resource(string key) => LocalizationResourceManager.Instance.GetString(key);
}

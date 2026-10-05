using CommunityToolkit.Mvvm.Input;
using TravelCompanion.Mobile.Services;

namespace TravelCompanion.Mobile.ViewModels;

public sealed partial class BiometricUnlockViewModel(
    BiometricUnlockService biometricUnlockService,
    AuthSessionService sessionService) : ViewModelBase
{
    private string _unlockStatusMessage = LocalizationResourceManager.Instance["BiometricStatusDefault"];
    private bool _hasTriedAutoUnlock;

    public string DisplayName => sessionService.CurrentDisplayName ?? LocalizationResourceManager.Instance["BiometricAccountFallback"];
    public string SessionDescription => string.Format(LocalizationResourceManager.Instance.CurrentCulture,
        LocalizationResourceManager.Instance["BiometricSessionFormat"], DisplayName);

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
            try
            {
                UnlockStatusMessage = LocalizationResourceManager.Instance["BiometricStatusDefault"];
                if (!sessionService.HasSession || !sessionService.IsBiometricEnabled)
                {
                    await Shell.Current.GoToAsync("//login");
                    return;
                }

                var contextVersion = sessionService.ContextVersion;
                var token = await sessionService.GetTokenAsync();
                if (!sessionService.HasSession || !sessionService.IsBiometricEnabled
                    || contextVersion != sessionService.ContextVersion) return;
                if (string.IsNullOrWhiteSpace(token))
                {
                    sessionService.Clear();
                    await Shell.Current.GoToAsync("//login");
                    return;
                }

                if (!await biometricUnlockService.IsAvailableAsync())
                {
                    UnlockStatusMessage = LocalizationResourceManager.Instance["BiometricUnavailable"];
                    return;
                }

                if (!sessionService.HasSession || !sessionService.IsBiometricEnabled
                    || contextVersion != sessionService.ContextVersion) return;

                if (await biometricUnlockService.UnlockAsync())
                {
                    if (!sessionService.HasSession || !sessionService.IsBiometricEnabled
                        || contextVersion != sessionService.ContextVersion) return;
                    await Shell.Current.GoToAsync(AppShell.GetAuthenticatedLandingRoute(sessionService));
                    return;
                }

                UnlockStatusMessage = LocalizationResourceManager.Instance["BiometricRejected"];
            }
            catch (Exception error)
            {
                ClientDiagnostics.Record("biometric_unlock_failed", exception: error);
                ErrorMessage = LocalizationResourceManager.Instance["BiometricUnlockError"];
            }
        });
    }

    [RelayCommand]
    private async Task UsePasswordAsync()
    {
        await Shell.Current.GoToAsync("//login");
    }
}

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
    public bool UsesBiometrics => sessionService.IsBiometricEnabled;
    public string PrimaryUnlockText => LocalizationResourceManager.Instance[UsesBiometrics ? "BiometricUnlockAction" : "UnlockPinAction"];

    public string UnlockStatusMessage
    {
        get => _unlockStatusMessage;
        set => SetProperty(ref _unlockStatusMessage, value);
    }

    public async Task TryAutoUnlockAsync()
    {
        OnPropertyChanged(nameof(UsesBiometrics));
        OnPropertyChanged(nameof(PrimaryUnlockText));
        UnlockStatusMessage = LocalizationResourceManager.Instance[UsesBiometrics ? "BiometricStatusDefault" : "UnlockPinStatus"];
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
        return LoadAsync(async cancellationToken =>
        {
            var contextVersion = sessionService.ContextVersion;
            bool Current() => !cancellationToken.IsCancellationRequested && sessionService.HasSession
                && sessionService.IsBiometricEnabled && contextVersion == sessionService.ContextVersion;
            try
            {
                UnlockStatusMessage = LocalizationResourceManager.Instance[UsesBiometrics ? "BiometricStatusDefault" : "UnlockPinStatus"];
                if (!sessionService.HasSession || !sessionService.IsBiometricEnabled)
                {
                    await Shell.Current.GoToAsync("//login");
                    return;
                }

                var token = await sessionService.GetTokenAsync();
                if (!Current()) return;
                if (string.IsNullOrWhiteSpace(token))
                {
                    sessionService.Clear();
                    await Shell.Current.GoToAsync("//login");
                    return;
                }

                var available = await biometricUnlockService.IsAvailableAsync(cancellationToken);
                if (!Current()) return;
                if (!available)
                {
                    UnlockStatusMessage = LocalizationResourceManager.Instance["BiometricUnavailable"];
                    return;
                }

                var result = await biometricUnlockService.AuthenticateForUnlockAsync(cancellationToken);
                if (!Current()) return;
                if (result == BiometricUnlockOutcome.Succeeded)
                {
                    await Shell.Current.GoToAsync(AppShell.GetAuthenticatedLandingRoute(sessionService));
                    return;
                }
                if (result == BiometricUnlockOutcome.UsePin)
                {
                    await Shell.Current.GoToAsync("//login");
                    return;
                }
                if (result == BiometricUnlockOutcome.Rejected)
                    UnlockStatusMessage = LocalizationResourceManager.Instance["BiometricRejected"];
                else ErrorMessage = LocalizationResourceManager.Instance["BiometricUnlockError"];
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
            catch (Exception error)
            {
                ClientDiagnostics.Record("biometric_unlock_failed", exception: error);
                if (Current()) ErrorMessage = LocalizationResourceManager.Instance["BiometricUnlockError"];
            }
        });
    }

    [RelayCommand]
    private async Task UsePasswordAsync()
    {
        await Shell.Current.GoToAsync("//login");
    }
}

using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using TravelCompanion.Mobile.Services;
using TravelCompanion.Shared;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Mobile.ViewModels;

public sealed partial class AccountViewModel(
    TravelCompanionApiClient api,
    AuthSessionService sessions,
    SessionLogoutService logout,
    AnalyticsConsentService analyticsConsent,
    ProductAnalyticsQueueService analyticsQueue,
    TripDocumentStore documents,
    BiometricUnlockService biometricUnlock,
    PendingStorePurchaseStore pendingPurchases) : ViewModelBase
{
    private string _email = string.Empty;
    private bool _emailVerified;
    private bool _behaviorAnalyticsEnabled;

    public ObservableCollection<AccountTripItem> Trips { get; } = [];
    public string PageTitle => Resource("TabAccount");
    public string Email { get => _email; private set => SetProperty(ref _email, value); }
    public bool EmailVerified { get => _emailVerified; private set => SetProperty(ref _emailVerified, value); }
    public bool HasTrips => Trips.Count > 0;
    public bool HasNoTrips => !HasTrips;
    public string UnlockMethodLabel => Resource(sessions.PreferredUnlockMethod == DeviceUnlockMethod.Biometric
        ? "AccountUnlockBiometric" : "AccountUnlockPin");
    public string UnlockMethodDescription => Resource(sessions.PreferredUnlockMethod == DeviceUnlockMethod.Biometric
        ? "AccountUnlockBiometricBody" : "AccountUnlockPinBody");
    public bool BehaviorAnalyticsEnabled
    {
        get => _behaviorAnalyticsEnabled;
        set
        {
            if (!SetProperty(ref _behaviorAnalyticsEnabled, value)) return;
            analyticsConsent.IsGranted = value;
            if (!value && sessions.CurrentUserId is { } userId)
                _ = analyticsQueue.RemoveForUserAsync(userId);
            _ = PersistAnalyticsConsentAsync(value);
        }
    }

    [RelayCommand]
    private Task LoadAccountAsync() => LoadAsync(async cancellationToken =>
    {
        NotifyUnlockMethod();
        var token = await sessions.GetTokenAsync();
        if (string.IsNullOrWhiteSpace(token)) throw new UnauthorizedAccessException();
        var account = await api.GetAccountAsync(token, cancellationToken)
            ?? throw new InvalidOperationException(Resource("AccountLoadError"));
        Email = account.Email;
        EmailVerified = account.EmailVerified;
        _behaviorAnalyticsEnabled = account.BehaviorAnalyticsConsent;
        analyticsConsent.IsGranted = account.BehaviorAnalyticsConsent;
        OnPropertyChanged(nameof(BehaviorAnalyticsEnabled));
        Trips.Clear();
        foreach (var trip in account.Trips)
            Trips.Add(AccountTripItem.From(trip, sessions.CurrentTripId == trip.TripId));
        OnPropertyChanged(nameof(HasTrips));
        OnPropertyChanged(nameof(HasNoTrips));
    });

    [RelayCommand]
    private Task ChooseUnlockMethodAsync() => LoadAsync(async cancellationToken =>
    {
        var context = sessions.ContextVersion;
        var userId = sessions.CurrentUserId;
        if (!sessions.HasSession || userId is null) return;
        bool Current() => !cancellationToken.IsCancellationRequested && sessions.HasSession
            && sessions.CurrentUserId == userId && sessions.ContextVersion == context;
        try
        {
            var biometric = Resource("AccountUnlockBiometric");
            var pin = Resource("AccountUnlockPin");
            var choice = await Shell.Current.DisplayActionSheetAsync(Resource("AccountUnlockChoose"),
                Resource("CommonCancel"), null, biometric, pin);
            if (!Current() || choice != biometric && choice != pin) return;
            var method = choice == biometric ? DeviceUnlockMethod.Biometric : DeviceUnlockMethod.Pin;
            if (method == sessions.PreferredUnlockMethod && sessions.RequiresLocalUnlock) return;
            if (method == DeviceUnlockMethod.Biometric)
            {
                var available = await biometricUnlock.IsAvailableAsync(cancellationToken);
                if (!Current()) return;
                if (!available) { ErrorMessage = Resource("BiometricUnavailable"); return; }
                var verified = await biometricUnlock.UnlockAsync(cancellationToken);
                if (!Current()) return;
                if (!verified) { ErrorMessage = Resource("BiometricRejected"); return; }
            }
            sessions.SetUnlockMethod(method);
            NotifyUnlockMethod();
            StatusMessage = Resource("AccountUnlockSaved");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception error)
        {
            ClientDiagnostics.Record("account_unlock_preference_failed", exception: error);
            if (Current()) ErrorMessage = Resource("AccountUnlockError");
        }
    });

    private void NotifyUnlockMethod()
    {
        OnPropertyChanged(nameof(UnlockMethodLabel));
        OnPropertyChanged(nameof(UnlockMethodDescription));
    }

    [RelayCommand]
    private Task SelectTripAsync(AccountTripItem? item) => LoadAsync(async cancellationToken =>
    {
        if (item is null) return;
        var token = await sessions.GetTokenAsync();
        if (string.IsNullOrWhiteSpace(token)) throw new UnauthorizedAccessException();
        var session = await api.SelectAccountTripAsync(token, item.TripId, cancellationToken)
            ?? throw new InvalidOperationException(Resource("AccountSelectTripError"));
        await logout.ResetContentAsync(session.UserId);
        await sessions.SaveAsync(session);
        if (Shell.Current is AppShell shell) shell.ApplySessionTabs(sessions);
        await Shell.Current.GoToAsync(AppShell.GetAuthenticatedLandingRoute(sessions));
    });

    [RelayCommand]
    private async Task ArchiveTripAsync(AccountTripItem? item)
    {
        if (item is null || !item.CanArchive) return;
        if (!await Shell.Current.DisplayAlertAsync(Resource("AccountArchiveTitle"),
                Resource("AccountArchiveWarning"), Resource("AccountArchive"), Resource("CommonCancel"))) return;
        await LoadAsync(async cancellationToken =>
        {
            var token = await sessions.GetTokenAsync();
            if (string.IsNullOrWhiteSpace(token)) throw new UnauthorizedAccessException();
            var updatedSession = await api.ArchiveAccountTripAsync(token, item.TripId, cancellationToken);
            if (updatedSession is null)
                throw new InvalidOperationException(Resource("AccountArchiveError"));
            if (item.IsCurrent)
            {
                await logout.ResetContentAsync(sessions.CurrentUserId);
                await sessions.SaveAsync(updatedSession);
                if (Shell.Current is AppShell shell) shell.ApplySessionTabs(sessions);
                token = updatedSession.Token;
            }
            await LoadAccountCoreAsync(token, cancellationToken);
        });
    }

    [RelayCommand]
    private Task VerifyEmailAsync() => LoadAsync(async cancellationToken =>
    {
        var token = await sessions.GetTokenAsync();
        if (string.IsNullOrWhiteSpace(token)) throw new UnauthorizedAccessException();
        var email = await Shell.Current.DisplayPromptAsync(
            Resource("AccountRecoverTitle"), Resource("AccountRecoverPrompt"),
            Resource("AccountSendCode"), Resource("CommonCancel"),
            initialValue: EmailVerified ? Email : string.Empty, keyboard: Keyboard.Email);
        if (string.IsNullOrWhiteSpace(email)) return;
        if (await api.RequestEmailCodeAsync(token, email, System.Globalization.CultureInfo.CurrentUICulture.Name, cancellationToken) is null)
            throw new InvalidOperationException(Resource("AccountCodeSendError"));
        var code = await Shell.Current.DisplayPromptAsync(
            Resource("AccountVerifyTitle"), Resource("AccountVerifyPrompt"),
            Resource("AccountVerify"), Resource("CommonCancel"),
            keyboard: Keyboard.Numeric, maxLength: 6);
        if (string.IsNullOrWhiteSpace(code)) return;
        var session = await api.VerifyEmailCodeAsync(token, email, new string(code.Where(char.IsDigit).ToArray()), cancellationToken)
            ?? throw new InvalidOperationException(Resource("AccountCodeInvalid"));
        await MauiProgram.Services.GetRequiredService<JournalStore>().TransferLinkedTripAsync(session);
        await MauiProgram.Services.GetRequiredService<ExpenseStore>().TransferLinkedTripAsync(session);
        await MauiProgram.Services.GetRequiredService<DayPlannerStore>().DiscardLinkedPreviewAsync(session);
        await logout.ResetContentAsync(sessions.CurrentUserId);
        await sessions.SaveAsync(session);
        if (Shell.Current is AppShell shell) shell.ApplySessionTabs(sessions);
        Email = session.Email;
        EmailVerified = true;
        await LoadAccountCoreAsync(session.Token, cancellationToken);
    });

    [RelayCommand]
    private async Task DeleteAccountAsync()
    {
        var confirmed = await Shell.Current.DisplayAlertAsync(
            Resource("AccountDeleteTitle"), Resource("AccountDeleteWarning"),
            Resource("AccountDeleteConfirm"), Resource("CommonCancel"));
        if (!confirmed) return;
        await LoadAsync(async cancellationToken =>
        {
            var deletingUserId = sessions.CurrentUserId;
            var deletingContext = sessions.ContextVersion;
            var token = await sessions.GetTokenAsync();
            if (string.IsNullOrWhiteSpace(token) || !await api.DeleteAccountAsync(token, cancellationToken))
                throw new InvalidOperationException(Resource("AccountDeleteError"));
            if (deletingUserId.HasValue) sessions.DeleteUnlockPreference(deletingUserId.Value);
            if (deletingUserId.HasValue) await pendingPurchases.ClearUserAsync(deletingUserId.Value);
            if (deletingUserId.HasValue) { await documents.DeleteAccountAsync(deletingUserId.Value); await MauiProgram.Services.GetRequiredService<JournalStore>().DeleteAccountAsync(deletingUserId.Value); }
            if (deletingUserId.HasValue) await MauiProgram.Services.GetRequiredService<ExpenseStore>().DeleteAccountAsync(deletingUserId.Value);
            if (deletingUserId.HasValue) await MauiProgram.Services.GetRequiredService<DayPlannerStore>().DeleteAccountAsync(deletingUserId.Value);
            if (deletingContext != sessions.ContextVersion) return;
            await logout.LogoutAsync();
            if (Shell.Current is AppShell shell) shell.ApplySessionTabs(sessions);
            await Shell.Current.GoToAsync("//login");
        });
    }

    private async Task LoadAccountCoreAsync(string token, CancellationToken cancellationToken)
    {
        var account = await api.GetAccountAsync(token, cancellationToken)
            ?? throw new InvalidOperationException(Resource("AccountLoadError"));
        Trips.Clear();
        foreach (var trip in account.Trips)
            Trips.Add(AccountTripItem.From(trip, sessions.CurrentTripId == trip.TripId));
        OnPropertyChanged(nameof(HasTrips));
        OnPropertyChanged(nameof(HasNoTrips));
    }

    private async Task PersistAnalyticsConsentAsync(bool granted)
    {
        try
        {
            var token = await sessions.GetTokenAsync();
            if (!string.IsNullOrWhiteSpace(token))
                await api.UpdateAnalyticsConsentAsync(token, granted);
        }
        catch
        {
            // The local choice is retained and retried the next time the account screen is used.
        }
    }

    private static string Resource(string key) => LocalizationResourceManager.Instance.GetString(key);
}

public sealed record AccountTripItem(
    Guid TripId,
    string Name,
    string DateRange,
    string AccessLabel,
    string ExpiryLabel,
    bool IsCurrent,
    bool IsArchived)
{
    public bool CanArchive => !IsArchived;
    public bool HasExpiry => !string.IsNullOrWhiteSpace(ExpiryLabel);

    public static AccountTripItem From(AccountTripDto trip, bool isCurrent)
    {
        var resources = LocalizationResourceManager.Instance;
        var access = resources.GetString($"AccountAccess{trip.AccessState}");
        if (string.IsNullOrWhiteSpace(access) || access.StartsWith("AccountAccess", StringComparison.Ordinal))
            access = trip.AccessState.ToString();
        var expiry = trip.AccessExpiresAtUtc is { } value
            ? string.Format(resources.GetString("AccountExpiresFormat"), value.ToLocalTime())
            : string.Empty;
        return new(trip.TripId, trip.Name,
            $"{trip.StartsOn:dd/MM/yyyy} – {trip.EndsOn:dd/MM/yyyy}", access, expiry, isCurrent, trip.IsArchived);
    }
}

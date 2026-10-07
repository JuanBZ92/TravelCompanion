using TravelCompanion.Shared.Dtos;
using TravelCompanion.Shared;

namespace TravelCompanion.Mobile.Services;

public enum DeviceUnlockMethod { Biometric, Pin }

public sealed class AuthSessionService
{
    public event EventHandler? StateChanged;
    private long _contextVersion;
    private long _sessionInvalidationVersion;
    private readonly SemaphoreSlim _sessionWriteGate = new(1, 1);
    private readonly object _sessionStateLock = new();
    private const string UserIdKey = "auth_user_id";
    private const string EmailKey = "auth_email";
    private const string EmailVerifiedKey = "auth_email_verified";
    private const string DisplayNameKey = "auth_display_name";
    private const string TripIdKey = "auth_trip_id";
    private const string DestinationNameKey = "auth_destination_name";
    private const string MustChangePasswordKey = "auth_must_change_password";
    private const string BiometricEnabledKey = "auth_biometric_enabled";
    private const string UnlockMethodKeyPrefix = "auth_unlock_method_";
    private const string AccessModeKey = "auth_access_mode";
    private const string ExperienceModeKey = "auth_experience_mode";
    private const string CanEditItineraryKey = "auth_can_edit_itinerary";
    private const string CanSearchGooglePlacesKey = "auth_can_search_google_places";
    private const string HasCuratedDocsKey = "auth_has_curated_docs";
    private const string RequiresTripSetupKey = "auth_requires_trip_setup";
    private const string CanCalculateRoutesKey = "auth_can_calculate_routes";
    private const string AccessExpiresAtUtcKey = "auth_access_expires_at_utc";
    private const string TrialStateKey = "auth_trial_state";
    private const string TrialEditingExpiresAtUtcKey = "auth_trial_editing_expires_at_utc";
    private const string TrialDraftExpiresAtUtcKey = "auth_trial_draft_expires_at_utc";
    private const string TrialAssistantRemainingKey = "auth_trial_assistant_remaining";
    private const string TrialPassPriceKey = "auth_trial_pass_price";
    private const string TrialCurrencyKey = "auth_trial_currency";
    private const string TrialPurchaseUrlKey = "auth_trial_purchase_url";
    private const string TokenKey = "auth_token";
    private const string TokenStorageKey = "auth_token_storage_key";
    private const string StagedTokenPrefix = "auth_token_v2_";
    private const string SignedOutKey = "auth_signed_out";

    public bool HasSession => !Preferences.Default.Get(SignedOutKey, false) && CurrentUserId.HasValue;
    public long ContextVersion { get { lock (_sessionStateLock) return _contextVersion; } }
    public bool MustChangePassword => Preferences.Default.Get(MustChangePasswordKey, false);
    public SessionAccessMode AccessMode
    {
        get
        {
            var value = Preferences.Default.Get(AccessModeKey, SessionAccessMode.Trip.ToString());
            return Enum.TryParse<SessionAccessMode>(value, out var mode) ? mode : SessionAccessMode.Trip;
        }
    }

    public bool IsFreeMapPreview => AccessMode == SessionAccessMode.FreeMapPreview;
    public ExperienceMode ExperienceMode
    {
        get
        {
            var value = Preferences.Default.Get(ExperienceModeKey, TravelCompanion.Shared.Dtos.ExperienceMode.CuratedPremium.ToString());
            return Enum.TryParse<ExperienceMode>(value, out var mode) ? mode : TravelCompanion.Shared.Dtos.ExperienceMode.CuratedPremium;
        }
    }
    public bool IsBuilder => ExperienceMode == TravelCompanion.Shared.Dtos.ExperienceMode.SelfServiceBuilder;
    public bool CanUseAssistant => HasSession && HasKnownValidAccess && AccessMode != SessionAccessMode.BuilderReadOnly
        && (CanEditItinerary || ExperienceMode == TravelCompanion.Shared.Dtos.ExperienceMode.CuratedPremium && !IsFreeMapPreview);
    public bool IsTrial => IsFreeMapPreview && Preferences.Default.ContainsKey(TrialStateKey);
    public FreeAccessPolicy FreePolicy => (FreeAccessPolicy)Preferences.Default.Get("auth_free_policy", 0);
    public int DayImprovementsRemaining => Preferences.Default.Get("auth_day_improvements_remaining", 0);
    public TrialAccessState? TrialState => Enum.TryParse<TrialAccessState>(
        Preferences.Default.Get(TrialStateKey, string.Empty), out var state) ? state : null;
    public DateTimeOffset? TrialEditingExpiresAtUtc => ReadTimestamp(TrialEditingExpiresAtUtcKey);
    public DateTimeOffset? TrialDraftExpiresAtUtc => ReadTimestamp(TrialDraftExpiresAtUtcKey);
    public int TrialAssistantRequestsRemaining => Preferences.Default.Get(TrialAssistantRemainingKey, 0);
    public decimal TrialPassPrice => decimal.TryParse(
        Preferences.Default.Get(TrialPassPriceKey, "24.99"),
        System.Globalization.NumberStyles.Number,
        System.Globalization.CultureInfo.InvariantCulture,
        out var price) ? price : 24.99m;
    public string TrialCurrency => Preferences.Default.Get(TrialCurrencyKey, "EUR");
    public string? TrialPurchaseUrl => Preferences.Default.Get(TrialPurchaseUrlKey, string.Empty) is { Length: > 0 } url ? url : null;
    public bool CanEditItinerary => ExperienceMode != TravelCompanion.Shared.Dtos.ExperienceMode.CuratedPremium
        && Preferences.Default.Get(CanEditItineraryKey, IsBuilder)
        && (!IsTrial
            || TrialState == TrialAccessState.NotStarted
            || FreePolicy == FreeAccessPolicy.PersistentFree && TrialState == TrialAccessState.Editing
            || TrialEditingExpiresAtUtc is { } editingExpiry && editingExpiry > DateTimeOffset.UtcNow);
    public bool CanSearchGooglePlaces => Preferences.Default.Get(CanSearchGooglePlacesKey, !IsFreeMapPreview);
    public bool HasCuratedDocs => Preferences.Default.Get(HasCuratedDocsKey, !IsBuilder && !IsFreeMapPreview);
    public bool RequiresTripSetup => Preferences.Default.Get(RequiresTripSetupKey, IsBuilder && !CurrentTripId.HasValue);
    public bool CanCalculateRoutes => Preferences.Default.Get(CanCalculateRoutesKey, !IsFreeMapPreview);
    public DateTimeOffset? KnownAccessExpiresAtUtc
    {
        get
        {
            var value = Preferences.Default.Get(AccessExpiresAtUtcKey, string.Empty);
            return DateTimeOffset.TryParse(value, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind, out var expiresAt)
                ? expiresAt
                : null;
        }
    }
    public bool HasKnownValidAccess => KnownAccessExpiresAtUtc is not { } expiresAt || expiresAt > DateTimeOffset.UtcNow;
    public DeviceUnlockMethod PreferredUnlockMethod => CurrentUserId is { } userId
        && Enum.TryParse<DeviceUnlockMethod>(Preferences.Default.Get(UnlockMethodKey(userId), string.Empty), out var method)
        && Enum.IsDefined(method) ? method : DeviceUnlockMethod.Biometric;

    // The legacy flag controls the existing default; an explicit choice always
    // requires verification, including when the user chooses PIN instead of biometrics.
    public bool RequiresLocalUnlock => HasSession && !MustChangePassword
        && (Preferences.Default.Get(BiometricEnabledKey, false)
            || CurrentUserId is { } userId && Preferences.Default.ContainsKey(UnlockMethodKey(userId)));

    public bool IsBiometricEnabled
    {
        get => RequiresLocalUnlock && PreferredUnlockMethod == DeviceUnlockMethod.Biometric;
        set => SetUnlockMethod(value ? DeviceUnlockMethod.Biometric : DeviceUnlockMethod.Pin);
    }

    public void SetUnlockMethod(DeviceUnlockMethod method)
    {
        if (!Enum.IsDefined(method)) throw new ArgumentOutOfRangeException(nameof(method));
        lock (_sessionStateLock)
        {
            if (!HasSession || CurrentUserId is not { } userId) return;
            Preferences.Default.Set(UnlockMethodKey(userId), method.ToString());
            Interlocked.Increment(ref _contextVersion);
        }
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void DeleteUnlockPreference(Guid userId) => Preferences.Default.Remove(UnlockMethodKey(userId));

    private static string UnlockMethodKey(Guid userId) => UnlockMethodKeyPrefix + userId.ToString("N");

    public Guid? CurrentUserId
    {
        get
        {
            var value = Preferences.Default.Get(UserIdKey, string.Empty);
            return Guid.TryParse(value, out var userId) ? userId : null;
        }
    }

    public string? CurrentEmail
    {
        get
        {
            var value = Preferences.Default.Get(EmailKey, string.Empty);
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }
    }

    public string? CurrentDisplayName
    {
        get
        {
            var value = Preferences.Default.Get(DisplayNameKey, string.Empty);
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }
    }

    public bool EmailVerified => Preferences.Default.Get(EmailVerifiedKey, false);

    public Guid? CurrentTripId
    {
        get
        {
            var value = Preferences.Default.Get(TripIdKey, string.Empty);
            return Guid.TryParse(value, out var tripId) ? tripId : null;
        }
    }

    public string? CurrentDestinationName
    {
        get
        {
            var value = Preferences.Default.Get(DestinationNameKey, string.Empty);
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }
    }

    public async Task SaveAsync(AuthSessionDto session) => await SaveCoreAsync(session, null);

    public Task<bool> SaveIfCurrentAsync(AuthSessionDto session, long expectedContextVersion) =>
        SaveCoreAsync(session, expectedContextVersion);

    private async Task<bool> SaveCoreAsync(AuthSessionDto session, long? expectedContextVersion)
    {
        long invalidation;
        lock (_sessionStateLock) invalidation = _sessionInvalidationVersion;
        await _sessionWriteGate.WaitAsync().ConfigureAwait(false);
        string? stagedKey = null;
        try
        {
            lock (_sessionStateLock)
            {
                if (invalidation != _sessionInvalidationVersion
                    || expectedContextVersion.HasValue && expectedContextVersion.Value != _contextVersion) return false;
            }
            stagedKey = StagedTokenPrefix + Guid.NewGuid().ToString("N");
            // Publish the pointer and profile together only after encrypted token persistence.
            // An obsolete secure-storage continuation never overwrites the active token slot.
            await SecureStorage.Default.SetAsync(stagedKey, session.Token).ConfigureAwait(false);
            string previousKey;
            lock (_sessionStateLock)
            {
                if (invalidation != _sessionInvalidationVersion
                    || expectedContextVersion.HasValue && expectedContextVersion.Value != _contextVersion)
                {
                    RemoveTokenQuietly(stagedKey);
                    return false;
                }
                previousKey = Preferences.Default.Get(TokenStorageKey, TokenKey);
                PublishSession(session, stagedKey);
                stagedKey = null;
            }
            RemoveTokenQuietly(previousKey);
            if (previousKey != TokenKey) RemoveTokenQuietly(TokenKey);
            StateChanged?.Invoke(this, EventArgs.Empty);
            return true;
        }
        finally
        {
            if (stagedKey is not null) RemoveTokenQuietly(stagedKey);
            _sessionWriteGate.Release();
        }
    }

    private static void RemoveTokenQuietly(string key)
    {
        try { SecureStorage.Default.Remove(key); }
        catch (Exception error) { ClientDiagnostics.Record("auth_token_cleanup_failed", exception: error); }
    }

    private void PublishSession(AuthSessionDto session, string storageKey)
    {
        if (CurrentUserId != session.UserId || CurrentTripId != session.TripId)
            Preferences.Default.Remove(AccessExpiresAtUtcKey);
        Preferences.Default.Set(UserIdKey, session.UserId.ToString());
        Preferences.Default.Set(EmailKey, session.Email);
        Preferences.Default.Set(EmailVerifiedKey, session.EmailVerified);
        Preferences.Default.Set(DisplayNameKey, session.DisplayName);
        if (session.TripId.HasValue)
        {
            Preferences.Default.Set(TripIdKey, session.TripId.Value.ToString());
        }
        else
        {
            Preferences.Default.Remove(TripIdKey);
        }

        if (!string.IsNullOrWhiteSpace(session.DestinationName))
        {
            Preferences.Default.Set(DestinationNameKey, session.DestinationName);
        }
        else
        {
            Preferences.Default.Remove(DestinationNameKey);
        }

        Preferences.Default.Set(MustChangePasswordKey, session.MustChangePassword);
        Preferences.Default.Set(AccessModeKey, session.AccessMode.ToString());
        Preferences.Default.Set(ExperienceModeKey, session.ExperienceMode.ToString());
        Preferences.Default.Set(CanEditItineraryKey, session.Capabilities?.CanEditItinerary ?? session.AccessMode == SessionAccessMode.Builder);
        Preferences.Default.Set(CanSearchGooglePlacesKey, session.Capabilities?.CanSearchGooglePlaces ?? session.AccessMode != SessionAccessMode.FreeMapPreview);
        Preferences.Default.Set(HasCuratedDocsKey, session.Capabilities?.HasCuratedDocs ?? session.AccessMode == SessionAccessMode.Trip);
        Preferences.Default.Set(RequiresTripSetupKey, session.Capabilities?.RequiresTripSetup ?? false);
        Preferences.Default.Set(CanCalculateRoutesKey, session.Capabilities?.CanCalculateRoutes ?? session.AccessMode != SessionAccessMode.FreeMapPreview);
        Preferences.Default.Set(
            BiometricEnabledKey,
            session.AccessMode != SessionAccessMode.FreeMapPreview && !session.MustChangePassword);
        ApplyTrialAccess(session.TrialAccess);
        Preferences.Default.Set(TokenStorageKey, storageKey);
        Preferences.Default.Set(SignedOutKey, false);
        Interlocked.Increment(ref _contextVersion);
    }

    public async Task<string?> GetTokenAsync()
    {
        string storageKey;
        long context;
        lock (_sessionStateLock)
        {
            if (!HasSession) return null;
            storageKey = Preferences.Default.Get(TokenStorageKey, TokenKey);
            context = _contextVersion;
        }
        try
        {
            var token = await SecureStorage.Default.GetAsync(storageKey).ConfigureAwait(false);
            lock (_sessionStateLock)
                return HasSession && context == _contextVersion
                    && storageKey == Preferences.Default.Get(TokenStorageKey, TokenKey) ? token : null;
        }
        catch
        {
            return null;
        }
    }

    public void MarkPasswordChanged()
    {
        lock (_sessionStateLock)
        {
            Preferences.Default.Set(MustChangePasswordKey, false);
            Preferences.Default.Set(BiometricEnabledKey, true);
        }
    }

    public void ApplyCapabilities(TravelerCapabilitiesDto capabilities)
    {
        lock (_sessionStateLock)
        {
            Preferences.Default.Set(CanEditItineraryKey, capabilities.CanEditItinerary);
            Preferences.Default.Set(CanSearchGooglePlacesKey, capabilities.CanSearchGooglePlaces);
            Preferences.Default.Set(HasCuratedDocsKey, capabilities.HasCuratedDocs);
            Preferences.Default.Set(RequiresTripSetupKey, capabilities.RequiresTripSetup);
            Preferences.Default.Set(CanCalculateRoutesKey, capabilities.CanCalculateRoutes);
        }
    }

    public void ApplySyncState(MobileSyncStateDto state)
    {
        lock (_sessionStateLock)
        {
            if (state.AccessMode.HasValue)
                Preferences.Default.Set(AccessModeKey, state.AccessMode.Value.ToString());
            ApplyCapabilities(state.Capabilities);
            Preferences.Default.Set(AccessExpiresAtUtcKey, state.AccessExpiresAtUtc.ToString("O"));
            ApplyTrialAccess(state.TrialAccess);
        }
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ApplyTrialAccess(TrialAccessStatusDto? trial)
    {
        lock (_sessionStateLock)
        {
            if (trial is null || !trial.IsTrial)
            {
                ClearTrialAccess();
                return;
            }

            Preferences.Default.Set(TrialStateKey, trial.State.ToString());
            Preferences.Default.Set("auth_free_policy", (int)trial.FreePolicy);
            Preferences.Default.Set("auth_day_improvements_remaining", trial.DayImprovementsRemaining);
            SetTimestamp(TrialEditingExpiresAtUtcKey, trial.EditingExpiresAtUtc);
            SetTimestamp(TrialDraftExpiresAtUtcKey, trial.DraftExpiresAtUtc);
            Preferences.Default.Set(TrialAssistantRemainingKey, trial.AssistantRequestsRemaining);
            Preferences.Default.Set(TrialPassPriceKey, trial.PassPrice.ToString(System.Globalization.CultureInfo.InvariantCulture));
            Preferences.Default.Set(TrialCurrencyKey, trial.Currency);
            Preferences.Default.Set(TrialPurchaseUrlKey, trial.PurchaseUrl ?? string.Empty);
            Preferences.Default.Set(CanEditItineraryKey, trial.CanEdit);
            Interlocked.Increment(ref _contextVersion);
        }
    }

    public void MarkTripConfigured(Guid tripId, string? destinationName = null)
    {
        lock (_sessionStateLock)
        {
            Preferences.Default.Set(TripIdKey, tripId.ToString());
            Preferences.Default.Set(RequiresTripSetupKey, false);
            if (!string.IsNullOrWhiteSpace(destinationName))
            {
                Preferences.Default.Set(DestinationNameKey, destinationName);
            }
            Interlocked.Increment(ref _contextVersion);
        }
    }

    public void MarkTripDeleted()
    {
        lock (_sessionStateLock)
        {
            Preferences.Default.Remove(TripIdKey);
            Preferences.Default.Set(RequiresTripSetupKey, true);
            Interlocked.Increment(ref _contextVersion);
        }
    }

    public void BeginLogout()
    {
        lock (_sessionStateLock)
        {
            // Persist before navigation/native cleanup: a killed process must reopen at PIN.
            Preferences.Default.Set(SignedOutKey, true);
            Preferences.Default.Remove(BiometricEnabledKey);
            Interlocked.Increment(ref _contextVersion);
            _sessionInvalidationVersion++;
        }
    }

    public void Clear()
    {
        lock (_sessionStateLock)
        {
            BeginLogout();
            Preferences.Default.Remove(UserIdKey);
            Preferences.Default.Remove(EmailKey);
            Preferences.Default.Remove(EmailVerifiedKey);
            Preferences.Default.Remove(DisplayNameKey);
            Preferences.Default.Remove(TripIdKey);
            Preferences.Default.Remove(DestinationNameKey);
            Preferences.Default.Remove(MustChangePasswordKey);
            Preferences.Default.Remove(BiometricEnabledKey);
            Preferences.Default.Remove(AccessModeKey);
            Preferences.Default.Remove(ExperienceModeKey);
            Preferences.Default.Remove(CanEditItineraryKey);
            Preferences.Default.Remove(CanSearchGooglePlacesKey);
            Preferences.Default.Remove(HasCuratedDocsKey);
            Preferences.Default.Remove(RequiresTripSetupKey);
            Preferences.Default.Remove(CanCalculateRoutesKey);
            Preferences.Default.Remove(AccessExpiresAtUtcKey);
            ClearTrialAccess();
            var activeTokenKey = Preferences.Default.Get(TokenStorageKey, TokenKey);
            RemoveTokenQuietly(activeTokenKey);
            Preferences.Default.Remove(TokenStorageKey);
            RemoveTokenQuietly(TokenKey);
            Interlocked.Increment(ref _contextVersion);
        }
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private static DateTimeOffset? ReadTimestamp(string key) =>
        DateTimeOffset.TryParse(
            Preferences.Default.Get(key, string.Empty),
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.RoundtripKind,
            out var value) ? value : null;

    private static void SetTimestamp(string key, DateTimeOffset? value)
    {
        if (value.HasValue) Preferences.Default.Set(key, value.Value.ToString("O"));
        else Preferences.Default.Remove(key);
    }

    private static void ClearTrialAccess()
    {
        Preferences.Default.Remove(TrialStateKey);
        Preferences.Default.Remove(TrialEditingExpiresAtUtcKey);
        Preferences.Default.Remove("auth_free_policy");
        Preferences.Default.Remove("auth_day_improvements_remaining");
        Preferences.Default.Remove(TrialDraftExpiresAtUtcKey);
        Preferences.Default.Remove(TrialAssistantRemainingKey);
        Preferences.Default.Remove(TrialPassPriceKey);
        Preferences.Default.Remove(TrialCurrencyKey);
        Preferences.Default.Remove(TrialPurchaseUrlKey);
    }
}

using TravelCompanion.Shared.Dtos;
using TravelCompanion.Shared;

namespace TravelCompanion.Mobile.Services;

public sealed class AuthSessionService
{
    private const string UserIdKey = "auth_user_id";
    private const string EmailKey = "auth_email";
    private const string DisplayNameKey = "auth_display_name";
    private const string TripIdKey = "auth_trip_id";
    private const string DestinationNameKey = "auth_destination_name";
    private const string MustChangePasswordKey = "auth_must_change_password";
    private const string BiometricEnabledKey = "auth_biometric_enabled";
    private const string AccessModeKey = "auth_access_mode";
    private const string ExperienceModeKey = "auth_experience_mode";
    private const string CanEditItineraryKey = "auth_can_edit_itinerary";
    private const string HasCuratedDocsKey = "auth_has_curated_docs";
    private const string RequiresTripSetupKey = "auth_requires_trip_setup";
    private const string TokenKey = "auth_token";

    public bool HasSession => CurrentUserId.HasValue;
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
    public bool CanEditItinerary => Preferences.Default.Get(CanEditItineraryKey, IsBuilder);
    public bool HasCuratedDocs => Preferences.Default.Get(HasCuratedDocsKey, !IsBuilder && !IsFreeMapPreview);
    public bool RequiresTripSetup => Preferences.Default.Get(RequiresTripSetupKey, IsBuilder && !CurrentTripId.HasValue);
    public bool IsBiometricEnabled
    {
        get => Preferences.Default.Get(BiometricEnabledKey, false);
        set => Preferences.Default.Set(BiometricEnabledKey, value);
    }

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

    public async Task SaveAsync(AuthSessionDto session)
    {
        Preferences.Default.Set(UserIdKey, session.UserId.ToString());
        Preferences.Default.Set(EmailKey, session.Email);
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
        Preferences.Default.Set(HasCuratedDocsKey, session.Capabilities?.HasCuratedDocs ?? session.AccessMode == SessionAccessMode.Trip);
        Preferences.Default.Set(RequiresTripSetupKey, session.Capabilities?.RequiresTripSetup ?? false);
        Preferences.Default.Set(
            BiometricEnabledKey,
            session.AccessMode != SessionAccessMode.FreeMapPreview && !session.MustChangePassword);
        await SecureStorage.Default.SetAsync(TokenKey, session.Token).ConfigureAwait(false);
    }

    public async Task<string?> GetTokenAsync()
    {
        try
        {
            return await SecureStorage.Default.GetAsync(TokenKey).ConfigureAwait(false);
        }
        catch
        {
            return null;
        }
    }

    public void MarkPasswordChanged()
    {
        Preferences.Default.Set(MustChangePasswordKey, false);
        Preferences.Default.Set(BiometricEnabledKey, true);
    }

    public void MarkTripConfigured(Guid tripId, string? destinationName = null)
    {
        Preferences.Default.Set(TripIdKey, tripId.ToString());
        Preferences.Default.Set(RequiresTripSetupKey, false);
        if (!string.IsNullOrWhiteSpace(destinationName))
        {
            Preferences.Default.Set(DestinationNameKey, destinationName);
        }
    }

    public void Clear()
    {
        Preferences.Default.Remove(UserIdKey);
        Preferences.Default.Remove(EmailKey);
        Preferences.Default.Remove(DisplayNameKey);
        Preferences.Default.Remove(TripIdKey);
        Preferences.Default.Remove(DestinationNameKey);
        Preferences.Default.Remove(MustChangePasswordKey);
        Preferences.Default.Remove(BiometricEnabledKey);
        Preferences.Default.Remove(AccessModeKey);
        Preferences.Default.Remove(ExperienceModeKey);
        Preferences.Default.Remove(CanEditItineraryKey);
        Preferences.Default.Remove(HasCuratedDocsKey);
        Preferences.Default.Remove(RequiresTripSetupKey);
        SecureStorage.Default.Remove(TokenKey);
    }
}

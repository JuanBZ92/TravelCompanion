using System.ComponentModel.DataAnnotations;

namespace TravelCompanion.Shared.Dtos;

public sealed record LoginRequestDto(
    [param: Required]
    [param: EmailAddress]
    [param: MaxLength(180)]
    string Email,
    [param: Required]
    [param: MaxLength(256)]
    string Password);

public sealed record PinLoginRequestDto(
    [param: Required]
    [param: RegularExpression(@"^(\d{4}|\d{6})$")]
    string Pin,
    [param: MaxLength(128)] string? ClientInstanceId = null,
    bool SupportsPersistentFree = false);

public enum ExperienceMode
{
    FreePreview,
    SelfServiceBuilder,
    CuratedPremium
}

public sealed record TravelerCapabilitiesDto(
    bool CanViewFullMap,
    bool CanSearchGooglePlaces,
    bool CanEditItinerary,
    bool HasCuratedDocs,
    bool RequiresTripSetup,
    bool CanCalculateRoutes = false);

public sealed record MobileSyncStateDto(
    TravelerCapabilitiesDto Capabilities,
    DateTimeOffset AccessExpiresAtUtc,
    Guid? TripId,
    Guid? DestinationId,
    string? DestinationSlug,
    long CatalogVersion,
    int ItineraryVersion,
    long DocumentsVersion,
    long TodayPersonalizationVersion,
    long FreeCatalogVersion,
    int CacheFormatVersion = 1,
    TrialAccessStatusDto? TrialAccess = null,
    SessionAccessMode? AccessMode = null);

public enum TrialAccessState
{
    NoAccess,
    NotStarted,
    Editing,
    ReadOnly,
    PurchasePending,
    Expired,
    Revoked,
    Paid
}

public sealed record TrialAccessStatusDto(
    bool IsTrial,
    TrialAccessState State,
    DateTimeOffset? EditingExpiresAtUtc,
    DateTimeOffset? DraftExpiresAtUtc,
    int AssistantRequestsRemaining,
    decimal PassPrice,
    string Currency,
    string? PurchaseUrl)
{
    public FreeAccessPolicy FreePolicy { get; init; }
    public int DayImprovementsRemaining { get; init; }
    public bool CanEdit => State is TrialAccessState.NotStarted or TrialAccessState.Editing or TrialAccessState.Paid;
    public bool CanUseAssistant => State == TrialAccessState.Paid || AssistantRequestsRemaining > 0;
}

public sealed record RequestEmailCodeDto(
    [param: Required, EmailAddress, MaxLength(180)] string Email,
    [param: MaxLength(16)] string? Locale = null);

public sealed record VerifyEmailCodeDto(
    [param: Required, EmailAddress, MaxLength(180)] string Email,
    [param: Required, RegularExpression(@"^\d{6}$")] string Code);

public sealed record EmailCodeRequestedDto(DateTimeOffset ExpiresAtUtc, DateTimeOffset ResendAvailableAtUtc);

public sealed record AccountTripDto(
    Guid TripId,
    string Name,
    DateOnly StartsOn,
    DateOnly EndsOn,
    TrialAccessState AccessState,
    DateTimeOffset? AccessExpiresAtUtc,
    bool IsArchived);

public sealed record TravelerAccountDto(
    Guid UserId,
    string Email,
    bool EmailVerified,
    IReadOnlyList<AccountTripDto> Trips,
    bool BehaviorAnalyticsConsent = false);

public sealed record SelectAccountTripDto(Guid TripId);

public sealed record UpdateAnalyticsConsentDto(bool Granted);

public sealed record RedeemTravelPassRequest(
    [param: Required]
    [param: RegularExpression(@"^(\d{4}|\d{6})$")]
    string Pin);

public sealed record ChangePasswordRequestDto(
    [param: MaxLength(256)]
    string? CurrentPassword,
    [param: Required]
    [param: MinLength(12)]
    [param: MaxLength(256)]
    string NewPassword);

public sealed record AuthSessionDto(
    Guid UserId,
    string Email,
    string DisplayName,
    bool MustChangePassword,
    string Token,
    Guid? TripId = null,
    string? DestinationName = null,
    SessionAccessMode AccessMode = SessionAccessMode.Trip,
    ExperienceMode ExperienceMode = ExperienceMode.CuratedPremium,
    TravelerCapabilitiesDto? Capabilities = null,
    TrialAccessStatusDto? TrialAccess = null,
    bool EmailVerified = false);

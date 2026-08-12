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
    string Pin);

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
    bool RequiresTripSetup);

public sealed record ChangePasswordRequestDto(
    [property: MaxLength(256)]
    string? CurrentPassword,
    [property: Required]
    [property: MinLength(12)]
    [property: MaxLength(256)]
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
    TravelerCapabilitiesDto? Capabilities = null);

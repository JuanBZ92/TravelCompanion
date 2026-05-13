using System.ComponentModel.DataAnnotations;

namespace TravelCompanion.Shared.Dtos;

public sealed record LoginRequestDto(
    [property: Required]
    [property: EmailAddress]
    [property: MaxLength(180)]
    string Email,
    [property: Required]
    [property: MaxLength(256)]
    string Password);

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
    string Token);

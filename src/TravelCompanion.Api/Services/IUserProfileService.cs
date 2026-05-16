using TravelCompanion.Api.Models;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Api.Services;

public interface IUserProfileService
{
    Task<TravelPreferenceProfile?> GetProfileAsync(Guid userId, CancellationToken cancellationToken);

    Task<TravelPreferenceProfileDto> GetProfileDtoAsync(Guid userId, CancellationToken cancellationToken);

    Task<TravelPreferenceProfileDto> PatchProfileAsync(
        Guid userId,
        TravelPreferenceProfilePatchDto patch,
        CancellationToken cancellationToken);

    bool HasMinimumPreferences(TravelPreferenceProfile? profile, out IReadOnlyList<string> missingFields);

    TravelPreferenceProfileDto ToDto(TravelPreferenceProfile? profile, Guid userId);
}

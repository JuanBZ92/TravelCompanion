using Microsoft.EntityFrameworkCore;
using TravelCompanion.Api.Data;
using TravelCompanion.Api.Models;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Api.Services;

public sealed class UserProfileService(TravelCompanionDbContext dbContext) : IUserProfileService
{
    private static readonly HashSet<string> ValidBudgetLevels = new(StringComparer.OrdinalIgnoreCase)
    {
        "free",
        "low",
        "medium",
        "high"
    };

    private static readonly HashSet<string> ValidTravelPaces = new(StringComparer.OrdinalIgnoreCase)
    {
        "relaxed",
        "balanced",
        "efficient"
    };

    public async Task<TravelPreferenceProfile?> GetProfileAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        return await dbContext.TravelPreferenceProfiles
            .FirstOrDefaultAsync(profile => profile.UserId == userId, cancellationToken);
    }

    public async Task<TravelPreferenceProfileDto> GetProfileDtoAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        return ToDto(await GetProfileAsync(userId, cancellationToken), userId);
    }

    public async Task<TravelPreferenceProfileDto> PatchProfileAsync(
        Guid userId,
        TravelPreferenceProfilePatchDto patch,
        CancellationToken cancellationToken)
    {
        var profile = await GetProfileAsync(userId, cancellationToken);
        if (profile is null)
        {
            profile = new TravelPreferenceProfile
            {
                UserId = userId
            };
            dbContext.TravelPreferenceProfiles.Add(profile);
        }

        if (patch.FoodPreferences is not null)
        {
            profile.FoodPreferences = NormalizeList(patch.FoodPreferences);
        }

        if (patch.DietaryRestrictions is not null)
        {
            profile.DietaryRestrictions = NormalizeList(patch.DietaryRestrictions);
        }

        if (patch.Interests is not null)
        {
            profile.Interests = NormalizeList(patch.Interests);
        }

        if (patch.Dislikes is not null)
        {
            profile.Dislikes = NormalizeList(patch.Dislikes);
        }

        if (!string.IsNullOrWhiteSpace(patch.BudgetLevel))
        {
            profile.BudgetLevel = NormalizeAllowedValue(
                patch.BudgetLevel,
                ValidBudgetLevels,
                nameof(patch.BudgetLevel));
        }

        if (!string.IsNullOrWhiteSpace(patch.TravelPace))
        {
            profile.TravelPace = NormalizeAllowedValue(
                patch.TravelPace,
                ValidTravelPaces,
                nameof(patch.TravelPace));
        }

        if (patch.AvoidTouristTraps.HasValue)
        {
            profile.AvoidTouristTraps = patch.AvoidTouristTraps.Value;
        }

        if (patch.MaxWalkingMinutes.HasValue)
        {
            if (patch.MaxWalkingMinutes.Value is < 5 or > 180)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(patch.MaxWalkingMinutes),
                    "Max walking minutes must be between 5 and 180.");
            }

            profile.MaxWalkingMinutes = patch.MaxWalkingMinutes.Value;
        }

        profile.UpdatedAt = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);

        return ToDto(profile, userId);
    }

    public bool HasMinimumPreferences(
        TravelPreferenceProfile? profile,
        out IReadOnlyList<string> missingFields)
    {
        var missing = new List<string>();

        if (profile is null || profile.Interests.Count == 0)
        {
            missing.Add("interests");
        }

        if (profile is null || string.IsNullOrWhiteSpace(profile.BudgetLevel))
        {
            missing.Add("budgetLevel");
        }

        if (profile is null || string.IsNullOrWhiteSpace(profile.TravelPace))
        {
            missing.Add("travelPace");
        }

        missingFields = missing;
        return missing.Count == 0;
    }

    public TravelPreferenceProfileDto ToDto(TravelPreferenceProfile? profile, Guid userId)
    {
        HasMinimumPreferences(profile, out var missingFields);

        return new TravelPreferenceProfileDto(
            userId,
            profile?.FoodPreferences.ToList() ?? [],
            profile?.DietaryRestrictions.ToList() ?? [],
            string.IsNullOrWhiteSpace(profile?.BudgetLevel) ? "medium" : profile.BudgetLevel,
            string.IsNullOrWhiteSpace(profile?.TravelPace) ? "balanced" : profile.TravelPace,
            profile?.Interests.ToList() ?? [],
            profile?.Dislikes.ToList() ?? [],
            profile?.AvoidTouristTraps ?? true,
            profile?.MaxWalkingMinutes ?? 25,
            missingFields.Count == 0,
            missingFields,
            profile?.UpdatedAt);
    }

    private static List<string> NormalizeList(IEnumerable<string> values)
    {
        return values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(25)
            .ToList();
    }

    private static string NormalizeAllowedValue(
        string value,
        HashSet<string> allowedValues,
        string fieldName)
    {
        var normalized = value.Trim().ToLowerInvariant();
        if (!allowedValues.Contains(normalized))
        {
            throw new ArgumentException(
                $"{fieldName} must be one of: {string.Join(", ", allowedValues.Order())}.",
                fieldName);
        }

        return normalized;
    }
}

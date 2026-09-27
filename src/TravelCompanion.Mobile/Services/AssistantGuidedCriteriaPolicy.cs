using System.Diagnostics.CodeAnalysis;
using TravelCompanion.Shared;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Mobile.Services;

public static class AssistantGuidedCriteriaPolicy
{
    public static bool HasValidCategory([NotNullWhen(true)] GuidedPlanCriteriaDto? criteria) =>
        criteria is not null && (GuidedTravelCategories.IsValid(criteria.Category)
            || criteria.Categories?.Any(GuidedTravelCategories.IsValid) == true);
}

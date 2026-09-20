namespace TravelCompanion.Api.Services;

internal static class RecommendationBudget
{
    public static int GetRank(string? value)
    {
        return value?.Trim().ToLowerInvariant() switch
        {
            "free" or "gratis" => 0,
            "low" or "budget" or "cheap" or "barato" => 1,
            "medium" or "moderate" or "medio" => 2,
            "high" or "expensive" or "premium" or "alto" => 3,
            _ => 2
        };
    }
}

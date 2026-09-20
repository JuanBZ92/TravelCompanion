namespace TravelCompanion.Mobile.Services;

internal static class RecommendationPriceFormatter
{
    public static string Format(string? priceLevel)
    {
        return priceLevel?.Trim().ToLowerInvariant() switch
        {
            "free" or "gratis" => "Gratis",
            "low" or "budget" or "cheap" or "barato" => "Bajo",
            "medium" or "moderate" or "medio" => "Medio",
            "high" or "expensive" or "premium" or "alto" => "Alto",
            _ => string.IsNullOrWhiteSpace(priceLevel) ? "Medio" : priceLevel.Trim()
        };
    }
}

namespace TravelCompanion.Api.Services;

public sealed class TravelPreferenceProfile
{
    public required string UserId { get; init; }
    public List<string> FoodPreferences { get; init; } = [];
    public List<string> DietaryRestrictions { get; init; } = [];
    public string BudgetLevel { get; set; } = "medium";
    public string TravelPace { get; set; } = "balanced";
    public List<string> Interests { get; init; } = [];
    public List<string> Dislikes { get; init; } = [];
    public bool AvoidTouristTraps { get; set; } = true;
    public int MaxWalkingMinutes { get; set; } = 25;
}

namespace TravelCompanion.Api.Models;

public sealed class TravelPreferenceProfile
{
    public Guid UserId { get; set; }
    public AppUser? User { get; set; }
    public List<string> FoodPreferences { get; set; } = [];
    public List<string> DietaryRestrictions { get; set; } = [];
    public string BudgetLevel { get; set; } = "medium";
    public string TravelPace { get; set; } = "balanced";
    public List<string> Interests { get; set; } = [];
    public List<string> Dislikes { get; set; } = [];
    public bool AvoidTouristTraps { get; set; } = true;
    public int MaxWalkingMinutes { get; set; } = 25;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

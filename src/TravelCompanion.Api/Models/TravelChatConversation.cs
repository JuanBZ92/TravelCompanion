namespace TravelCompanion.Api.Models;

public sealed class TravelChatConversation
{
    public string Id { get; set; } = string.Empty;
    public Guid UserId { get; set; }
    public AppUser? User { get; set; }
    public string? LastCity { get; set; }
    public DateOnly? LastDate { get; set; }
    public string LastResponseMode { get; set; } = "balanced";
    public string? LastRecommendationIds { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

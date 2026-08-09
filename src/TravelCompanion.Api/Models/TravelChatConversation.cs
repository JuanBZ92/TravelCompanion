namespace TravelCompanion.Api.Models;

public sealed class TravelChatConversation
{
    public string Id { get; set; } = string.Empty;
    public Guid UserId { get; set; }
    public AppUser? User { get; set; }
    public string? LastCity { get; set; }
    public DateOnly? LastDate { get; set; }
    public string? LastIntent { get; set; }
    public string? LastLocale { get; set; }
    public string? LastPromptVersion { get; set; }
    public string LastResponseMode { get; set; } = "balanced";
    public string? LastRecommendationIds { get; set; }
    public string? StateJson { get; set; }
    public string? PendingPreferencePatchJson { get; set; }
    public string? PendingPreferenceOriginalMessage { get; set; }
    public DateTimeOffset? PendingPreferenceRequestedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public List<TravelAssistantFeedback> FeedbackItems { get; set; } = [];
}

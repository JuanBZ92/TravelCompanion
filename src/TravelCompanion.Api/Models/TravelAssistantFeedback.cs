using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Api.Models;

public sealed class TravelAssistantFeedback
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public AppUser? User { get; set; }
    public string ConversationId { get; set; } = string.Empty;
    public TravelChatConversation? Conversation { get; set; }
    public Guid RecommendationId { get; set; }
    public Recommendation? Recommendation { get; set; }
    public TravelAssistantFeedbackSignal Signal { get; set; }
    public string? Locale { get; set; }
    public string? Intent { get; set; }
    public string? ResponseMode { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}

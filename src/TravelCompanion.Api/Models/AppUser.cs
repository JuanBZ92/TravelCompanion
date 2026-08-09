namespace TravelCompanion.Api.Models;

public sealed class AppUser
{
    public Guid Id { get; set; }
    public required string Email { get; set; }
    public required string DisplayName { get; set; }
    public string? PasswordHash { get; set; }
    public bool MustChangePassword { get; set; } = true;
    public DateTimeOffset? TemporaryPasswordIssuedAt { get; set; }
    public DateTimeOffset? PasswordChangedAt { get; set; }
    public List<UserEntitlement> Entitlements { get; set; } = [];
    public List<Trip> Trips { get; set; } = [];
    public List<AppUserSession> Sessions { get; set; } = [];
    public List<NotificationDeviceRegistration> NotificationDevices { get; set; } = [];
    public List<NotificationOutboxItem> NotificationOutboxItems { get; set; } = [];
    public TravelPreferenceProfile? TravelPreferenceProfile { get; set; }
    public List<TravelChatConversation> TravelChatConversations { get; set; } = [];
    public List<TravelAssistantFeedback> TravelAssistantFeedbackItems { get; set; } = [];
}

namespace TravelCompanion.Shared.Dtos;

public sealed record TravelChatRequest(
    string Message,
    string? ConversationId,
    string? City,
    DateOnly? Date,
    GeoPointDto? CurrentLocation,
    string? Locale,
    GuidedTravelActionDto? GuidedAction = null,
    GuidedPlanCriteriaDto? Criteria = null);

public sealed record GeoPointDto(
    decimal Latitude,
    decimal Longitude);

public sealed record TravelChatResponse(
    string ConversationId,
    string Message,
    string Intent,
    IReadOnlyList<TravelCardDto> Cards,
    IReadOnlyList<string> SuggestedReplies,
    MissingContextDto? MissingContext,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    GuidedQuestionDto? GuidedQuestion = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    GuidedPlanCriteriaDto? Criteria = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    TrialAccessStatusDto? TrialAccess = null);

public sealed record GuidedTravelActionDto(
    string Action,
    string? OptionId = null,
    string? RecommendationId = null);

public sealed record GuidedPlanCriteriaDto(
    string? Category = null,
    string? Priority = null,
    string? Budget = null,
    int? MaxWalkingMinutes = null,
    int? MaxDurationMinutes = null)
{
    public IReadOnlyList<string> Categories { get; init; } = [];
    public IReadOnlyList<string> Budgets { get; init; } = [];
    public IReadOnlyList<int> WalkingMinuteOptions { get; init; } = [];
}

public sealed record GuidedQuestionDto(
    string Id,
    string Message,
    IReadOnlyList<GuidedOptionDto> Options,
    bool CanGoBack = true,
    bool CanRestart = true);

public sealed record GuidedOptionDto(string Id, string Label);

public sealed record TravelCardDto(
    string Type,
    string Title,
    string? Subtitle,
    string? Description,
    string? StartTime,
    string? EndTime,
    string? EstimatedCost,
    double? DistanceKm,
    int? WalkingMinutes,
    IReadOnlyList<string> WhyItFits,
    IReadOnlyList<string> Warnings,
    string? RecommendationId,
    string? ReservationId)
{
    public IReadOnlyList<string> Tags { get; init; } = [];
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? ProviderPlaceId { get; init; }
}

public sealed record MissingContextDto(
    string Field,
    string Message,
    IReadOnlyList<string> Suggestions);

public sealed record TravelPreferenceProfileDto(
    Guid UserId,
    IReadOnlyList<string> FoodPreferences,
    IReadOnlyList<string> DietaryRestrictions,
    string BudgetLevel,
    string TravelPace,
    IReadOnlyList<string> Interests,
    IReadOnlyList<string> Dislikes,
    bool AvoidTouristTraps,
    int MaxWalkingMinutes,
    bool HasMinimumPreferences,
    IReadOnlyList<string> MissingFields,
    DateTimeOffset? UpdatedAt);

public sealed record TravelPreferenceProfilePatchDto(
    IReadOnlyList<string>? FoodPreferences,
    IReadOnlyList<string>? DietaryRestrictions,
    string? BudgetLevel,
    string? TravelPace,
    IReadOnlyList<string>? Interests,
    IReadOnlyList<string>? Dislikes,
    bool? AvoidTouristTraps,
    int? MaxWalkingMinutes);

public sealed record SaveItineraryItemRequest(
    Guid RecommendationId,
    DateOnly Date,
    TimeOnly StartsAt,
    TimeOnly? EndsAt,
    Guid? ClientMutationId = null);

public sealed record SaveItineraryItemResponse(
    bool Saved,
    string Message,
    ScheduleItemDto? Item,
    int? Revision = null);

public enum TravelAssistantFeedbackSignal
{
    Helpful,
    NotHelpful,
    HideSimilar
}

public sealed record TravelAssistantFeedbackRequest(
    string ConversationId,
    Guid RecommendationId,
    TravelAssistantFeedbackSignal Signal,
    string? Locale,
    string? Intent,
    string? ResponseMode);

public sealed record TravelAssistantFeedbackResponse(
    bool Accepted,
    string Message);

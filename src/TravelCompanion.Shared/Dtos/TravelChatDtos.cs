namespace TravelCompanion.Shared.Dtos;

public sealed record TravelChatRequest(
    string Message,
    string? ConversationId,
    string? City,
    DateOnly? Date,
    GeoPointDto? CurrentLocation,
    string? Locale,
    GuidedTravelActionDto? GuidedAction = null,
    GuidedPlanCriteriaDto? Criteria = null,
    Guid? OperationId = null);

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
    string? RecommendationId = null)
{
    public IReadOnlyList<Guid> ReplaceReservationIds { get; init; } = [];
    public string? DistanceAdjustment { get; init; }
    public string? BudgetAdjustment { get; init; }
    public IReadOnlyList<DayPlanDraftStopDto> DraftDayStops { get; init; } = [];
    public string? AdaptationReason { get; init; }
    public int? DelayMinutes { get; init; }
    public int? ExpectedRevision { get; init; }
    public Guid? TripId { get; init; }
    public string? PlanningMode { get; init; }
}

// Preview context only. The backend resolves catalog data and validates any saved replacement target.
public sealed record DayPlanDraftStopDto(Guid RecommendationId, TimeOnly StartsAt, Guid? ReservationId = null);

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
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public bool IgnorePreferences { get; init; }
    public string? TravelPace { get; init; }
    public IReadOnlyList<string> Interests { get; init; } = [];
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public DateTime? WindowStartsAtLocal { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public DateTime? WindowEndsAtLocal { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? WindowTimeZoneId { get; init; }
    // Search anchor only. The server resolves the current user's next timed plan;
    // this does not claim that the traveler is already at that location.
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public Guid? NearReservationId { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<Guid>? ExcludedRecommendationIds { get; init; }
}

public sealed record DayPersonalizationOptionsDto(
    bool Enabled,
    bool FreeTrialAvailable,
    TravelPreferenceProfileDto Profile);

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
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public bool IsPeriodOnly { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? ProviderPlaceId { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public bool HasLongTransfer { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public bool IsDayPlan { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public Guid? ReplacesRecommendationId { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? PeriodKey { get; init; }
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
    Guid? ClientMutationId = null,
    ItineraryTimePrecision TimePrecision = ItineraryTimePrecision.PeriodOnly,
    Guid? ReplaceReservationId = null,
    Guid? ExpectedRecommendationId = null,
    int? ExpectedRevision = null,
    Guid? ExpectedTripId = null,
    string? PeriodKey = null);

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

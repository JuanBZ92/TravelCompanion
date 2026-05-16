namespace TravelCompanion.Shared.Dtos;

public sealed record TravelChatRequest(
    string Message,
    string? ConversationId,
    string? City,
    DateOnly? Date,
    GeoPointDto? CurrentLocation,
    string? Locale);

public sealed record GeoPointDto(
    decimal Latitude,
    decimal Longitude);

public sealed record TravelChatResponse(
    string ConversationId,
    string Message,
    string Intent,
    IReadOnlyList<TravelCardDto> Cards,
    IReadOnlyList<string> SuggestedReplies,
    MissingContextDto? MissingContext);

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
    string? ReservationId);

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
    TimeOnly? EndsAt);

public sealed record SaveItineraryItemResponse(
    bool Saved,
    string Message,
    ScheduleItemDto? Item);

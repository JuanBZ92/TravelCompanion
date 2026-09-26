using System.ComponentModel.DataAnnotations;

namespace TravelCompanion.Shared.Dtos;

public enum StoreProvider { Apple, Google, AdminPin }
public enum StoreEnvironment { Sandbox, Production }
public enum PurchaseIntentState { Preparing, AwaitingConfirmation, Verifying, Pending, Active, Cancelled, Failed, Refunded }
public enum PaywallEntryPoint { Map, Today, Assistant, Routes, ExplicitUpgrade, Offline }
public enum DayPlanningGoal { Balance, ReduceWalking, Reorganize }
public enum ItineraryChangeKind { Add, Move, Replace, Remove }
public enum RouteOrigin { Yuku, Personal }
public enum RoutePublicationStatus { Draft, Published, Archived }
public enum RouteAccessLevel { Free, Premium }
public enum RouteTheme { Food, HistoryAndTemples, ArtAndDesign, NatureAndGardens, NeighborhoodsAndShopping }

public sealed record CreatePurchaseIntentDto(
    Guid TripId,
    StoreProvider Provider,
    [param: Required, MaxLength(120)] string ProductId,
    PaywallEntryPoint EntryPoint,
    [param: Required, MaxLength(40)] string PaywallVariant,
    [param: MaxLength(16)] string? Locale = null,
    [param: MaxLength(32)] string? AppVersion = null,
    [param: MaxLength(24)] string? Platform = null);

public sealed record PurchaseIntentDto(
    Guid Id,
    Guid TripId,
    StoreProvider Provider,
    string ProductId,
    string OpaqueAccountId,
    PurchaseIntentState State,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? ExpiresAtUtc,
    string? ErrorCode = null);

public sealed record VerifyPurchaseDto(
    Guid IntentId,
    [param: Required, MaxLength(12000)] string Evidence,
    StoreEnvironment Environment,
    [param: Required, MaxLength(80)] string IdempotencyKey);

public sealed record PassAccessDto(
    Guid TripId,
    TrialAccessState State,
    StoreProvider? Provider,
    DateTimeOffset? PurchasedAtUtc,
    DateTimeOffset? ExpiresAtUtc,
    DateTimeOffset? MaximumExpiresAtUtc,
    int AssistantRequestsRemaining);

public sealed record PaywallOfferDto(
    Guid TripId,
    string ProductId,
    string? LocalizedPrice,
    string ReferencePrice,
    bool CanPurchase,
    string Variant,
    PaywallEntryPoint EntryPoint,
    IReadOnlyList<DateOnly> PlannedDays,
    int ItemCount,
    int SavedRouteCount,
    int AdditionalRecommendationCount,
    DateTimeOffset? DraftDeletionAtUtc,
    DateTimeOffset? PassExpiresAtUtc,
    int DailyAssistantLimit,
    IReadOnlyList<string> Benefits,
    DateTimeOffset? AssistantQuotaResetsAtUtc = null);

public sealed record ProductAnalyticsEventDto(
    Guid EventId,
    [param: Required, MaxLength(80)] string Name,
    DateTimeOffset OccurredAtUtc,
    [param: MaxLength(40)] string? Source,
    [param: MaxLength(32)] string? AppVersion,
    [param: MaxLength(24)] string? Platform,
    [param: MaxLength(40)] string? PaywallVariant,
    Guid? TripId,
    bool BehaviorConsent,
    int SchemaVersion = 1);

public sealed record ProductAnalyticsBatchDto(IReadOnlyList<ProductAnalyticsEventDto> Events);

public sealed record DayProposalRequestDto(
    DateOnly Date,
    DayPlanningGoal Goal,
    int ExpectedRevision,
    TimeOnly? WindowStart = null,
    TimeOnly? WindowEnd = null,
    [param: Required, MaxLength(80)] string IdempotencyKey = "",
    Guid? TargetItemId = null,
    [param: MaxLength(40)] string? IssueKind = null);

public sealed record ItineraryChangeDto(
    Guid ChangeId,
    ItineraryChangeKind Kind,
    Guid? ExistingItemId,
    Guid? RecommendationId,
    string Title,
    DateOnly Date,
    TimeOnly StartsAt,
    TimeOnly? EndsAt,
    bool IsProtected,
    string Explanation,
    int SortOrder,
    DateOnly? EndsOn = null);

public sealed record DayProposalDto(
    Guid Id,
    Guid TripId,
    DateOnly Date,
    DayPlanningGoal Goal,
    int BasedOnRevision,
    int Version,
    DateTimeOffset ExpiresAtUtc,
    IReadOnlyList<ItineraryChangeDto> Changes,
    IReadOnlyList<string> Warnings,
    bool CanApply,
    string? Narrative = null,
    TimeOnly? WindowStart = null,
    TimeOnly? WindowEnd = null,
    bool WindowEndsNextDay = false);

public sealed record ReviseDayProposalDto(
    int Version,
    IReadOnlyList<Guid> ExcludedChangeIds,
    IReadOnlyList<Guid> OrderedChangeIds,
    Guid? ReplaceChangeId = null);

public sealed record ApplyDayProposalDto(
    int Version,
    int ExpectedRevision,
    [param: Required, MaxLength(80)] string IdempotencyKey);

public sealed record ItineraryChangeSetDto(
    Guid OperationId,
    Guid TripId,
    int Revision,
    IReadOnlyList<ScheduleItemDto> Items,
    DateTimeOffset UndoAvailableUntilUtc);

public sealed record RouteStopDto(
    Guid Id,
    Guid RecommendationId,
    string Title,
    int DurationMinutes,
    int SortOrder,
    int? EstimatedTransferMinutes,
    Guid? ItineraryItemId = null);

public sealed record ThematicRouteDto(
    Guid Id,
    string Name,
    RouteTheme Theme,
    string City,
    RouteOrigin Origin,
    int Version,
    int VisitMinutes,
    int EstimatedTransferMinutes,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<RouteStopDto> Stops,
    RoutePublicationStatus Status = RoutePublicationStatus.Published,
    DateOnly? PlannedDate = null,
    TimeOnly? WindowStart = null,
    TimeOnly? WindowEnd = null,
    string Pace = "balanced",
    RouteAccessLevel AccessLevel = RouteAccessLevel.Premium,
    Guid? TemplateSourceId = null,
    int? TemplateVersion = null,
    IReadOnlyList<RouteApplicationDto>? Applications = null);

public sealed record RouteApplicationDto(Guid Id, DateOnly Date, int Version,
    IReadOnlyDictionary<Guid, Guid> ItineraryItemByStopId);

public sealed record CreateThematicRouteDto(
    [param: Required, MaxLength(140)] string Name,
    RouteTheme Theme,
    [param: Required, MaxLength(120)] string City,
    DateOnly Date,
    TimeOnly WindowStart,
    TimeOnly WindowEnd,
    [param: MaxLength(24)] string Pace = "balanced");

public sealed record UpdateThematicRouteDto(
    [param: Required, MaxLength(140)] string Name,
    int ExpectedVersion,
    IReadOnlyList<Guid> OrderedRecommendationIds);

public sealed record ApplyThematicRouteDto(
    DateOnly Date,
    TimeOnly WindowStart,
    TimeOnly WindowEnd,
    int ExpectedRevision,
    [param: Required, MaxLength(80)] string IdempotencyKey);

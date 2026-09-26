using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Api.Models;

public sealed class EmailVerificationChallenge
{
    public Guid Id { get; set; }
    public required string Email { get; set; }
    public required string CodeHash { get; set; }
    public required string RequestIpHash { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset ExpiresAtUtc { get; set; }
    public DateTimeOffset ResendAvailableAtUtc { get; set; }
    public int FailedAttempts { get; set; }
    public DateTimeOffset? ConsumedAtUtc { get; set; }
}

public sealed class StorePurchaseIntent
{
    public Guid Id { get; set; }
    public Guid AppUserId { get; set; }
    public AppUser? AppUser { get; set; }
    public Guid TripId { get; set; }
    public Trip? Trip { get; set; }
    public StoreProvider Provider { get; set; }
    public required string ProductId { get; set; }
    public required string OpaqueAccountId { get; set; }
    public PurchaseIntentState State { get; set; }
    public PaywallEntryPoint EntryPoint { get; set; }
    public required string PaywallVariant { get; set; }
    public string? AppVersion { get; set; }
    public string? Platform { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset? ExpiresAtUtc { get; set; }
    public DateTimeOffset? LastAttemptAtUtc { get; set; }
    public int RetryCount { get; set; }
    public string? ErrorCode { get; set; }
    public StoreEnvironment? Environment { get; set; }
    public string? ProtectedEvidence { get; set; }
    public string? DraftSnapshotJson { get; set; }
    public List<StorePurchaseTransaction> Transactions { get; set; } = [];
}

public sealed class StorePurchaseTransaction
{
    public Guid Id { get; set; }
    public Guid PurchaseIntentId { get; set; }
    public StorePurchaseIntent? PurchaseIntent { get; set; }
    public StoreProvider Provider { get; set; }
    public StoreEnvironment Environment { get; set; }
    public required string ProviderTransactionId { get; set; }
    public string? ProviderOriginalTransactionId { get; set; }
    public required string ProductId { get; set; }
    public string? Currency { get; set; }
    public decimal? GrossAmount { get; set; }
    public DateTimeOffset PurchasedAtUtc { get; set; }
    public DateTimeOffset VerifiedAtUtc { get; set; }
    public DateTimeOffset? RevokedAtUtc { get; set; }
    public string? RevocationReason { get; set; }
    public bool AcknowledgedOrConsumed { get; set; }
    public string? EvidenceFingerprint { get; set; }
    public string? ProtectedProviderToken { get; set; }
}

public sealed class StoreNotificationReceipt
{
    public Guid Id { get; set; }
    public StoreProvider Provider { get; set; }
    public StoreEnvironment Environment { get; set; }
    public required string ProviderNotificationId { get; set; }
    public DateTimeOffset ReceivedAtUtc { get; set; }
    public DateTimeOffset? ProcessedAtUtc { get; set; }
    public int RetryCount { get; set; }
    public string? LastError { get; set; }
    public required string PayloadHash { get; set; }
    public string? ProtectedPayload { get; set; }
}

public sealed class StoreRevocationMarker
{
    public Guid Id { get; set; }
    public StoreProvider Provider { get; set; }
    public StoreEnvironment Environment { get; set; }
    public string? ProviderTransactionId { get; set; }
    public string? EvidenceFingerprint { get; set; }
    public required string Reason { get; set; }
    public DateTimeOffset ReceivedAtUtc { get; set; }
    public DateTimeOffset? AppliedAtUtc { get; set; }
}

public sealed class ProductAnalyticsEvent
{
    public Guid Id { get; set; }
    public Guid EventId { get; set; }
    public Guid? AppUserId { get; set; }
    public Guid? AnonymousUserId { get; set; }
    public Guid? TripId { get; set; }
    public required string Name { get; set; }
    public DateTimeOffset OccurredAtUtc { get; set; }
    public DateTimeOffset ReceivedAtUtc { get; set; }
    public string? Source { get; set; }
    public string? AppVersion { get; set; }
    public string? Platform { get; set; }
    public string? AccessState { get; set; }
    public string? FreePolicyVariant { get; set; }
    public string? PaywallVariant { get; set; }
    public bool BehaviorConsent { get; set; }
    public bool IsBusinessEvent { get; set; }
    public int SchemaVersion { get; set; } = 1;
    public bool IsSandbox { get; set; }
}

public sealed class ProductAnalyticsDailyAggregate
{
    public string FreePolicyVariant { get; set; } = string.Empty;
    public Guid Id { get; set; }
    public DateOnly Date { get; set; }
    public required string Name { get; set; }
    public required string Source { get; set; }
    public required string Platform { get; set; }
    public required string AppVersion { get; set; }
    public required string PaywallVariant { get; set; }
    public int EventCount { get; set; }
}

public sealed class ProductExperimentAssignment
{
    public Guid Id { get; set; }
    public Guid AppUserId { get; set; }
    public AppUser? AppUser { get; set; }
    public required string Experiment { get; set; }
    public required string Variant { get; set; }
    public DateTimeOffset AssignedAtUtc { get; set; }
}

public sealed class AssistantDailyUsage
{
    public Guid Id { get; set; }
    public Guid BuilderAccessGrantId { get; set; }
    public BuilderAccessGrant? BuilderAccessGrant { get; set; }
    public DateOnly UtcDate { get; set; }
    public int SuccessfulRequests { get; set; }
    public int ReservedRequests { get; set; }
}

public sealed class AssistantUsageLease
{
    public Guid Id { get; set; }
    public Guid BuilderAccessGrantId { get; set; }
    public BuilderAccessGrant? BuilderAccessGrant { get; set; }
    public required string OperationKey { get; set; }
    public DateOnly UtcDate { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset ExpiresAtUtc { get; set; }
    public DateTimeOffset? CompletedAtUtc { get; set; }
    public DateTimeOffset? CancelledAtUtc { get; set; }
}

public sealed class ItineraryProposal
{
    public Guid Id { get; set; }
    public Guid TripId { get; set; }
    public Trip? Trip { get; set; }
    public Guid AppUserId { get; set; }
    public Guid? SourceRouteId { get; set; }
    public ThematicRoute? SourceRoute { get; set; }
    public DateOnly Date { get; set; }
    public DayPlanningGoal Goal { get; set; }
    public TimeOnly WindowStart { get; set; } = new(9, 0);
    public TimeOnly WindowEnd { get; set; } = new(21, 0);
    public bool WindowEndsNextDay { get; set; }
    public string CurrentContextJson { get; set; } = "[]";
    public string ProtectedItemsJson { get; set; } = "[]";
    public int BasedOnRevision { get; set; }
    public int Version { get; set; } = 1;
    public required string ChangesJson { get; set; }
    public required string WarningsJson { get; set; }
    public string Narrative { get; set; } = string.Empty;
    public required string IdempotencyKey { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset ExpiresAtUtc { get; set; }
    public DateTimeOffset? AppliedAtUtc { get; set; }
}

public sealed class ItineraryOperation
{
    public Guid Id { get; set; }
    public Guid TripId { get; set; }
    public Trip? Trip { get; set; }
    public Guid AppUserId { get; set; }
    public required string IdempotencyKey { get; set; }
    public required string PreviousStateJson { get; set; }
    public int PreviousRevision { get; set; }
    public int AppliedRevision { get; set; }
    public DateTimeOffset AppliedAtUtc { get; set; }
    public DateTimeOffset UndoAvailableUntilUtc { get; set; }
    public DateTimeOffset? UndoneAtUtc { get; set; }
    public Guid? RouteApplicationId { get; set; }
}

public sealed class ThematicRoute
{
    public Guid Id { get; set; }
    public Guid? AppUserId { get; set; }
    public AppUser? AppUser { get; set; }
    public Guid? TripId { get; set; }
    public Trip? Trip { get; set; }
    public Guid DestinationId { get; set; }
    public Destination? Destination { get; set; }
    public required string Name { get; set; }
    public RouteTheme Theme { get; set; }
    public required string City { get; set; }
    public RouteOrigin Origin { get; set; }
    public RoutePublicationStatus Status { get; set; }
    public int Version { get; set; } = 1;
    public RouteAccessLevel AccessLevel { get; set; } = RouteAccessLevel.Premium;
    public Guid? TemplateSourceId { get; set; }
    public int? TemplateVersion { get; set; }
    public DateOnly? PlannedDate { get; set; }
    public TimeOnly? WindowStart { get; set; }
    public TimeOnly? WindowEnd { get; set; }
    public string Pace { get; set; } = "balanced";
    public int VisitMinutes { get; set; }
    public int EstimatedTransferMinutes { get; set; }
    public required string WarningsJson { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public List<ThematicRouteStop> Stops { get; set; } = [];
    public List<ThematicRouteApplication> Applications { get; set; } = [];
}

public sealed class ThematicRouteStop
{
    public Guid Id { get; set; }
    public Guid ThematicRouteId { get; set; }
    public ThematicRoute? ThematicRoute { get; set; }
    public Guid RecommendationId { get; set; }
    public Recommendation? Recommendation { get; set; }
    public int DurationMinutes { get; set; }
    public int SortOrder { get; set; }
    public int? EstimatedTransferMinutes { get; set; }
    public Guid? ItineraryItemId { get; set; }
    public Reservation? ItineraryItem { get; set; }
}

public sealed class ThematicRouteApplication
{
    public Guid Id { get; set; }
    public Guid ThematicRouteId { get; set; }
    public ThematicRoute? ThematicRoute { get; set; }
    public Guid TripId { get; set; }
    public Trip? Trip { get; set; }
    public Guid ItineraryOperationId { get; set; }
    public ItineraryOperation? ItineraryOperation { get; set; }
    public DateOnly Date { get; set; }
    public int Version { get; set; } = 1;
    public DateTimeOffset CreatedAtUtc { get; set; }
    public List<ThematicRouteApplicationStop> Stops { get; set; } = [];
}

public sealed class ThematicRouteApplicationStop
{
    public Guid Id { get; set; }
    public Guid ThematicRouteApplicationId { get; set; }
    public ThematicRouteApplication? ThematicRouteApplication { get; set; }
    public Guid ThematicRouteStopId { get; set; }
    public ThematicRouteStop? ThematicRouteStop { get; set; }
    public Guid ItineraryItemId { get; set; }
    public Reservation? ItineraryItem { get; set; }
}

public sealed class TripSynchronizationWork
{
    public Guid Id { get; set; }
    public Guid TripId { get; set; }
    public Guid AppUserId { get; set; }
    public int Revision { get; set; }
    public required string Kind { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset NextAttemptAtUtc { get; set; }
    public DateTimeOffset? ProcessedAtUtc { get; set; }
    public int AttemptCount { get; set; }
    public string? LastError { get; set; }
}

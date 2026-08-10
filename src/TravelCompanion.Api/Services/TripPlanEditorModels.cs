using TravelCompanion.Shared;

namespace TravelCompanion.Api.Services;

public static class TripPlanPeriods
{
    public static readonly IReadOnlyList<TripPlanPeriodDefinition> All =
    [
        new("morning", "Mañana", 1, new TimeOnly(9, 0), new TimeOnly(11, 59)),
        new("midday", "Medio día", 2, new TimeOnly(12, 0), new TimeOnly(14, 29)),
        new("afternoon", "Tarde", 3, new TimeOnly(14, 30), new TimeOnly(18, 29)),
        new("night", "Noche", 4, new TimeOnly(18, 30), new TimeOnly(23, 59))
    ];

    public static TripPlanPeriodDefinition Resolve(TimeOnly time) =>
        All.First(period => time >= period.StartsAt && time <= period.EndsAt);

    public static TripPlanPeriodDefinition? Find(string key) =>
        All.FirstOrDefault(period => string.Equals(period.Key, key, StringComparison.OrdinalIgnoreCase));
}

public sealed record TripPlanPeriodDefinition(
    string Key,
    string Label,
    int SortOrder,
    TimeOnly StartsAt,
    TimeOnly EndsAt);

public sealed class TripPlanEditorPayload
{
    public string TravelerName { get; set; } = string.Empty;
    public Guid DestinationId { get; set; }
    public DateOnly StartsOn { get; set; }
    public DateOnly EndsOn { get; set; }
    public string TimeZoneId { get; set; } = "UTC";
    public List<TripPlanDayDraft> Days { get; set; } = [];
}

public sealed class TripPlanDayDraft
{
    public Guid Id { get; set; }
    public DateOnly Date { get; set; }
    public int DayNumber { get; set; }
    public string City { get; set; } = string.Empty;
    public string HotelBase { get; set; } = string.Empty;
    public decimal? BaseLatitude { get; set; }
    public decimal? BaseLongitude { get; set; }
    public string Introduction { get; set; } = string.Empty;
    public List<TripPlanBlockDraft> Blocks { get; set; } = [];
}

public sealed class TripPlanBlockDraft
{
    public Guid Id { get; set; }
    public string PeriodKey { get; set; } = string.Empty;
    public string CuratedDescription { get; set; } = string.Empty;
    public bool AutofillEnabled { get; set; } = true;
    public List<TripPlanRecommendationDraft> Recommendations { get; set; } = [];
    public List<TripPlanItemDraft> Items { get; set; } = [];
}

public sealed class TripPlanRecommendationDraft
{
    public Guid Id { get; set; }
    public Guid RecommendationId { get; set; }
}

public sealed class TripPlanItemDraft
{
    public Guid Id { get; set; }
    public ReservationType Type { get; set; } = ReservationType.Event;
    public ScheduleItemKind PlanningKind { get; set; } = ScheduleItemKind.ManualEvent;
    public TimeOnly StartsAt { get; set; }
    public DateOnly? EndsOn { get; set; }
    public TimeOnly? EndsAt { get; set; }
    public string Title { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string LocationName { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string ConfirmationCode { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;
    public decimal? Latitude { get; set; }
    public decimal? Longitude { get; set; }
    public string? Airline { get; set; }
    public string? FlightNumber { get; set; }
    public string? OriginName { get; set; }
    public string? DestinationName { get; set; }
    public string? OriginAirport { get; set; }
    public string? DestinationAirport { get; set; }
}

public sealed record TripPlanEditorState(
    Guid TripId,
    int BasePlanRevision,
    bool HasDraft,
    bool HasPendingPin,
    string PublicationStatus,
    DateTimeOffset? DraftUpdatedAtUtc,
    TripPlanEditorPayload Payload,
    IReadOnlyList<TripPlanRecommendationCatalogItem> Recommendations);

public sealed record TripPlanRecommendationCatalogItem(
    Guid Id,
    string Title,
    string Category,
    string Neighborhood,
    string CitySlug,
    string Description,
    IReadOnlyList<string> Tags,
    string PriceLevel,
    int SuggestedDurationMinutes,
    double? Rating,
    decimal Latitude,
    decimal Longitude);

public sealed record TripPlanListItem(
    Guid Id,
    string TravelerName,
    string DestinationName,
    DateOnly StartsOn,
    DateOnly EndsOn,
    string PublicationStatus,
    bool HasDraft,
    DateTimeOffset UpdatedAtUtc);

public sealed record CreateTripPlanCommand(
    string TravelerName,
    string AccessPin,
    Guid DestinationId,
    DateOnly StartsOn,
    DateOnly EndsOn,
    string TimeZoneId);

public sealed record TripPlanOperationResult(bool Success, string Message, int? CurrentRevision = null)
{
    public static TripPlanOperationResult Ok(string message, int? revision = null) => new(true, message, revision);
    public static TripPlanOperationResult Fail(string message, int? revision = null) => new(false, message, revision);
}

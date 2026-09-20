namespace TravelCompanion.Shared.Dtos;

public static class DayReviewStatuses
{
    public const string Balanced = "balanced";
    public const string Tight = "tight";
    public const string NeedsAttention = "needs_attention";
}

public static class DayReviewIssueKinds
{
    public const string Overlap = "overlap";
    public const string TightTransfer = "tight_transfer";
    public const string PackedDay = "packed_day";
    public const string IncompleteInformation = "incomplete_information";
}

public static class DayReviewSeverities
{
    public const string Info = "info";
    public const string Warning = "warning";
    public const string Critical = "critical";
}

public sealed record DayReviewDto(
    DateOnly Date,
    string Status,
    string Title,
    string Summary,
    IReadOnlyList<DayReviewIssueDto> Issues);

public sealed record DayReviewIssueDto(
    string Kind,
    string Severity,
    string Title,
    string Message,
    IReadOnlyList<Guid> ItemIds,
    int? AvailableMinutes = null,
    int? RecommendedMinutes = null);

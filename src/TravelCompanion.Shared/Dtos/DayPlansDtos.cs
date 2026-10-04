namespace TravelCompanion.Shared.Dtos;

public sealed record DayPlanPreferencesDto(string TravelPace, string Budget, IReadOnlyList<string> Interests);

public sealed record DayPlanRequest(Guid TripId, int ExpectedRevision, DateOnly StartDate, int DayCount,
    Guid OperationId, DayPlanPreferencesDto? Preferences = null, string? Locale = null);

public sealed record DayPlanOptionsDto(bool Enabled, Guid? TripId, int Revision, DateOnly? StartsOn,
    DateOnly? EndsOn, IReadOnlyList<int> DayCounts, TravelPreferenceProfileDto Profile,
    TrialAccessStatusDto? TrialAccess = null);

public sealed record DayPlanStopDto(Guid Id, Guid RecommendationId, string PeriodKey, TravelCardDto Card);

public sealed record DayPlanDayDto(DateOnly Date, IReadOnlyList<string> Cities,
    IReadOnlyList<DayPlanStopDto> Stops, IReadOnlyList<string> MissingMoments);

public sealed record DayPlanResponse(Guid OperationId, Guid TripId, int BasedOnRevision,
    IReadOnlyList<DayPlanDayDto> Days, string Message, TrialAccessStatusDto? TrialAccess = null);

public sealed record DayPlanApplyRequest(Guid OperationId, Guid TripId, int ExpectedRevision,
    Guid MutationId, IReadOnlyList<Guid> SelectedStopIds, string? Locale = null);

public sealed record DayPlanApplyResponse(bool Applied, string Message, Guid TripId, int Revision,
    IReadOnlyList<ScheduleItemDto> Items, TrialAccessStatusDto? TrialAccess = null);

public sealed record DayPlanErrorDto(string Code, string Message);

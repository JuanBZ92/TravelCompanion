namespace TravelCompanion.Shared;

public static class FreePlanningPolicy
{
    public const int MaximumDays = 3;
    public const int MaximumDayImprovements = 3;
    public static bool CanPlanDate(DateOnly startsOn, DateOnly date) =>
        date.DayNumber - startsOn.DayNumber is >= 0 and < MaximumDays;
}

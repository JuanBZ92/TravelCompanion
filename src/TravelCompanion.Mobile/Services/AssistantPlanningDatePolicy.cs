namespace TravelCompanion.Mobile.Services;

public static class AssistantPlanningDatePolicy
{
    public static bool ShouldPreserveSelection(DateOnly selectedDate, DateOnly firstAllowedDate,
        DateOnly lastAllowedDate, bool selectedByTraveler, bool isFullDayFlow) =>
        (selectedByTraveler || isFullDayFlow)
        && selectedDate >= firstAllowedDate && selectedDate <= lastAllowedDate;
}

using TravelCompanion.Api.Models;
using TravelCompanion.Shared;

namespace TravelCompanion.Api.Services;

internal static class ItineraryPlanningPolicy
{
    // SourceName is persisted by the assistant save endpoint, including older app versions.
    public static bool CanReplace(Reservation item) => item.Type == ReservationType.Event
        && item.Owner == ItineraryItemOwner.Traveler
        && item.SourceName == "Travel Assistant"
        && item.Flexibility == ItineraryFlexibility.Flexible
        && item.PlanningKind != ScheduleItemKind.ConfirmedReservation;
}

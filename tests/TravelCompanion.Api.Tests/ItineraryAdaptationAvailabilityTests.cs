using TravelCompanion.Api.Models;
using TravelCompanion.Api.Services;
using TravelCompanion.Shared;

namespace TravelCompanion.Api.Tests;

public sealed class ItineraryAdaptationAvailabilityTests
{
    [Fact]
    public void Schedule_item_adaptation_matches_server_replacement_policy()
    {
        var item = new Reservation
        {
            Title = "Flexible stop",
            City = "Tokyo",
            LocationName = "Tokyo",
            Address = "",
            ConfirmationCode = "",
            Notes = "",
            Type = ReservationType.Event,
            Owner = ItineraryItemOwner.Traveler,
            SourceName = "Travel Assistant",
            Flexibility = ItineraryFlexibility.Flexible,
            PlanningKind = ScheduleItemKind.ManualEvent
        };

        Assert.True(TravelerItineraryService.ToDto(item).CanAdapt);

        item.SourceName = "Manual";
        Assert.False(TravelerItineraryService.ToDto(item).CanAdapt);

        item.SourceName = "Travel Assistant";
        item.PlanningKind = ScheduleItemKind.ConfirmedReservation;
        Assert.False(TravelerItineraryService.ToDto(item).CanAdapt);

        item.PlanningKind = ScheduleItemKind.ManualEvent;
        item.Flexibility = ItineraryFlexibility.FixedByTraveler;
        Assert.False(TravelerItineraryService.ToDto(item).CanAdapt);

        item.Flexibility = ItineraryFlexibility.Flexible;
        item.Type = ReservationType.Lodging;
        Assert.False(TravelerItineraryService.ToDto(item).CanAdapt);
    }
}

using TravelCompanion.Mobile.ViewModels;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Mobile.Tests;

public sealed class ItineraryRouteViewModelTests
{
    [Fact]
    public void Reservation_hides_place_when_it_repeats_the_title()
    {
        var item = new ScheduleItemDto(
            Guid.NewGuid(),
            Guid.NewGuid(),
            TravelCompanion.Shared.ReservationType.Event,
            new DateOnly(2026, 10, 2),
            new TimeOnly(15, 0),
            null,
            null,
            "Tonkatsu Suzuki",
            "Tokyo",
            "Tonkatsu Suzuki",
            "Ginza",
            string.Empty,
            string.Empty,
            null,
            null,
            null,
            null,
            null,
            null);

        var reservation = new TodayReservationViewModel(item);

        Assert.False(reservation.HasPlace);
    }

    [Fact]
    public void Shows_departure_origin_and_partial_failure_without_old_times()
    {
        var route = new ItineraryRouteViewModel("WALK", "A pie", "route_walk.svg");
        route.Apply(new("WALK", "Available", "Hotel", 12, DepartureLabel: "19:38", OriginKind: "Hotel"));
        Assert.Equal("12 min (H)", route.Duration);
        Assert.Equal("Salir 19:38", route.Departure);
        Assert.Equal("Desde Hotel", route.Origin);
        route.Apply(new("WALK", "Available", "Tu ubicacion", 7, DepartureLabel: "19:43", OriginKind: "CurrentLocation"));
        Assert.Equal("7 min (U)", route.Duration);
        Assert.Equal("Desde Tu ubicacion", route.Origin);
        route.Apply(null);
        Assert.Equal("No disponible", route.Duration);
        Assert.Empty(route.Departure);
    }
}

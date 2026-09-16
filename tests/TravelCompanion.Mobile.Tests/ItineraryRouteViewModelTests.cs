using TravelCompanion.Mobile.ViewModels;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Mobile.Tests;

public sealed class ItineraryRouteViewModelTests
{
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

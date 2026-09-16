using TravelCompanion.Mobile.ViewModels;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Mobile.Tests;

public sealed class ItineraryRouteViewModelTests
{
    [Fact]
    public void Shows_departure_origin_and_partial_failure_without_old_times()
    {
        var route = new ItineraryRouteViewModel("WALK", "A pie", "route_walk.svg");
        route.Apply(new("WALK", "Available", "Hotel", 12, DepartureLabel: "19:38"));
        Assert.Equal("12 min", route.Duration);
        Assert.Equal("Salir 19:38", route.Departure);
        Assert.Equal("Desde Hotel", route.Origin);
        route.Apply(null);
        Assert.Equal("No disponible", route.Duration);
        Assert.Empty(route.Departure);
    }
}

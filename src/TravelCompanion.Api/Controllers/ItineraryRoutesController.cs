using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using TravelCompanion.Api.Data;
using TravelCompanion.Api.Services;
using TravelCompanion.Shared;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Api.Controllers;

[ApiController]
[Route("api/mobile/itinerary/{id:guid}/route")]
[EnableRateLimiting("ItineraryRoutes")]
public sealed class ItineraryRoutesController(TravelCompanionDbContext db, TravelerAccessService accessService,
    IGoogleRoutesService routes, IGooglePlacesService places) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<ItineraryRouteDto>> Estimate(Guid id, ItineraryRouteRequest request, CancellationToken ct)
    {
        var access = await accessService.GetAsync(HttpContext, ct);
        if (access is null || !access.Capabilities.CanEditItinerary || access.TripId is null) return Forbid();
        if (request.Mode is not ("DRIVE" or "TRANSIT" or "WALK") ||
            request.Latitude is < -90 or > 90 || request.Longitude is < -180 or > 180 ||
            request.Latitude.HasValue != request.Longitude.HasValue) return BadRequest();
        var item = await db.Reservations.AsNoTracking().Include(r => r.Trip)
            .SingleOrDefaultAsync(r => r.Id == id && r.TripId == access.TripId && r.Trip!.AppUserId == access.User.Id, ct);
        if (item is null) return NotFound();
        if (item.Owner != ItineraryItemOwner.Traveler || item.TimePrecision != ItineraryTimePrecision.Exact) return BadRequest();
        TimeZoneInfo zone;
        try { zone = TimeZoneInfo.FindSystemTimeZoneById(item.TimeZoneId ?? item.Trip!.TimeZoneId); }
        catch (TimeZoneNotFoundException) { return Ok(new ItineraryRouteDto(request.Mode, "Unavailable", "")); }
        var local = item.Date.ToDateTime(item.StartsAt, DateTimeKind.Unspecified);
        var start = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(local, zone));
        var target = start.AddMinutes(-10);
        var now = DateTimeOffset.UtcNow;
        if (start <= now) return Ok(new ItineraryRouteDto(request.Mode, "Past", ""));
        if (request.Mode == "TRANSIT" && target > now.AddDays(100)) return Ok(new ItineraryRouteDto(request.Mode, "TooEarly", ""));
        var destination = new RouteWaypoint(item.ProviderPlaceId, item.Latitude, item.Longitude);
        var isToday = item.Date == DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, zone).DateTime);
        if (isToday && request.Latitude.HasValue && destination.Latitude is null && destination.PlaceId is not null)
        {
            var detail = await places.DetailsAsync(item.Trip!.DestinationId, new(destination.PlaceId, Guid.NewGuid().ToString()), ct);
            if (detail is not null) destination = destination with { Latitude = detail.Latitude, Longitude = detail.Longitude };
        }
        RouteWaypoint? origin = null;
        var label = "Hotel/base";
        var originKind = "Hotel";
        if (isToday && request.Latitude.HasValue && destination.Latitude.HasValue && destination.Longitude.HasValue &&
            FreeMapPreviewService.CalculateDistanceKm(request.Latitude.Value, request.Longitude!.Value, destination.Latitude.Value, destination.Longitude.Value) <= 50)
        {
            origin = new(null, request.Latitude, request.Longitude);
            label = "Tu ubicacion";
            originKind = "CurrentLocation";
        }
        else
        {
            var day = await db.TripDayPlans.AsNoTracking().SingleOrDefaultAsync(d => d.TripId == item.TripId && d.Date == item.Date, ct);
            if (day is not null)
            {
                origin = new(day.BaseProviderPlaceId, day.BaseLatitude, day.BaseLongitude);
                label = string.IsNullOrWhiteSpace(day.HotelBase) ? label : day.HotelBase;
            }
        }
        if (origin is null || !origin.IsValid || !destination.IsValid)
            return Ok(new ItineraryRouteDto(request.Mode, "NoLocation", label, OriginKind: originKind));
        var estimate = await routes.EstimateAsync(origin, destination, request.Mode, target, ct);
        if (estimate is null)
            return Ok(new ItineraryRouteDto(request.Mode, "Unavailable", label, OriginKind: originKind));
        var leave = TimeZoneInfo.ConvertTime(estimate.LeaveAt, zone);
        return Ok(new ItineraryRouteDto(request.Mode, "Available", label, estimate.Minutes, leave,
            leave <= now, leave.ToString(leave.Date == local.Date ? "HH:mm" : "dd/MM HH:mm"), originKind));
    }
}

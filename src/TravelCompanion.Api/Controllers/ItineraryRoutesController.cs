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
    IGoogleRoutesService routes) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<ItineraryRouteDto>> Estimate(Guid id, ItineraryRouteRequest request, CancellationToken ct)
    {
        var access = await accessService.GetAsync(HttpContext, ct);
        if (access is null || !access.Capabilities.CanCalculateRoutes || access.TripId is null) return Forbid();
        if (request.Mode is not ("DRIVE" or "TRANSIT" or "WALK") ||
            request.Latitude is < -90 or > 90 || request.Longitude is < -180 or > 180 ||
            request.Latitude.HasValue != request.Longitude.HasValue || request.ArrivalMarginMinutes is < 0 or > 120 ||
            request.OriginKind is not (null or "CurrentLocation" or "Hotel" or "PreviousActivity") ||
            request.OriginKind == "CurrentLocation" && !request.Latitude.HasValue ||
            (request.OriginKind is "Hotel" or "PreviousActivity") && request.Latitude.HasValue ||
            (request.OriginKind == "PreviousActivity") != request.PreviousActivityId.HasValue) return BadRequest();
        var item = await db.Reservations.AsNoTracking().Include(r => r.Trip).Include(r => r.Recommendation)
            .SingleOrDefaultAsync(r => r.Id == id && r.TripId == access.TripId && r.Trip!.AppUserId == access.User.Id, ct);
        if (item is null) return NotFound();
        if (!ItineraryRoutePolicy.TryBounds(TravelerItineraryService.ToDto(item), item.Trip!.TimeZoneId, out var start, out _)) return BadRequest();
        TimeZoneInfo zone;
        try { zone = TimeZoneInfo.FindSystemTimeZoneById(item.TimeZoneId ?? item.Trip!.TimeZoneId); }
        catch (TimeZoneNotFoundException) { return Ok(new ItineraryRouteDto(request.Mode, "Unavailable", "")); }
        catch (InvalidTimeZoneException) { return Ok(new ItineraryRouteDto(request.Mode, "Unavailable", "")); }
        var local = item.Date.ToDateTime(item.StartsAt, DateTimeKind.Unspecified);
        var target = start.AddMinutes(-request.ArrivalMarginMinutes);
        var now = DateTimeOffset.UtcNow;
        if (start <= now) return Ok(new ItineraryRouteDto(request.Mode, "Past", ""));
        if (request.Mode == "TRANSIT" && target > now.AddDays(100)) return Ok(new ItineraryRouteDto(request.Mode, "TooEarly", ""));
        var destinationItem = TravelerItineraryService.ToDto(item);
        var destination = new RouteWaypoint(destinationItem.ProviderPlaceId, destinationItem.Latitude, destinationItem.Longitude);
        RouteWaypoint? origin = null;
        DateTimeOffset? previousEnd = null;
        var label = "Hotel/base";
        var originKind = "Hotel";
        if (request.Latitude.HasValue && request.Longitude.HasValue)
        {
            origin = new(null, request.Latitude, request.Longitude);
            label = "Tu ubicacion";
            originKind = "CurrentLocation";
        }
        else if (request.OriginKind == "PreviousActivity")
        {
            // Restrict candidates to the authenticated trip before resolving the previous commitment.
            var candidates = await db.Reservations.AsNoTracking().Include(r => r.Recommendation)
                // A 24 h UTC window can span -3/+2 calendar dates with offsets -12 through +14.
                .Where(r => r.TripId == item.TripId && r.Id != id && r.Date >= item.Date.AddDays(-3) && r.Date <= item.Date.AddDays(2))
                .ToListAsync(ct);
            var previous = ItineraryRoutePolicy.Previous(candidates.Select(TravelerItineraryService.ToDto), destinationItem, item.Trip!.TimeZoneId);
            if (previous is null || previous.Id != request.PreviousActivityId) return BadRequest();
            origin = new(previous.ProviderPlaceId, previous.Latitude, previous.Longitude);
            label = previous.Title;
            originKind = "PreviousActivity";
            ItineraryRoutePolicy.TryBounds(previous, item.Trip.TimeZoneId, out _, out var end);
            previousEnd = end;
        }
        else
        {
            var day = await db.TripDayPlans.AsNoTracking().SingleOrDefaultAsync(d => d.TripId == item.TripId && d.Date == item.Date, ct);
            if (day is not null)
            {
                origin = new(day.BaseProviderPlaceId, day.BaseLatitude, day.BaseLongitude);
                label = string.IsNullOrWhiteSpace(day.HotelBase) ? label : day.HotelBase;
            }
            if (origin is null || !origin.IsValid)
            {
                var stay = await db.Reservations.AsNoTracking()
                    .Where(r => r.TripId == item.TripId && r.Type == ReservationType.Lodging && r.Date <= item.Date
                        && (r.EndsOn == null ? r.Date == item.Date : r.EndsOn >= item.Date))
                    .OrderByDescending(r => r.Date).ThenBy(r => r.Id).FirstOrDefaultAsync(ct);
                if (stay is not null)
                {
                    origin = new(stay.ProviderPlaceId, stay.Latitude, stay.Longitude);
                    label = string.IsNullOrWhiteSpace(stay.LocationName) ? stay.Title : stay.LocationName;
                }
            }
        }
        if (origin is null || !origin.IsValid || !destination.IsValid)
            return Ok(new ItineraryRouteDto(request.Mode, "NoLocation", label, OriginKind: originKind));
        var estimate = await routes.EstimateAsync(origin, destination, request.Mode, target, ct);
        if (estimate is null)
            return Ok(new ItineraryRouteDto(request.Mode, "Unavailable", label, OriginKind: originKind));
        var leave = TimeZoneInfo.ConvertTime(estimate.LeaveAt, zone);
        return Ok(new ItineraryRouteDto(request.Mode, "Available", label, estimate.Minutes, leave,
            leave <= now, leave.ToString(leave.Date == local.Date ? "HH:mm" : "dd/MM HH:mm"), originKind)
        {
            ArrivalMarginMinutes = request.ArrivalMarginMinutes,
            PreviousActivityOverlapsDeparture = previousEnd > estimate.LeaveAt
        });
    }
}

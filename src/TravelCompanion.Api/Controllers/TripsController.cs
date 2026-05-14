using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TravelCompanion.Api.Data;
using TravelCompanion.Api.Services;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class TripsController(
    TravelCompanionDbContext dbContext,
    UserSessionService sessionService) : ControllerBase
{
    [HttpGet("{id:guid}/schedule")]
    public async Task<ActionResult<TripScheduleDto>> GetSchedule(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var isAdmin = User.Identity?.IsAuthenticated == true && User.IsInRole("Admin");
        Guid? requesterUserId = null;
        if (!isAdmin)
        {
            var sessionUser = await sessionService.GetUserAsync(HttpContext, cancellationToken);
            if (sessionUser is null)
            {
                return Unauthorized();
            }

            requesterUserId = sessionUser.Id;
        }

        var trip = await dbContext.Trips
            .AsNoTracking()
            .Include(existingTrip => existingTrip.Destination)
            .Include(existingTrip => existingTrip.Reservations)
            .SingleOrDefaultAsync(existingTrip => existingTrip.Id == id, cancellationToken);

        if (trip is null || trip.Destination is null)
        {
            return NotFound();
        }

        if (!isAdmin && trip.AppUserId != requesterUserId)
        {
            return Forbid();
        }

        var response = new TripScheduleDto(
            trip.Id,
            trip.TravelerName,
            trip.Destination.Name,
            trip.StartsOn,
            trip.EndsOn,
            trip.Reservations
                .OrderBy(reservation => reservation.Date)
                .ThenBy(reservation => reservation.StartsAt)
                .Select(reservation => new ScheduleItemDto(
                    reservation.Id,
                    reservation.Type,
                    reservation.Date,
                    reservation.StartsAt,
                    reservation.EndsOn,
                    reservation.EndsAt,
                    reservation.Title,
                    reservation.City,
                    reservation.LocationName,
                    reservation.Address,
                    reservation.ConfirmationCode,
                    reservation.Notes,
                    reservation.Airline,
                    reservation.FlightNumber,
                    reservation.OriginName,
                    reservation.DestinationName,
                    reservation.OriginAirport,
                    reservation.DestinationAirport))
                .ToList());

        return Ok(response);
    }
}

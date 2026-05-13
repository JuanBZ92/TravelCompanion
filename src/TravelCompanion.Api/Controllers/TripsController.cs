using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TravelCompanion.Api.Data;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class TripsController(TravelCompanionDbContext dbContext) : ControllerBase
{
    [HttpGet("{id:guid}/schedule")]
    public async Task<ActionResult<TripScheduleDto>> GetSchedule(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var trip = await dbContext.Trips
            .AsNoTracking()
            .Include(existingTrip => existingTrip.Destination)
            .Include(existingTrip => existingTrip.Reservations)
            .SingleOrDefaultAsync(existingTrip => existingTrip.Id == id, cancellationToken);

        if (trip is null || trip.Destination is null)
        {
            return NotFound();
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
                    reservation.Date,
                    reservation.StartsAt,
                    reservation.Title,
                    reservation.City,
                    reservation.LocationName,
                    reservation.Address,
                    reservation.ConfirmationCode,
                    reservation.Notes))
                .ToList());

        return Ok(response);
    }
}

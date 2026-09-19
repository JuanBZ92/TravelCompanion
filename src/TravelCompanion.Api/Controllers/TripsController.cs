using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TravelCompanion.Api.Data;
using TravelCompanion.Api.Models;
using TravelCompanion.Api.Services;
using TravelCompanion.Shared;
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
            .SingleOrDefaultAsync(existingTrip =>
                existingTrip.Id == id
                && existingTrip.PublicationStatus == TripPublicationStatus.Published,
                cancellationToken);

        if (trip is null || trip.Destination is null)
        {
            return NotFound();
        }

        if (!isAdmin && trip.AppUserId != requesterUserId)
        {
            return Forbid();
        }

        var items = trip.Reservations
            .OrderBy(reservation => reservation.Date)
            .ThenBy(reservation => reservation.StartsAt)
            .Select(TravelerItineraryService.ToDto)
            .ToList();
        var response = new TripScheduleDto(
            trip.Id,
            trip.TravelerName,
            trip.Destination.Name,
            trip.StartsOn,
            trip.EndsOn,
            items,
            trip.PlanRevision,
            ScheduleReviewAnalyzer.Analyze(items, trip.StartsOn, trip.EndsOn));

        return Ok(response);
    }
}

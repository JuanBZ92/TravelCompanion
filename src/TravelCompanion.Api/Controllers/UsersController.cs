using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TravelCompanion.Api.Data;
using TravelCompanion.Api.Models;
using TravelCompanion.Api.Services;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class UsersController(
    TravelCompanionDbContext dbContext,
    UserSessionService sessionService,
    IUserProfileService userProfileService) : ControllerBase
{
    private const string DemoUserEmail = "demo@travelcompanion.local";

    [HttpGet("demo/entitlements")]
    public async Task<ActionResult<UserEntitlementsDto>> GetDemoEntitlements(
        CancellationToken cancellationToken = default)
    {
        if (!IsAdminRequest())
        {
            return Unauthorized();
        }

        return await GetEntitlementsByEmailAsync(DemoUserEmail, cancellationToken);
    }

    [HttpGet("{userId:guid}/entitlements")]
    public async Task<ActionResult<UserEntitlementsDto>> GetEntitlements(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var accessError = await EnsureCanAccessUserAsync(userId, cancellationToken);
        if (accessError is not null)
        {
            return accessError;
        }

        var user = await dbContext.AppUsers
            .AsNoTracking()
            .Include(user => user.Entitlements)
            .FirstOrDefaultAsync(user => user.Id == userId, cancellationToken);

        return user is null
            ? NotFound()
            : Ok(ToDto(user));
    }

    [HttpGet("{userId:guid}/schedule")]
    public async Task<ActionResult<TripScheduleDto>> GetSchedule(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var accessError = await EnsureCanAccessUserAsync(userId, cancellationToken);
        if (accessError is not null)
        {
            return accessError;
        }

        var schedule = await FindScheduleAsync(userId, cancellationToken: cancellationToken);
        return schedule is null
            ? NotFound()
            : Ok(schedule);
    }

    [HttpGet("~/api/me/entitlements")]
    public async Task<ActionResult<UserEntitlementsDto>> GetMyEntitlements(CancellationToken cancellationToken)
    {
        var user = await sessionService.GetUserAsync(HttpContext, cancellationToken);
        return user is null
            ? Unauthorized()
            : Ok(ToDto(user));
    }

    [HttpGet("~/api/me/schedule")]
    public async Task<ActionResult<TripScheduleDto>> GetMySchedule(CancellationToken cancellationToken)
    {
        var user = await sessionService.GetUserAsync(HttpContext, cancellationToken);
        if (user is null)
        {
            return Unauthorized();
        }

        var sessionTripId = await sessionService.GetSessionTripIdAsync(HttpContext, cancellationToken);
        var schedule = await FindScheduleAsync(user.Id, sessionTripId, cancellationToken);
        return schedule is null
            ? NotFound()
            : Ok(schedule);
    }

    [HttpGet("~/api/me/travel-preference-profile")]
    public async Task<ActionResult<TravelPreferenceProfileDto>> GetMyTravelPreferenceProfile(
        CancellationToken cancellationToken)
    {
        var user = await sessionService.GetUserAsync(HttpContext, cancellationToken);
        return user is null
            ? Unauthorized()
            : Ok(await userProfileService.GetProfileDtoAsync(user.Id, cancellationToken));
    }

    [HttpPatch("~/api/me/travel-preference-profile")]
    public async Task<ActionResult<TravelPreferenceProfileDto>> PatchMyTravelPreferenceProfile(
        [FromBody] TravelPreferenceProfilePatchDto patch,
        CancellationToken cancellationToken)
    {
        if (patch is null)
        {
            return this.ValidationError("profile", "Profile patch is required.");
        }

        var user = await sessionService.GetUserAsync(HttpContext, cancellationToken);
        if (user is null)
        {
            return Unauthorized();
        }

        try
        {
            return Ok(await userProfileService.PatchProfileAsync(user.Id, patch, cancellationToken));
        }
        catch (ArgumentException ex)
        {
            return this.ValidationError("profile", ex.Message);
        }
    }

    private async Task<TripScheduleDto?> FindScheduleAsync(
        Guid userId,
        Guid? sessionTripId = null,
        CancellationToken cancellationToken = default)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var tripsQuery = dbContext.Trips
            .AsNoTracking()
            .Include(existingTrip => existingTrip.Destination)
            .Include(existingTrip => existingTrip.Reservations)
            .Where(existingTrip => existingTrip.AppUserId == userId);

        if (sessionTripId.HasValue)
        {
            tripsQuery = tripsQuery.Where(existingTrip => existingTrip.Id == sessionTripId.Value);
        }

        var trip = await tripsQuery
            .OrderBy(existingTrip => existingTrip.StartsOn < today)
            .ThenBy(existingTrip => existingTrip.StartsOn)
            .FirstOrDefaultAsync(cancellationToken);

        return trip is null || trip.Destination is null
            ? null
            : ToScheduleDto(trip);
    }

    private async Task<ActionResult<UserEntitlementsDto>> GetEntitlementsByEmailAsync(
        string email,
        CancellationToken cancellationToken = default)
    {
        var user = await dbContext.AppUsers
            .AsNoTracking()
            .Include(user => user.Entitlements)
            .FirstOrDefaultAsync(user => user.Email == email, cancellationToken);

        return user is null
            ? NotFound()
            : Ok(ToDto(user));
    }

    private static UserEntitlementsDto ToDto(AppUser user)
    {
        var now = DateTimeOffset.UtcNow;
        var activeEntitlements = user.Entitlements
            .Where(entitlement => entitlement.ExpiresAt is null || entitlement.ExpiresAt > now)
            .OrderBy(entitlement => entitlement.AccessLevel)
            .ThenBy(entitlement => entitlement.GrantedAt)
            .ToList();

        return new UserEntitlementsDto(
            user.Id,
            user.Email,
            user.DisplayName,
            activeEntitlements.Select(entitlement => entitlement.AccessLevel).Distinct().ToList(),
            activeEntitlements
                .Where(entitlement => entitlement.DestinationId.HasValue)
                .Select(entitlement => entitlement.DestinationId!.Value)
                .Distinct()
                .ToList(),
            activeEntitlements
                .Where(entitlement => entitlement.TravelPackageId.HasValue)
                .Select(entitlement => entitlement.TravelPackageId!.Value)
                .Distinct()
                .ToList(),
            activeEntitlements
                .Select(entitlement => new UserEntitlementDto(
                    entitlement.Id,
                    entitlement.AccessLevel,
                    entitlement.DestinationId,
                    entitlement.TravelPackageId,
                    entitlement.GrantedAt,
                    entitlement.ExpiresAt,
                    entitlement.Source))
                .ToList());
    }

    private bool IsAdminRequest()
    {
        return User.Identity?.IsAuthenticated == true && User.IsInRole("Admin");
    }

    private async Task<ActionResult?> EnsureCanAccessUserAsync(Guid userId, CancellationToken cancellationToken)
    {
        if (IsAdminRequest())
        {
            return null;
        }

        var sessionUser = await sessionService.GetUserAsync(HttpContext, cancellationToken);
        if (sessionUser is null)
        {
            return Unauthorized();
        }

        return sessionUser.Id == userId
            ? null
            : Forbid();
    }

    private static TripScheduleDto ToScheduleDto(Trip trip)
    {
        return new TripScheduleDto(
            trip.Id,
            trip.TravelerName,
            trip.Destination!.Name,
            trip.StartsOn,
            trip.EndsOn,
            trip.Reservations
                .OrderBy(reservation => reservation.Date)
                .ThenBy(reservation => reservation.StartsAt)
                .Select(reservation => new ScheduleItemDto(
                    reservation.Id,
                    reservation.RecommendationId,
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
    }
}

using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TravelCompanion.Api.Data;
using TravelCompanion.Api.Models;
using TravelCompanion.Api.Services;
using TravelCompanion.Shared;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Api.Controllers;

[ApiController]
[Route("api/mobile")]
public sealed class MobileController(
    TravelCompanionDbContext dbContext,
    UserSessionService sessionService,
    ILogger<MobileController> logger) : ControllerBase
{
    [HttpGet("discover")]
    public async Task<ActionResult<MobileDiscoverDto>> GetDiscover(
        [FromQuery] string? destinationSlug = null,
        CancellationToken cancellationToken = default)
    {
        var totalStopwatch = Stopwatch.StartNew();

        var userStopwatch = Stopwatch.StartNew();
        var user = await sessionService.GetUserAsync(HttpContext, cancellationToken);
        userStopwatch.Stop();
        if (user is null)
        {
            return Unauthorized();
        }

        var destinationStopwatch = Stopwatch.StartNew();
        var destination = await FindDestinationAsync(destinationSlug, cancellationToken);
        destinationStopwatch.Stop();
        if (destination is null)
        {
            return NotFound();
        }

        var entitlements = ToEntitlementsDto(user);
        var recommendationsStopwatch = Stopwatch.StartNew();
        var recommendations = await GetUnlockedRecommendationsAsync(destination.Id, entitlements, cancellationToken);
        recommendationsStopwatch.Stop();
        totalStopwatch.Stop();

        Response.Headers["Server-Timing"] = FormatServerTiming(
            ("session", userStopwatch.Elapsed.TotalMilliseconds),
            ("destination", destinationStopwatch.Elapsed.TotalMilliseconds),
            ("recommendations", recommendationsStopwatch.Elapsed.TotalMilliseconds),
            ("total", totalStopwatch.Elapsed.TotalMilliseconds));

        logger.LogInformation(
            "Mobile discover loaded in {ElapsedMs}ms. Destination={DestinationSlug}; Recommendations={RecommendationCount}.",
            totalStopwatch.Elapsed.TotalMilliseconds,
            destination.Slug,
            recommendations.Count);

        return Ok(new MobileDiscoverDto(
            DateTimeOffset.UtcNow,
            destination,
            recommendations));
    }

    [HttpGet("bootstrap")]
    public async Task<ActionResult<MobileBootstrapDto>> GetBootstrap(
        [FromQuery] string? destinationSlug = null,
        CancellationToken cancellationToken = default)
    {
        var totalStopwatch = Stopwatch.StartNew();

        var userStopwatch = Stopwatch.StartNew();
        var user = await sessionService.GetUserAsync(HttpContext, cancellationToken);
        userStopwatch.Stop();
        if (user is null)
        {
            return Unauthorized();
        }

        var destinationStopwatch = Stopwatch.StartNew();
        var destination = await FindDestinationAsync(destinationSlug, cancellationToken);
        destinationStopwatch.Stop();

        if (destination is null)
        {
            return NotFound();
        }

        var entitlements = ToEntitlementsDto(user);
        var recommendationsStopwatch = Stopwatch.StartNew();
        var unlockedRecommendations = await GetUnlockedRecommendationsAsync(destination.Id, entitlements, cancellationToken);
        recommendationsStopwatch.Stop();

        var packagesStopwatch = Stopwatch.StartNew();
        var packages = await dbContext.TravelPackages
            .AsNoTracking()
            .Where(package => package.DestinationId == destination.Id)
            .OrderBy(package => package.Price)
            .ToListAsync(cancellationToken);
        packagesStopwatch.Stop();

        var scheduleStopwatch = Stopwatch.StartNew();
        var schedule = await FindScheduleAsync(user.Id, cancellationToken);
        scheduleStopwatch.Stop();
        totalStopwatch.Stop();

        Response.Headers["Server-Timing"] = FormatServerTiming(
            ("session", userStopwatch.Elapsed.TotalMilliseconds),
            ("destination", destinationStopwatch.Elapsed.TotalMilliseconds),
            ("recommendations", recommendationsStopwatch.Elapsed.TotalMilliseconds),
            ("packages", packagesStopwatch.Elapsed.TotalMilliseconds),
            ("schedule", scheduleStopwatch.Elapsed.TotalMilliseconds),
            ("total", totalStopwatch.Elapsed.TotalMilliseconds));

        logger.LogInformation(
            "Mobile bootstrap loaded in {ElapsedMs}ms. Destination={DestinationSlug}; Recommendations={RecommendationCount}; Packages={PackageCount}; HasSchedule={HasSchedule}.",
            totalStopwatch.Elapsed.TotalMilliseconds,
            destination.Slug,
            unlockedRecommendations.Count,
            packages.Count,
            schedule is not null);

        return Ok(new MobileBootstrapDto(
            DateTimeOffset.UtcNow,
            destination,
            entitlements,
            unlockedRecommendations,
            packages.Select(package => ToPackageDto(package, user)).ToList(),
            schedule));
    }

    private async Task<TripScheduleDto?> FindScheduleAsync(Guid userId, CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var trip = await dbContext.Trips
            .AsNoTracking()
            .Include(existingTrip => existingTrip.Destination)
            .Include(existingTrip => existingTrip.Reservations)
            .Where(existingTrip => existingTrip.AppUserId == userId)
            .OrderBy(existingTrip => existingTrip.StartsOn < today)
            .ThenBy(existingTrip => existingTrip.StartsOn)
            .FirstOrDefaultAsync(cancellationToken);

        return trip is null || trip.Destination is null
            ? null
            : new TripScheduleDto(
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
    }

    private static TravelPackageDto ToPackageDto(TravelPackage package, AppUser user)
    {
        var requiredAccessLevel = package.IsSubscription
            ? ContentAccessLevel.Subscription
            : ContentAccessLevel.Bundle;

        var activeEntitlements = GetActiveEntitlements(user);
        var isUnlocked = ContentAccessPolicy.IsUnlocked(
            requiredAccessLevel,
            activeEntitlements.Select(entitlement => entitlement.AccessLevel),
            activeEntitlements.Any(entitlement => entitlement.DestinationId == package.DestinationId),
            activeEntitlements.Any(entitlement => entitlement.TravelPackageId == package.Id));

        return new TravelPackageDto(
            package.Id,
            package.DestinationId,
            package.Name,
            package.Slug,
            package.Description,
            package.Price,
            package.Currency,
            package.IsSubscription,
            requiredAccessLevel,
            isUnlocked);
    }

    private static UserEntitlementsDto ToEntitlementsDto(AppUser user)
    {
        var activeEntitlements = GetActiveEntitlements(user)
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

    private static List<UserEntitlement> GetActiveEntitlements(AppUser user)
    {
        var now = DateTimeOffset.UtcNow;
        return user.Entitlements
            .Where(entitlement => entitlement.ExpiresAt is null || entitlement.ExpiresAt > now)
            .ToList();
    }

    private async Task<DestinationSummaryDto?> FindDestinationAsync(
        string? destinationSlug,
        CancellationToken cancellationToken)
    {
        var destinationsQuery = dbContext.Destinations
            .AsNoTracking();

        if (!string.IsNullOrWhiteSpace(destinationSlug))
        {
            destinationsQuery = destinationsQuery
                .Where(existingDestination => existingDestination.Slug == destinationSlug);
        }

        return await destinationsQuery
            .OrderBy(existingDestination => existingDestination.Name)
            .Select(existingDestination => new DestinationSummaryDto(
                existingDestination.Id,
                existingDestination.Name,
                existingDestination.Slug,
                existingDestination.Country,
                existingDestination.HeroImageUrl,
                existingDestination.ShortDescription))
            .FirstOrDefaultAsync(cancellationToken);
    }

    private async Task<IReadOnlyList<RecommendationDto>> GetUnlockedRecommendationsAsync(
        Guid destinationId,
        UserEntitlementsDto entitlements,
        CancellationToken cancellationToken)
    {
        var recommendations = await dbContext.Recommendations
            .AsNoTracking()
            .Where(recommendation => recommendation.DestinationId == destinationId)
            .OrderBy(recommendation => recommendation.Title)
            .ToListAsync(cancellationToken);

        return recommendations
            .Where(recommendation => ContentAccessPolicy.IsUnlocked(
                recommendation.AccessLevel,
                entitlements.AccessLevels,
                entitlements.DestinationIds.Contains(recommendation.DestinationId),
                hasPackageAccess: false))
            .Select(recommendation => new RecommendationDto(
                recommendation.Id,
                recommendation.DestinationId,
                recommendation.Title,
                recommendation.Category,
                recommendation.Neighborhood,
                recommendation.Description,
                recommendation.Latitude,
                recommendation.Longitude,
                recommendation.SuggestedDurationMinutes,
                recommendation.AccessLevel,
                null))
            .ToList();
    }

    private static string FormatServerTiming(params (string Name, double DurationMs)[] timings)
    {
        return string.Join(", ", timings.Select(timing =>
            FormattableString.Invariant($"{timing.Name};dur={timing.DurationMs:0.##}")));
    }
}

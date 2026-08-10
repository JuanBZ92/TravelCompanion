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
    ITodayRecommendationService todayRecommendationService,
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

        var sessionTripId = await sessionService.GetSessionTripIdAsync(HttpContext, cancellationToken);
        var destinationStopwatch = Stopwatch.StartNew();
        var destination = string.IsNullOrWhiteSpace(destinationSlug) && sessionTripId.HasValue
            ? await FindDestinationForTripAsync(user.Id, sessionTripId.Value, cancellationToken)
            : await FindDestinationAsync(destinationSlug, cancellationToken);
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

    [HttpGet("today")]
    public async Task<ActionResult<TodayDto>> GetToday(
        [FromQuery] DateOnly? date = null,
        [FromQuery] decimal? latitude = null,
        [FromQuery] decimal? longitude = null,
        CancellationToken cancellationToken = default)
    {
        var user = await sessionService.GetUserAsync(HttpContext, cancellationToken);
        if (user is null)
        {
            return Unauthorized();
        }

        var currentLocation = latitude.HasValue && longitude.HasValue
            ? new GeoPointDto(latitude.Value, longitude.Value)
            : null;
        var sessionTripId = await sessionService.GetSessionTripIdAsync(HttpContext, cancellationToken);
        var today = await todayRecommendationService.GetTodayAsync(
            user,
            sessionTripId,
            date,
            currentLocation,
            cancellationToken);

        return today is null ? NotFound() : Ok(today);
    }

    [HttpPost("recommendations/{id:guid}/signals")]
    public async Task<ActionResult<RecommendationSignalResponse>> RecordRecommendationSignal(
        Guid id,
        [FromBody] RecommendationSignalRequest request,
        CancellationToken cancellationToken = default)
    {
        var user = await sessionService.GetUserAsync(HttpContext, cancellationToken);
        if (user is null)
        {
            return Unauthorized();
        }

        var sessionTripId = await sessionService.GetSessionTripIdAsync(HttpContext, cancellationToken);
        var response = await todayRecommendationService.RecordSignalAsync(
            user,
            id,
            sessionTripId,
            request,
            cancellationToken);

        return response.Accepted ? Ok(response) : BadRequest(response);
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

        var sessionTripId = await sessionService.GetSessionTripIdAsync(HttpContext, cancellationToken);
        var destinationStopwatch = Stopwatch.StartNew();
        var destination = string.IsNullOrWhiteSpace(destinationSlug) && sessionTripId.HasValue
            ? await FindDestinationForTripAsync(user.Id, sessionTripId.Value, cancellationToken)
            : await FindDestinationAsync(destinationSlug, cancellationToken);
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
        var schedule = await FindScheduleAsync(user.Id, sessionTripId, cancellationToken);
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

    [HttpGet("recommendations/{id:guid}")]
    public async Task<ActionResult<RecommendationDto>> GetRecommendationDetail(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var user = await sessionService.GetUserAsync(HttpContext, cancellationToken);
        if (user is null)
        {
            return Unauthorized();
        }

        var recommendation = await dbContext.Recommendations
            .AsNoTracking()
            .Include(existingRecommendation => existingRecommendation.Packages)
            .FirstOrDefaultAsync(existingRecommendation => existingRecommendation.Id == id, cancellationToken);

        if (recommendation is null)
        {
            return NotFound();
        }

        var entitlements = ToEntitlementsDto(user);
        if (!IsRecommendationUnlocked(recommendation, entitlements))
        {
            return NotFound();
        }

        return Ok(ToRecommendationDto(recommendation, useSummaryDescription: false));
    }

    [HttpGet("docs")]
    public async Task<ActionResult<TravelDocsDto>> GetDocs(CancellationToken cancellationToken = default)
    {
        var user = await sessionService.GetUserAsync(HttpContext, cancellationToken);
        if (user is null)
        {
            return Unauthorized();
        }

        var sessionTripId = await sessionService.GetSessionTripIdAsync(HttpContext, cancellationToken);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var tripsQuery = dbContext.Trips
            .AsNoTracking()
            .Include(existingTrip => existingTrip.Destination)
            .Include(existingTrip => existingTrip.Reservations)
            .Include(existingTrip => existingTrip.Documents)
            .Where(existingTrip => existingTrip.AppUserId == user.Id);

        if (sessionTripId.HasValue)
        {
            tripsQuery = tripsQuery.Where(existingTrip => existingTrip.Id == sessionTripId.Value);
        }

        var trip = await tripsQuery
            .OrderBy(existingTrip => existingTrip.StartsOn < today)
            .ThenBy(existingTrip => existingTrip.StartsOn)
            .FirstOrDefaultAsync(cancellationToken);

        if (trip is null || trip.Destination is null)
        {
            return NotFound();
        }

        var documents = trip.Documents
            .OrderBy(document => document.Category)
            .ThenBy(document => document.SortOrder)
            .ThenBy(document => document.Title)
            .Select(ToTravelDocumentDto)
            .ToList();

        return Ok(new TravelDocsDto(
            trip.Id,
            trip.TravelerName,
            trip.Destination.Name,
            trip.StartsOn,
            trip.EndsOn,
            CreateFlightDocsSection(trip),
            documents.Where(document => document.Category == TravelDocumentCategory.Hotel).ToList(),
            documents.Where(document => document.Category == TravelDocumentCategory.Other).ToList(),
            trip.Reservations
                .Where(reservation => reservation.Type == ReservationType.Lodging)
                .OrderBy(reservation => reservation.Date)
                .ThenBy(reservation => reservation.StartsAt)
                .Select(ToHotelDocDto)
                .ToList()));
    }

    private async Task<TripScheduleDto?> FindScheduleAsync(
        Guid userId,
        Guid? sessionTripId,
        CancellationToken cancellationToken)
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

    private static FlightDocsSectionDto? CreateFlightDocsSection(Trip trip)
    {
        var flights = trip.Reservations
            .Where(reservation => reservation.Type == ReservationType.Flight)
            .OrderBy(reservation => reservation.Date)
            .ThenBy(reservation => reservation.StartsAt)
            .ToList();

        if (flights.Count == 0)
        {
            return null;
        }

        var splitIndex = flights.Count > 2
            ? (int)Math.Ceiling(flights.Count / 2d)
            : flights.Count;
        var outbound = flights.Take(splitIndex).ToList();
        var inbound = flights.Skip(splitIndex).ToList();
        var journeys = new List<FlightJourneyDto> { CreateFlightJourney("ida", "Ida", outbound) };
        if (inbound.Count > 0)
        {
            journeys.Add(CreateFlightJourney("vuelta", "Vuelta", inbound));
        }

        return new FlightDocsSectionDto(
            flights.FirstOrDefault(flight => !string.IsNullOrWhiteSpace(flight.Airline))?.Airline,
            trip.TravelerName,
            CreateRouteLabel(flights.First(), flights.Last()),
            flights.FirstOrDefault(flight => !string.IsNullOrWhiteSpace(flight.ConfirmationCode))?.ConfirmationCode,
            journeys);
    }

    private static FlightJourneyDto CreateFlightJourney(string id, string label, IReadOnlyList<Reservation> flights)
    {
        return new FlightJourneyDto(
            id,
            label,
            CreateRouteLabel(flights.First(), flights.Last()),
            flights.Select((flight, index) => ToFlightLegDto(
                flight,
                index > 0 ? flights[index - 1] : null)).ToList());
    }

    private static FlightLegDto ToFlightLegDto(Reservation flight, Reservation? previousFlight)
    {
        var arriveDate = flight.EndsOn ?? flight.Date;
        return new FlightLegDto(
            flight.Id,
            flight.Date,
            flight.StartsAt,
            arriveDate,
            flight.EndsAt,
            flight.FlightNumber,
            CreateDurationLabel(flight.Date, flight.StartsAt, arriveDate, flight.EndsAt),
            null,
            CreateAirportLabel(flight.OriginName, flight.OriginAirport),
            CreateAirportLabel(flight.DestinationName, flight.DestinationAirport),
            CreateConnectionNote(previousFlight, flight));
    }

    private static string? CreateConnectionNote(Reservation? previousFlight, Reservation flight)
    {
        if (previousFlight?.EndsAt is null)
        {
            return null;
        }

        var previousEndDate = previousFlight.EndsOn ?? previousFlight.Date;
        var previousEnd = previousEndDate.ToDateTime(previousFlight.EndsAt.Value);
        var nextStart = flight.Date.ToDateTime(flight.StartsAt);
        if (nextStart <= previousEnd)
        {
            return null;
        }

        var layover = nextStart - previousEnd;
        var place = !string.IsNullOrWhiteSpace(previousFlight.DestinationName)
            ? previousFlight.DestinationName
            : previousFlight.DestinationAirport;

        return string.IsNullOrWhiteSpace(place)
            ? $"Escala · {FormatDuration(layover)}"
            : $"Escala en {place} · {FormatDuration(layover)}";
    }

    private static string? CreateDurationLabel(
        DateOnly date,
        TimeOnly startsAt,
        DateOnly endsOn,
        TimeOnly? endsAt)
    {
        if (!endsAt.HasValue)
        {
            return null;
        }

        var duration = endsOn.ToDateTime(endsAt.Value) - date.ToDateTime(startsAt);
        return duration <= TimeSpan.Zero ? null : FormatDuration(duration);
    }

    private static string FormatDuration(TimeSpan duration)
    {
        var hours = (int)duration.TotalHours;
        return duration.Minutes == 0
            ? $"{hours}h"
            : $"{hours}h {duration.Minutes:00}m";
    }

    private static string CreateRouteLabel(Reservation firstFlight, Reservation lastFlight)
    {
        return $"{CreatePlaceLabel(firstFlight.OriginName, firstFlight.OriginAirport)} -> {CreatePlaceLabel(lastFlight.DestinationName, lastFlight.DestinationAirport)}";
    }

    private static string CreateAirportLabel(string? name, string? airport)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return airport ?? string.Empty;
        }

        return string.IsNullOrWhiteSpace(airport)
            ? name
            : $"{airport} · {name}";
    }

    private static string CreatePlaceLabel(string? name, string? fallback)
    {
        return string.IsNullOrWhiteSpace(name)
            ? fallback ?? string.Empty
            : name;
    }

    private static TravelDocumentDto ToTravelDocumentDto(TravelDocument document) =>
        new(
            document.Id,
            document.Category,
            document.Title,
            document.Subtitle,
            document.FileUrl,
            document.SortOrder);

    private static TravelHotelDocDto ToHotelDocDto(Reservation reservation) =>
        new(
            reservation.Id,
            reservation.City,
            string.IsNullOrWhiteSpace(reservation.LocationName)
                ? reservation.Title
                : reservation.LocationName,
            CreateHotelDateRange(reservation),
            string.IsNullOrWhiteSpace(reservation.ConfirmationCode) ? null : reservation.ConfirmationCode,
            string.IsNullOrWhiteSpace(reservation.Address) ? null : reservation.Address);

    private static string CreateHotelDateRange(Reservation reservation)
    {
        var start = reservation.Date.ToString("dd/MM");
        return reservation.EndsOn.HasValue
            ? $"{start} - {reservation.EndsOn.Value:dd/MM}"
            : start;
    }

    private static TravelPackageDto ToPackageDto(TravelPackage package, AppUser user)
    {
        var activeEntitlements = GetActiveEntitlements(user);
        var requiredAccessLevel = ContentAccessLevel.Paid;
        var isUnlocked = activeEntitlements.Any(entitlement => entitlement.TravelPackageId == package.Id)
            || activeEntitlements.Any(entitlement =>
                entitlement.AccessLevel == ContentAccessLevel.Subscription
                && entitlement.DestinationId == package.DestinationId);

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

    private async Task<DestinationSummaryDto?> FindDestinationForTripAsync(
        Guid userId,
        Guid tripId,
        CancellationToken cancellationToken)
    {
        return await dbContext.Trips
            .AsNoTracking()
            .Where(trip => trip.Id == tripId && trip.AppUserId == userId)
            .Include(trip => trip.Destination)
            .Select(trip => trip.Destination == null
                ? null
                : new DestinationSummaryDto(
                    trip.Destination.Id,
                    trip.Destination.Name,
                    trip.Destination.Slug,
                    trip.Destination.Country,
                    trip.Destination.HeroImageUrl,
                    trip.Destination.ShortDescription))
            .FirstOrDefaultAsync(cancellationToken);
    }

    private async Task<IReadOnlyList<RecommendationDto>> GetUnlockedRecommendationsAsync(
        Guid destinationId,
        UserEntitlementsDto entitlements,
        CancellationToken cancellationToken)
    {
        var recommendations = await dbContext.Recommendations
            .AsNoTracking()
            .Include(recommendation => recommendation.Packages)
            .Where(recommendation => recommendation.DestinationId == destinationId)
            .OrderBy(recommendation => recommendation.Title)
            .ToListAsync(cancellationToken);

        return recommendations
            .Where(recommendation => IsRecommendationUnlocked(recommendation, entitlements))
            .Select(recommendation => ToRecommendationDto(recommendation, useSummaryDescription: true))
            .ToList();
    }

    private static RecommendationDto ToRecommendationDto(
        Recommendation recommendation,
        bool useSummaryDescription) =>
        new(
            recommendation.Id,
            recommendation.DestinationId,
            recommendation.Title,
            recommendation.Category,
            recommendation.Neighborhood,
            useSummaryDescription
                ? CreateSummaryDescription(recommendation.Description)
                : recommendation.Description,
            recommendation.Tags,
            recommendation.PriceLevel,
            recommendation.Latitude,
            recommendation.Longitude,
            recommendation.SuggestedDurationMinutes,
            recommendation.Rating,
            recommendation.OpeningHours,
            recommendation.AccessLevel,
            recommendation.Packages.Select(package => package.Id).ToList(),
            null);

    private static string CreateSummaryDescription(string description)
    {
        const int maxLength = 96;
        var normalized = string.Join(
            ' ',
            description.Split(['\r', '\n', '\t', ' '], StringSplitOptions.RemoveEmptyEntries));

        return normalized.Length <= maxLength
            ? normalized
            : $"{normalized[..maxLength].TrimEnd()}...";
    }

    private static bool IsRecommendationUnlocked(Recommendation recommendation, UserEntitlementsDto entitlements)
    {
        return ContentAccessPolicy.IsRecommendationUnlocked(
            entitlements,
            recommendation.AccessLevel,
            recommendation.DestinationId,
            recommendation.Packages.Select(package => package.Id).ToList());
    }

    private static string FormatServerTiming(params (string Name, double DurationMs)[] timings)
    {
        return string.Join(", ", timings.Select(timing =>
            FormattableString.Invariant($"{timing.Name};dur={timing.DurationMs:0.##}")));
    }
}

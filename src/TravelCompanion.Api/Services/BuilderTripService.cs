using Microsoft.EntityFrameworkCore;
using TravelCompanion.Api.Data;
using TravelCompanion.Api.Models;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Api.Services;

public sealed class BuilderTripService(
    TravelCompanionDbContext dbContext,
    UserSessionService sessionService)
{
    private const int MaxTripDays = 91;

    public async Task<BuilderTripSetupDto?> GetAsync(
        HttpContext httpContext,
        CancellationToken cancellationToken = default)
    {
        var access = await GetBuilderAccessAsync(httpContext, cancellationToken);
        if (access is null)
        {
            return null;
        }

        var grant = await LoadGrantAsync(access.User.Id, cancellationToken);
        if (grant?.TripId is null)
        {
            return EmptySetup(grant?.Destination?.Name ?? "Japan", grant?.Destination?.TimeZoneId ?? "Asia/Tokyo");
        }

        var trip = await dbContext.Trips
            .AsNoTracking()
            .Include(item => item.DayPlans)
            .Include(item => item.Destination)
            .FirstOrDefaultAsync(item => item.Id == grant.TripId && item.AppUserId == access.User.Id, cancellationToken);
        return trip is null ? EmptySetup("Japan", "Asia/Tokyo") : ToDto(trip);
    }

    public async Task<BuilderTripSetupDto> SaveAsync(
        HttpContext httpContext,
        SaveBuilderTripSetupRequest request,
        CancellationToken cancellationToken = default)
    {
        var access = await GetBuilderAccessAsync(httpContext, cancellationToken)
            ?? throw new UnauthorizedAccessException();
        Validate(request);

        var grant = await LoadGrantAsync(access.User.Id, cancellationToken)
            ?? throw new InvalidOperationException("No active builder access was found.");
        Trip trip;
        if (grant.TripId.HasValue)
        {
            trip = await dbContext.Trips
                .Include(item => item.DayPlans).ThenInclude(day => day.Blocks)
                .Include(item => item.Reservations)
                .Include(item => item.Destination)
                .SingleAsync(item => item.Id == grant.TripId && item.AppUserId == access.User.Id, cancellationToken);
            if (trip.PlanRevision != request.ExpectedRevision)
            {
                throw new BuilderRevisionConflictException(trip.PlanRevision);
            }

            if (trip.Reservations.Any(item => item.Date < request.ArrivalDate || item.Date > request.DepartureDate))
            {
                throw new InvalidOperationException("Move or remove itinerary items outside the new date range first.");
            }
        }
        else
        {
            trip = new Trip
            {
                Id = Guid.NewGuid(),
                AppUserId = access.User.Id,
                DestinationId = grant.DestinationId,
                Destination = grant.Destination,
                TravelerName = access.User.DisplayName,
                StartsOn = request.ArrivalDate,
                EndsOn = request.DepartureDate,
                TimeZoneId = request.TimeZoneId,
                ExperienceMode = ExperienceMode.SelfServiceBuilder,
                PublicationStatus = TripPublicationStatus.Published,
                PlanRevision = 0,
                PublishedAtUtc = DateTimeOffset.UtcNow,
                UpdatedAtUtc = DateTimeOffset.UtcNow
            };
            dbContext.Trips.Add(trip);
            grant.TripId = trip.Id;
        }

        trip.StartsOn = request.ArrivalDate;
        trip.EndsOn = request.DepartureDate;
        trip.TimeZoneId = request.TimeZoneId;
        trip.ExperienceMode = ExperienceMode.SelfServiceBuilder;
        SynchronizeDays(trip, request.Segments);
        trip.PlanRevision++;
        trip.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
        await sessionService.BindCurrentSessionToTripAsync(httpContext, trip.Id, cancellationToken);
        return ToDto(trip);
    }

    private async Task<TravelerAccessContext?> GetBuilderAccessAsync(HttpContext context, CancellationToken cancellationToken)
    {
        var session = await sessionService.GetSessionContextAsync(context, cancellationToken);
        return session?.AccessMode == TravelCompanion.Shared.SessionAccessMode.Builder
            ? new TravelerAccessContext(session, ExperienceMode.SelfServiceBuilder, TravelerAccessService.CreateCapabilities(ExperienceMode.SelfServiceBuilder, !session.TripId.HasValue))
            : null;
    }

    private Task<BuilderAccessGrant?> LoadGrantAsync(Guid userId, CancellationToken cancellationToken) => dbContext.BuilderAccessGrants
        .Include(item => item.Destination)
        .Where(item => item.AppUserId == userId && item.Status == TravelCompanion.Shared.BuilderAccessStatus.Active && item.RevokedAtUtc == null)
        .OrderByDescending(item => item.CreatedAtUtc)
        .FirstOrDefaultAsync(cancellationToken);

    private static void Validate(SaveBuilderTripSetupRequest request)
    {
        if (request.DepartureDate < request.ArrivalDate || request.DepartureDate.DayNumber - request.ArrivalDate.DayNumber + 1 > MaxTripDays)
        {
            throw new ArgumentException("The trip must contain between 1 and 91 days.");
        }

        if (request.Segments.Count == 0)
        {
            throw new ArgumentException("Add at least one city segment.");
        }

        var cursor = request.ArrivalDate;
        foreach (var segment in request.Segments.OrderBy(item => item.StartsOn))
        {
            if (string.IsNullOrWhiteSpace(segment.City) || segment.StartsOn != cursor || segment.EndsOn < segment.StartsOn)
            {
                throw new ArgumentException("City segments must cover every trip day without gaps or overlaps.");
            }

            cursor = segment.EndsOn.AddDays(1);
        }

        if (cursor != request.DepartureDate.AddDays(1))
        {
            throw new ArgumentException("City segments must cover every trip day without gaps or overlaps.");
        }
    }

    private static void SynchronizeDays(Trip trip, IReadOnlyList<BuilderTripSetupSegmentDto> segments)
    {
        var desiredDates = Enumerable.Range(0, trip.EndsOn.DayNumber - trip.StartsOn.DayNumber + 1)
            .Select(offset => trip.StartsOn.AddDays(offset)).ToHashSet();
        trip.DayPlans.RemoveAll(day => !desiredDates.Contains(day.Date));
        var byDate = trip.DayPlans.ToDictionary(day => day.Date);

        foreach (var date in desiredDates.Order())
        {
            var segment = segments.Single(item => date >= item.StartsOn && date <= item.EndsOn);
            if (!byDate.TryGetValue(date, out var day))
            {
                day = new TripDayPlan { Id = Guid.NewGuid(), TripId = trip.Id, Date = date };
                trip.DayPlans.Add(day);
            }

            day.DayNumber = date.DayNumber - trip.StartsOn.DayNumber + 1;
            day.City = segment.City.Trim();
            day.HotelBase = segment.HotelName?.Trim() ?? string.Empty;
            day.BaseAddress = segment.HotelAddress?.Trim() ?? string.Empty;
            day.BaseProviderPlaceId = segment.HotelPlaceId?.Trim();
            day.BaseLatitude = segment.HotelLatitude;
            day.BaseLongitude = segment.HotelLongitude;
            foreach (var period in TripPlanPeriods.All)
            {
                var block = day.Blocks.FirstOrDefault(item => item.PeriodKey == period.Key);
                if (block is null)
                {
                    day.Blocks.Add(new TripDayBlock
                    {
                        Id = Guid.NewGuid(),
                        TripDayPlanId = day.Id,
                        PeriodKey = period.Key,
                        SortOrder = period.SortOrder,
                        AutofillEnabled = false
                    });
                }
                else
                {
                    block.AutofillEnabled = false;
                }
            }
        }
    }

    private static BuilderTripSetupDto EmptySetup(string destination, string timeZoneId) =>
        new(false, null, 0, null, null, destination, timeZoneId, []);

    private static BuilderTripSetupDto ToDto(Trip trip)
    {
        var segments = new List<BuilderTripSetupSegmentDto>();
        foreach (var day in trip.DayPlans.OrderBy(item => item.Date))
        {
            var previous = segments.LastOrDefault();
            if (previous is not null && previous.City == day.City && previous.HotelName == day.HotelBase
                && previous.HotelAddress == day.BaseAddress && previous.EndsOn.AddDays(1) == day.Date)
            {
                segments[^1] = previous with { EndsOn = day.Date };
            }
            else
            {
                segments.Add(new(day.City, day.Date, day.Date, day.HotelBase, day.BaseAddress, day.BaseLatitude, day.BaseLongitude, day.BaseProviderPlaceId));
            }
        }

        return new(true, trip.Id, trip.PlanRevision, trip.StartsOn, trip.EndsOn, trip.Destination?.Name ?? "Japan", trip.TimeZoneId, segments);
    }
}

public sealed class BuilderRevisionConflictException(int currentRevision) : Exception("The itinerary changed. Reload it before saving.")
{
    public int CurrentRevision { get; } = currentRevision;
}

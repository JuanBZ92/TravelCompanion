using Microsoft.EntityFrameworkCore;
using System.Data;
using TravelCompanion.Api.Data;
using TravelCompanion.Api.Models;
using TravelCompanion.Shared;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Api.Services;

public sealed class BuilderTripService(
    TravelCompanionDbContext dbContext,
    UserSessionService sessionService,
    FreeTrialAccessService? freeTrialAccessService = null)
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

        var grant = await LoadGrantAsync(access.User.Id, access.TripId, cancellationToken);
        var trialStatus = grant?.IsTrial == true ? freeTrialAccessService?.ToStatus(grant) : null;
        if (trialStatus?.State == TrialAccessState.Expired)
        {
            return EmptySetup(
                grant?.Destination?.Name ?? "Japan",
                grant?.Destination?.TimeZoneId ?? "Asia/Tokyo",
                trialStatus);
        }
        if (grant?.TripId is null)
        {
            return EmptySetup(grant?.Destination?.Name ?? "Japan", grant?.Destination?.TimeZoneId ?? "Asia/Tokyo", trialStatus);
        }

        var trip = await dbContext.Trips
            .AsNoTracking()
            .AsSplitQuery()
            .Include(item => item.DayPlans)
            .Include(item => item.Reservations)
            .Include(item => item.Destination)
            .FirstOrDefaultAsync(item => item.Id == grant.TripId && item.AppUserId == access.User.Id, cancellationToken);
        return trip is null || trip.ExperienceMode != ExperienceMode.SelfServiceBuilder
            ? null
            : ToDto(trip, trialStatus);
    }

    public async Task<BuilderTripSetupDto> SaveAsync(
        HttpContext httpContext,
        SaveBuilderTripSetupRequest request,
        CancellationToken cancellationToken = default)
    {
        if (DbExecutionStrategy.ShouldExecute(dbContext))
            return await DbExecutionStrategy.ExecuteAsync(dbContext,
                () => SaveAsync(httpContext, request, cancellationToken), cancellationToken);
        var access = await GetBuilderAccessAsync(httpContext, cancellationToken)
            ?? throw new UnauthorizedAccessException();
        var trialStatus = access.Session.AccessMode == TravelCompanion.Shared.SessionAccessMode.FreeMapPreview
            ? await (freeTrialAccessService?.RequireEditingAsync(access.User.Id, startIfNeeded: true, cancellationToken)
                ?? throw new UnauthorizedAccessException())
            : null;
        Validate(request);

        await using var transaction = dbContext.Database.IsRelational()
            ? await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken) : null;
        if (access.TripId.HasValue)
            await TripConcurrencyLock.LockAsync(dbContext, access.TripId.Value, cancellationToken);
        var grant = await LoadGrantAsync(access.User.Id, access.TripId, cancellationToken)
            ?? throw new InvalidOperationException("No active builder access was found.");
        Trip trip;
        if (grant.TripId.HasValue)
        {
            trip = await dbContext.Trips
                .AsSplitQuery()
                .Include(item => item.DayPlans).ThenInclude(day => day.Blocks)
                .Include(item => item.Reservations)
                .Include(item => item.Destination)
                .SingleAsync(item => item.Id == grant.TripId && item.AppUserId == access.User.Id, cancellationToken);
            if (trip.ExperienceMode != ExperienceMode.SelfServiceBuilder)
            {
                throw new UnauthorizedAccessException();
            }

            if (trip.PlanRevision != request.ExpectedRevision)
            {
                throw new BuilderRevisionConflictException(trip.PlanRevision);
            }

            var excludedDates = trip.Reservations
                .Where(item => item.Date < request.ArrivalDate || item.Date > request.DepartureDate)
                .Select(item => item.Date)
                .Distinct()
                .Order()
                .ToList();
            if (excludedDates.Count > 0)
            {
                var dates = string.Join(", ", excludedDates.Select(item => item.ToString("dd/MM/yyyy")));
                throw new InvalidOperationException($"Hay planes fuera del nuevo rango en estas fechas: {dates}. Muévelos o elimínalos antes de guardar.");
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
        if (!grant.IsTrial && grant.MaximumExpiresAtUtc.HasValue)
        {
            if (!StorePurchaseService.IsTripWithinCoverage(trip, grant.MaximumExpiresAtUtc.Value))
                throw new InvalidOperationException("Las nuevas fechas quedan fuera de la vigencia máxima del pase.");
            var recalculated = StorePurchaseService.CalculateExpiry(trip, grant.MaximumExpiresAtUtc.Value);
            if (recalculated <= DateTimeOffset.UtcNow)
                throw new InvalidOperationException("Las nuevas fechas quedan fuera de la vigencia del pase.");
            grant.ExpiresAtUtc = recalculated;
        }
        SynchronizeDays(trip, request.Segments);
        trip.BuilderSegmentsJson = System.Text.Json.JsonSerializer.Serialize(request.Segments.OrderBy(segment => segment.StartsOn).ToList());
        trip.PlanRevision++;
        trip.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
        if (transaction is not null) await transaction.CommitAsync(cancellationToken);
        await sessionService.BindCurrentSessionToTripAsync(httpContext, trip.Id, cancellationToken);
        return ToDto(trip, trialStatus);
    }

    public async Task<BuilderTripSetupDto> DeleteAsync(
        HttpContext httpContext,
        DeleteBuilderTripSetupRequest request,
        CancellationToken cancellationToken = default)
    {
        if (DbExecutionStrategy.ShouldExecute(dbContext))
            return await DbExecutionStrategy.ExecuteAsync(dbContext,
                () => DeleteAsync(httpContext, request, cancellationToken), cancellationToken);
        var access = await GetBuilderAccessAsync(httpContext, cancellationToken)
            ?? throw new UnauthorizedAccessException();
        if (access.Session.AccessMode == TravelCompanion.Shared.SessionAccessMode.FreeMapPreview)
        {
            await (freeTrialAccessService?.RequireEditingAsync(access.User.Id, startIfNeeded: false, cancellationToken)
                ?? throw new UnauthorizedAccessException());
        }
        await using var transaction = dbContext.Database.IsRelational()
            ? await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken) : null;
        await TripConcurrencyLock.LockAsync(dbContext, request.TripId, cancellationToken);
        var grant = await LoadGrantAsync(access.User.Id, request.TripId, cancellationToken)
            ?? throw new InvalidOperationException("No active builder access was found.");
        if (grant.TripId != request.TripId)
        {
            throw new UnauthorizedAccessException();
        }

        var trip = await dbContext.Trips
            .Include(item => item.Reservations)
            .Include(item => item.DayPlans).ThenInclude(day => day.Blocks)
            .Include(item => item.Documents)
            .Include(item => item.PlanDraft)
            .SingleOrDefaultAsync(item => item.Id == request.TripId && item.AppUserId == access.User.Id, cancellationToken)
            ?? throw new UnauthorizedAccessException();
        if (trip.ExperienceMode != ExperienceMode.SelfServiceBuilder)
        {
            throw new UnauthorizedAccessException();
        }

        if (trip.PlanRevision != request.ExpectedRevision)
        {
            throw new BuilderRevisionConflictException(trip.PlanRevision);
        }

        // Store purchases stay attached to their original trip. Administrative/PIN
        // grants can reset their demo itinerary and reuse the same access PIN.
        if (grant.PurchaseTransactionId.HasValue)
        {
            var paidSessions = await dbContext.AppUserSessions
                .Where(item => item.UserId == access.User.Id && item.TripId == trip.Id)
                .ToListAsync(cancellationToken);
            foreach (var paidSession in paidSessions) paidSession.TripId = null;
            trip.IsArchived = true;
            trip.UpdatedAtUtc = DateTimeOffset.UtcNow;
            await dbContext.SaveChangesAsync(cancellationToken);
            if (transaction is not null) await transaction.CommitAsync(cancellationToken);
            return EmptySetup(grant.Destination?.Name ?? "Japan", grant.Destination?.TimeZoneId ?? "Asia/Tokyo", null);
        }

        var reservationIds = trip.Reservations.Select(item => item.Id).ToList();
        if (reservationIds.Count > 0)
        {
            var notifications = await dbContext.NotificationOutboxItems
                .Where(item => item.ReservationId.HasValue && reservationIds.Contains(item.ReservationId.Value))
                .ToListAsync(cancellationToken);
            dbContext.NotificationOutboxItems.RemoveRange(notifications);
        }

        var sessions = await dbContext.AppUserSessions
            .Where(item => item.UserId == access.User.Id && item.TripId == trip.Id)
            .ToListAsync(cancellationToken);
        foreach (var session in sessions)
        {
            session.TripId = null;
        }

        grant.TripId = null;
        dbContext.Trips.Remove(trip);
        await dbContext.SaveChangesAsync(cancellationToken);
        if (transaction is not null) await transaction.CommitAsync(cancellationToken);

        return EmptySetup(
            grant.Destination?.Name ?? "Japan",
            grant.Destination?.TimeZoneId ?? "Asia/Tokyo",
            grant.IsTrial ? freeTrialAccessService?.ToStatus(grant) : null);
    }

    private async Task<TravelerAccessContext?> GetBuilderAccessAsync(HttpContext context, CancellationToken cancellationToken)
    {
        var session = await sessionService.GetSessionContextAsync(context, cancellationToken);
        return session?.AccessMode is TravelCompanion.Shared.SessionAccessMode.Builder or TravelCompanion.Shared.SessionAccessMode.FreeMapPreview
            ? new TravelerAccessContext(session, ExperienceMode.SelfServiceBuilder, TravelerAccessService.CreateCapabilities(ExperienceMode.SelfServiceBuilder, !session.TripId.HasValue))
            : null;
    }

    private Task<BuilderAccessGrant?> LoadGrantAsync(Guid userId, Guid? tripId, CancellationToken cancellationToken) => dbContext.BuilderAccessGrants
        .Include(item => item.Destination)
        .Where(item => item.AppUserId == userId
            && (tripId.HasValue ? item.TripId == tripId : item.TripId == null)
            && item.Status == TravelCompanion.Shared.BuilderAccessStatus.Active
            && item.RevokedAtUtc == null
            && (!item.ExpiresAtUtc.HasValue || item.ExpiresAtUtc > DateTimeOffset.UtcNow))
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
        var firstSegment = true;
        foreach (var segment in request.Segments.OrderBy(item => item.StartsOn))
        {
            if (string.IsNullOrWhiteSpace(segment.City)
                || (segment.StartsOn != cursor && (firstSegment || segment.StartsOn != cursor.AddDays(-1)))
                || segment.EndsOn < segment.StartsOn)
            {
                throw new ArgumentException("City segments must cover every trip day without gaps or overlaps.");
            }

            cursor = segment.EndsOn.AddDays(1);
            firstSegment = false;
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
            // A shared transfer day uses the arriving city's hotel/base.
            var segment = segments.OrderBy(item => item.StartsOn).Last(item => date >= item.StartsOn && date <= item.EndsOn);
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

    private static BuilderTripSetupDto EmptySetup(
        string destination,
        string timeZoneId,
        TrialAccessStatusDto? trialAccess = null) =>
        new(false, null, 0, null, null, destination, timeZoneId, [], TrialAccess: trialAccess);

    private static BuilderTripSetupDto ToDto(Trip trip, TrialAccessStatusDto? trialAccess = null)
    {
        var segments = new List<BuilderTripSetupSegmentDto>();
        foreach (var day in trip.DayPlans.OrderBy(item => item.Date))
        {
            var previous = segments.LastOrDefault();
            if (previous is not null && previous.City == day.City && previous.HotelName == day.HotelBase && previous.HotelPlaceId == day.BaseProviderPlaceId
                && previous.HotelAddress == day.BaseAddress && previous.EndsOn.AddDays(1) == day.Date)
            {
                segments[^1] = previous with { EndsOn = day.Date };
            }
            else
            {
                segments.Add(new(day.City, day.Date, day.Date, day.HotelBase, day.BaseAddress, day.BaseLatitude, day.BaseLongitude, day.BaseProviderPlaceId));
            }
        }

        if (!string.IsNullOrWhiteSpace(trip.BuilderSegmentsJson))
        {
            var savedSegments = System.Text.Json.JsonSerializer.Deserialize<List<BuilderTripSetupSegmentDto>>(trip.BuilderSegmentsJson);
            if (savedSegments is { Count: > 0 } && savedSegments[0].StartsOn == trip.StartsOn
                && savedSegments[^1].EndsOn == trip.EndsOn) segments = savedSegments;
        }

        var scheduleItems = trip.Reservations
            .OrderBy(item => item.Date)
            .ThenBy(item => item.StartsAt)
            .Select(TravelerItineraryService.ToDto)
            .ToList();
        var schedule = new TripScheduleDto(
            trip.Id,
            trip.TravelerName,
            trip.Destination?.Name ?? "Japan",
            trip.StartsOn,
            trip.EndsOn,
            scheduleItems,
            trip.PlanRevision,
            ScheduleReviewAnalyzer.Analyze(scheduleItems, trip.StartsOn, trip.EndsOn));
        return new(
            true, trip.Id, trip.PlanRevision, trip.StartsOn, trip.EndsOn,
            trip.Destination?.Name ?? "Japan", trip.TimeZoneId, segments,
            trip.DayPlans.Select(day => day.Date).Order().ToList(),
            schedule,
            trialAccess);
    }

}

public sealed class BuilderRevisionConflictException(int currentRevision) : Exception("El itinerario cambió en otro dispositivo. Actualízalo antes de continuar.")
{
    public int CurrentRevision { get; } = currentRevision;
}

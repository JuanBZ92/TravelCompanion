using Microsoft.EntityFrameworkCore;
using System.Data;
using TravelCompanion.Api.Data;
using TravelCompanion.Api.Models;
using TravelCompanion.Shared;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Api.Services;

public sealed class TravelerItineraryService(
    TravelCompanionDbContext dbContext,
    TravelerAccessService accessService,
    FreeTrialAccessService? freeTrialAccessService = null,
    ProductAnalyticsService? analytics = null)
{
    public async Task<ItineraryItemMutationResponse> CreateAsync(
        HttpContext httpContext,
        ItineraryItemMutationRequest request,
        CancellationToken cancellationToken = default)
    {
        if (DbExecutionStrategy.ShouldExecute(dbContext))
            return await DbExecutionStrategy.ExecuteAsync(dbContext,
                () => CreateAsync(httpContext, request, cancellationToken), cancellationToken);
        var access = await accessService.GetAsync(httpContext, cancellationToken);
        await RequireActiveTrialEditingAsync(access, cancellationToken);
        if (access is null || !access.Capabilities.CanEditItinerary || access.TripId is null)
        {
            throw new UnauthorizedAccessException();
        }
        await using var transaction = dbContext.Database.IsRelational()
            ? await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken) : null;
        await TripConcurrencyLock.LockAsync(dbContext, access.TripId.Value, cancellationToken);

        var externalId = $"traveler-{request.IdempotencyKey.Trim()}";
        var priorResult = await dbContext.Reservations
            .AsNoTracking()
            .Where(item => item.TripId == access.TripId
                && item.Trip!.AppUserId == access.User.Id
                && item.ExternalId == externalId)
            .Select(item => new { Item = item, Revision = item.Trip!.PlanRevision })
            .SingleOrDefaultAsync(cancellationToken);
        if (priorResult is not null)
        {
            return new(true, "El lugar ya estaba en tu itinerario.", priorResult.Revision, ToDto(priorResult.Item));
        }

        var periodKey = ResolvePeriod(request);
        var (trip, block) = await LoadEditableContextAsync(httpContext, request.Date, periodKey, request.ExpectedRevision, cancellationToken);
        var existing = trip.Reservations.FirstOrDefault(item => item.ExternalId == externalId);
        if (existing is not null)
        {
            return new(true, "El lugar ya estaba en tu itinerario.", trip.PlanRevision, ToDto(existing));
        }

        var recommendation = request.RecommendationId.HasValue
            ? await dbContext.Recommendations.AsNoTracking().FirstOrDefaultAsync(item => item.Id == request.RecommendationId && item.DestinationId == trip.DestinationId, cancellationToken)
            : null;
        if (request.RecommendationId.HasValue && recommendation is null)
        {
            throw new InvalidOperationException("La recomendacion no esta disponible para este viaje.");
        }
        if (access.Session.AccessMode == SessionAccessMode.FreeMapPreview
            && (recommendation is null
                || freeTrialAccessService is null
                || !await freeTrialAccessService.IsRecommendationInFreeRadiusAsync(recommendation, cancellationToken)))
        {
            throw new InvalidOperationException("En la prueba gratuita solo puedes agregar recomendaciones dentro del radio abierto.");
        }

        var period = TripPlanPeriods.Find(periodKey)!;
        var startsAt = request.UseExactTime ? request.StartsAt ?? period.StartsAt : period.StartsAt;
        var overlap = trip.Reservations.Any(item => item.Date == request.Date && item.StartsAt == startsAt);
        if (overlap && !request.ConfirmOverlap)
        {
            return new(false, "Ya hay otro item en ese horario. Confirma para agregarlo igualmente.", trip.PlanRevision, HasOverlap: true);
        }

        var isGooglePlace = recommendation is null && !string.IsNullOrWhiteSpace(request.GooglePlaceId);
        var isFirstTravelerItem = !trip.Reservations.Any(existingItem => existingItem.Owner == ItineraryItemOwner.Traveler);
        var item = new Reservation
        {
            Id = Guid.NewGuid(),
            ExternalId = externalId,
            TripId = trip.Id,
            TripDayBlockId = block.Id,
            RecommendationId = recommendation?.Id,
            Type = ReservationType.Event,
            PlanningKind = ResolveKind(recommendation is not null || isGooglePlace, request.Flexibility),
            Owner = ItineraryItemOwner.Traveler,
            ItemSource = recommendation is not null ? ItineraryItemSource.YukuRecommendation
                : isGooglePlace ? ItineraryItemSource.GooglePlace
                : ItineraryItemSource.Manual,
            TimePrecision = request.UseExactTime ? ItineraryTimePrecision.Exact : ItineraryTimePrecision.PeriodOnly,
            Flexibility = request.Flexibility,
            DurationMinutes = request.DurationMinutes ?? recommendation?.SuggestedDurationMinutes,
            ProviderPlaceId = recommendation?.ProviderPlaceId ?? request.GooglePlaceId?.Trim(),
            Date = request.Date,
            StartsAt = startsAt,
            EndsAt = request.UseExactTime ? request.EndsAt : null,
            TimeZoneId = trip.TimeZoneId,
            Title = recommendation?.Title ?? request.Title.Trim(),
            City = recommendation?.Neighborhood.Split(',')[0].Trim() ?? request.City?.Trim() ?? block.TripDayPlan?.City ?? string.Empty,
            LocationName = recommendation?.Title ?? request.LocationName?.Trim() ?? request.Title.Trim(),
            Address = request.Address?.Trim() ?? recommendation?.Neighborhood ?? string.Empty,
            ConfirmationCode = string.Empty,
            Notes = request.Notes?.Trim() ?? recommendation?.Description ?? string.Empty,
            Latitude = recommendation?.Latitude ?? request.Latitude,
            Longitude = recommendation?.Longitude ?? request.Longitude,
            SourceName = itemSourceLabel(recommendation, request.GooglePlaceId),
            SourceUrl = recommendation?.SourceUrl,
            SortOrder = trip.Reservations.Where(existingItem => existingItem.TripDayBlockId == block.Id).Select(existingItem => existingItem.SortOrder).DefaultIfEmpty().Max() + 1
        };
        dbContext.Reservations.Add(item);
        trip.PlanRevision++;
        trip.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
        if (isFirstTravelerItem && analytics is not null)
            await analytics.RecordServerEventAsync(access.User.Id, trip.Id, "first_item_saved", "itinerary", null, cancellationToken);
        if (transaction is not null) await transaction.CommitAsync(cancellationToken);
        return new(true, "Agregado a tu itinerario.", trip.PlanRevision, ToDto(item));

        static string itemSourceLabel(Recommendation? recommendation, string? googlePlaceId) => recommendation is not null
            ? "YUKU Japan"
            : !string.IsNullOrWhiteSpace(googlePlaceId) ? "Google Places" : "Traveler";
    }

    public async Task<ItineraryItemMutationResponse> UpdateAsync(
        HttpContext httpContext,
        Guid id,
        ItineraryItemMutationRequest request,
        CancellationToken cancellationToken = default)
    {
        if (DbExecutionStrategy.ShouldExecute(dbContext))
            return await DbExecutionStrategy.ExecuteAsync(dbContext,
                () => UpdateAsync(httpContext, id, request, cancellationToken), cancellationToken);
        var access = await accessService.GetAsync(httpContext, cancellationToken);
        await RequireActiveTrialEditingAsync(access, cancellationToken);
        if (access is null || !access.Capabilities.CanEditItinerary || access.TripId is null)
            throw new UnauthorizedAccessException();
        await using var transaction = dbContext.Database.IsRelational()
            ? await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken) : null;
        await TripConcurrencyLock.LockAsync(dbContext, access.TripId.Value, cancellationToken);
        var periodKey = ResolvePeriod(request);
        var (trip, block) = await LoadEditableContextAsync(httpContext, request.Date, periodKey, request.ExpectedRevision, cancellationToken);
        var item = trip.Reservations.SingleOrDefault(existing => existing.Id == id)
            ?? throw new KeyNotFoundException();
        EnsureTravelerOwned(item);
        if (access.Session.AccessMode == SessionAccessMode.FreeMapPreview)
            await freeTrialAccessService!.RequirePlanningDateAsync(access.User.Id, trip.Id, item.Date, cancellationToken);
        var period = TripPlanPeriods.Find(periodKey)!;
        var startsAt = request.UseExactTime ? request.StartsAt!.Value : period.StartsAt;
        if (!request.ConfirmOverlap && trip.Reservations.Any(other => other.Id != id && other.Date == request.Date && other.StartsAt == startsAt))
            return new(false, "Ya hay otro item en ese horario. Confirma para agregarlo igualmente.", trip.PlanRevision, HasOverlap: true);
        item.TripDayBlockId = block.Id;
        item.Date = request.Date;
        item.StartsAt = request.UseExactTime ? request.StartsAt ?? period.StartsAt : period.StartsAt;
        item.EndsAt = request.UseExactTime ? request.EndsAt : null;
        item.TimePrecision = request.UseExactTime ? ItineraryTimePrecision.Exact : ItineraryTimePrecision.PeriodOnly;
        item.Flexibility = request.Flexibility;
        item.DurationMinutes = request.DurationMinutes ?? item.Recommendation?.SuggestedDurationMinutes;
        item.Title = request.Title.Trim();
        item.City = request.City?.Trim() ?? block.TripDayPlan?.City ?? item.City;
        item.LocationName = request.LocationName?.Trim() ?? request.Title.Trim();
        item.Address = request.Address?.Trim() ?? string.Empty;
        item.Notes = request.Notes?.Trim() ?? string.Empty;
        if (!item.RecommendationId.HasValue)
        {
            var googlePlaceId = request.GooglePlaceId?.Trim();
            if (!string.IsNullOrWhiteSpace(googlePlaceId))
            {
                var sameGooglePlace = item.ItemSource == ItineraryItemSource.GooglePlace
                    && string.Equals(item.ProviderPlaceId, googlePlaceId, StringComparison.Ordinal);
                item.ItemSource = ItineraryItemSource.GooglePlace;
                item.ProviderPlaceId = googlePlaceId;
                item.Latitude = request.Latitude ?? (sameGooglePlace ? item.Latitude : null);
                item.Longitude = request.Longitude ?? (sameGooglePlace ? item.Longitude : null);
                item.SourceName = "Google Places";
                item.SourceUrl = null;
            }
            else
            {
                item.ItemSource = ItineraryItemSource.Manual;
                item.ProviderPlaceId = null;
                item.Latitude = request.Latitude;
                item.Longitude = request.Longitude;
                item.SourceName = "Traveler";
                item.SourceUrl = null;
            }
        }
        item.PlanningKind = ResolveKind(
            item.RecommendationId.HasValue || item.ItemSource == ItineraryItemSource.GooglePlace,
            request.Flexibility);
        trip.PlanRevision++;
        trip.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
        if (transaction is not null) await transaction.CommitAsync(cancellationToken);
        return new(true, "Itinerario actualizado.", trip.PlanRevision, ToDto(item));
    }

    public async Task<ItineraryItemMutationResponse> DeleteAsync(
        HttpContext httpContext,
        Guid id,
        int expectedRevision,
        CancellationToken cancellationToken = default)
    {
        if (DbExecutionStrategy.ShouldExecute(dbContext))
            return await DbExecutionStrategy.ExecuteAsync(dbContext,
                () => DeleteAsync(httpContext, id, expectedRevision, cancellationToken), cancellationToken);
        var access = await accessService.GetAsync(httpContext, cancellationToken);
        await RequireActiveTrialEditingAsync(access, cancellationToken);
        if (access is null || !access.Capabilities.CanEditItinerary || access.TripId is null)
        {
            throw new UnauthorizedAccessException();
        }
        await using var transaction = dbContext.Database.IsRelational()
            ? await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken) : null;
        await TripConcurrencyLock.LockAsync(dbContext, access.TripId.Value, cancellationToken);

        var trip = await dbContext.Trips.Include(item => item.Reservations)
            .SingleAsync(item => item.Id == access.TripId && item.AppUserId == access.User.Id, cancellationToken);
        EnsureRevision(trip, expectedRevision);
        var item = trip.Reservations.SingleOrDefault(existing => existing.Id == id) ?? throw new KeyNotFoundException();
        EnsureTravelerOwned(item);
        if (access.Session.AccessMode == SessionAccessMode.FreeMapPreview)
            await freeTrialAccessService!.RequirePlanningDateAsync(access.User.Id, trip.Id, item.Date, cancellationToken);
        dbContext.Reservations.Remove(item);
        trip.PlanRevision++;
        trip.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
        if (transaction is not null) await transaction.CommitAsync(cancellationToken);
        return new(true, "Item eliminado.", trip.PlanRevision, DeletedItemId: id);
    }

    private async Task<(Trip Trip, TripDayBlock Block)> LoadEditableContextAsync(
        HttpContext httpContext,
        DateOnly date,
        string periodKey,
        int expectedRevision,
        CancellationToken cancellationToken)
    {
        var access = await accessService.GetAsync(httpContext, cancellationToken);
        await RequireActiveTrialEditingAsync(access, cancellationToken);
        if (access is null || !access.Capabilities.CanEditItinerary || access.TripId is null)
        {
            throw new UnauthorizedAccessException();
        }

        var period = TripPlanPeriods.Find(periodKey) ?? throw new ArgumentException("Momento del dia invalido.");
        var trip = await dbContext.Trips
            .Include(item => item.Reservations)
            .Include(item => item.DayPlans).ThenInclude(day => day.Blocks)
            .SingleAsync(item => item.Id == access.TripId && item.AppUserId == access.User.Id && item.ExperienceMode == ExperienceMode.SelfServiceBuilder, cancellationToken);
        EnsureRevision(trip, expectedRevision);
        if (date < trip.StartsOn || date > trip.EndsOn)
        {
            throw new ArgumentException("La fecha esta fuera del viaje.");
        }

        if (access.Session.AccessMode == SessionAccessMode.FreeMapPreview)
            await freeTrialAccessService!.RequirePlanningDateAsync(access.User.Id, trip.Id, date, cancellationToken);

        var block = trip.DayPlans.Single(day => day.Date == date).Blocks.Single(item => item.PeriodKey == period.Key);
        return (trip, block);
    }

    private async Task RequireActiveTrialEditingAsync(
        TravelerAccessContext? access,
        CancellationToken cancellationToken)
    {
        if (access?.Session.AccessMode == SessionAccessMode.FreeMapPreview)
        {
            await (freeTrialAccessService?.RequireEditingAsync(access.User.Id, startIfNeeded: false, cancellationToken)
                ?? throw new UnauthorizedAccessException());
        }
    }

    public static ScheduleItemKind ResolveKind(bool exact, bool place) => place
        ? ScheduleItemKind.Recommendation
        : ScheduleItemKind.ManualEvent;

    public static ScheduleItemKind ResolveKind(bool place, ItineraryFlexibility flexibility) =>
        flexibility == ItineraryFlexibility.ConfirmedReservation
            ? ScheduleItemKind.ConfirmedReservation
            : place ? ScheduleItemKind.Recommendation : ScheduleItemKind.ManualEvent;

    private static string ResolvePeriod(ItineraryItemMutationRequest request)
    {
        if (!request.UseExactTime) return request.PeriodKey;
        if (request.StartsAt is null) throw new ArgumentException("Indica la hora de inicio.");
        return TripPlanPeriods.Resolve(request.StartsAt.Value).Key;
    }

    private static void EnsureRevision(Trip trip, int expectedRevision)
    {
        if (trip.PlanRevision != expectedRevision)
        {
            throw new BuilderRevisionConflictException(trip.PlanRevision);
        }
    }

    private static void EnsureTravelerOwned(Reservation item)
    {
        if (item.Owner != ItineraryItemOwner.Traveler)
        {
            throw new UnauthorizedAccessException("Curated itinerary items cannot be edited from the app.");
        }
    }

    public static ScheduleItemDto ToDto(Reservation item) => new(
        item.Id, item.RecommendationId, item.Type, item.Date, item.StartsAt, item.EndsOn, item.EndsAt,
        item.Title, item.City, item.LocationName, item.Address, item.ConfirmationCode, item.Notes,
        item.Airline, item.FlightNumber, item.OriginName, item.DestinationName, item.OriginAirport,
        item.DestinationAirport, item.PlanningKind, item.Owner, item.ItemSource, item.TimePrecision,
        item.SortOrder, item.ProviderPlaceId, item.Latitude, item.Longitude, item.Flexibility, item.DurationMinutes);
}

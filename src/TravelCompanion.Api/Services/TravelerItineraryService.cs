using Microsoft.EntityFrameworkCore;
using TravelCompanion.Api.Data;
using TravelCompanion.Api.Models;
using TravelCompanion.Shared;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Api.Services;

public sealed class TravelerItineraryService(
    TravelCompanionDbContext dbContext,
    TravelerAccessService accessService)
{
    public async Task<ItineraryItemMutationResponse> CreateAsync(
        HttpContext httpContext,
        ItineraryItemMutationRequest request,
        CancellationToken cancellationToken = default)
    {
        var (trip, block) = await LoadEditableContextAsync(httpContext, request.Date, request.PeriodKey, request.ExpectedRevision, cancellationToken);
        var externalId = $"traveler-{request.IdempotencyKey.Trim()}";
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

        var period = TripPlanPeriods.Find(request.PeriodKey)!;
        var startsAt = request.UseExactTime ? request.StartsAt ?? period.StartsAt : period.StartsAt;
        var overlap = trip.Reservations.Any(item => item.Date == request.Date && item.StartsAt == startsAt);
        if (overlap && !request.ConfirmOverlap)
        {
            return new(false, "Ya hay otro item en ese horario. Confirma para agregarlo igualmente.", trip.PlanRevision, HasOverlap: true);
        }

        var isGooglePlace = recommendation is null && !string.IsNullOrWhiteSpace(request.GooglePlaceId);
        var item = new Reservation
        {
            Id = Guid.NewGuid(),
            ExternalId = externalId,
            TripId = trip.Id,
            TripDayBlockId = block.Id,
            RecommendationId = recommendation?.Id,
            Type = ReservationType.Event,
            PlanningKind = recommendation is null ? ScheduleItemKind.ManualEvent : ScheduleItemKind.Recommendation,
            Owner = ItineraryItemOwner.Traveler,
            ItemSource = recommendation is not null ? ItineraryItemSource.YukuRecommendation
                : isGooglePlace ? ItineraryItemSource.GooglePlace
                : ItineraryItemSource.Manual,
            TimePrecision = request.UseExactTime ? ItineraryTimePrecision.Exact : ItineraryTimePrecision.PeriodOnly,
            ProviderPlaceId = request.GooglePlaceId?.Trim(),
            Date = request.Date,
            StartsAt = startsAt,
            EndsAt = request.UseExactTime ? request.EndsAt : null,
            TimeZoneId = trip.TimeZoneId,
            Title = recommendation?.Title ?? request.Title.Trim(),
            City = recommendation?.Neighborhood.Split(',')[0].Trim() ?? request.City?.Trim() ?? block.TripDayPlan?.City ?? string.Empty,
            LocationName = recommendation?.Title ?? request.LocationName?.Trim() ?? request.Title.Trim(),
            Address = isGooglePlace ? string.Empty : request.Address?.Trim() ?? recommendation?.Neighborhood ?? string.Empty,
            ConfirmationCode = string.Empty,
            Notes = request.Notes?.Trim() ?? recommendation?.Description ?? string.Empty,
            Latitude = isGooglePlace ? null : recommendation?.Latitude ?? request.Latitude,
            Longitude = isGooglePlace ? null : recommendation?.Longitude ?? request.Longitude,
            SourceName = itemSourceLabel(recommendation, request.GooglePlaceId),
            SourceUrl = recommendation?.SourceUrl,
            SortOrder = trip.Reservations.Where(existingItem => existingItem.TripDayBlockId == block.Id).Select(existingItem => existingItem.SortOrder).DefaultIfEmpty().Max() + 1
        };
        dbContext.Reservations.Add(item);
        trip.PlanRevision++;
        trip.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
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
        var (trip, block) = await LoadEditableContextAsync(httpContext, request.Date, request.PeriodKey, request.ExpectedRevision, cancellationToken);
        var item = trip.Reservations.SingleOrDefault(existing => existing.Id == id)
            ?? throw new KeyNotFoundException();
        EnsureTravelerOwned(item);
        var period = TripPlanPeriods.Find(request.PeriodKey)!;
        item.TripDayBlockId = block.Id;
        item.Date = request.Date;
        item.StartsAt = request.UseExactTime ? request.StartsAt ?? period.StartsAt : period.StartsAt;
        item.EndsAt = request.UseExactTime ? request.EndsAt : null;
        item.TimePrecision = request.UseExactTime ? ItineraryTimePrecision.Exact : ItineraryTimePrecision.PeriodOnly;
        item.Title = request.Title.Trim();
        item.City = request.City?.Trim() ?? item.City;
        item.LocationName = request.LocationName?.Trim() ?? request.Title.Trim();
        item.Address = request.Address?.Trim() ?? string.Empty;
        item.Notes = request.Notes?.Trim() ?? string.Empty;
        item.Latitude = request.Latitude;
        item.Longitude = request.Longitude;
        trip.PlanRevision++;
        trip.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
        return new(true, "Itinerario actualizado.", trip.PlanRevision, ToDto(item));
    }

    public async Task<ItineraryItemMutationResponse> DeleteAsync(
        HttpContext httpContext,
        Guid id,
        int expectedRevision,
        CancellationToken cancellationToken = default)
    {
        var access = await accessService.GetAsync(httpContext, cancellationToken);
        if (access is null || !access.Capabilities.CanEditItinerary || access.TripId is null)
        {
            throw new UnauthorizedAccessException();
        }

        var trip = await dbContext.Trips.Include(item => item.Reservations)
            .SingleAsync(item => item.Id == access.TripId && item.AppUserId == access.User.Id, cancellationToken);
        EnsureRevision(trip, expectedRevision);
        var item = trip.Reservations.SingleOrDefault(existing => existing.Id == id) ?? throw new KeyNotFoundException();
        EnsureTravelerOwned(item);
        dbContext.Reservations.Remove(item);
        trip.PlanRevision++;
        trip.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
        return new(true, "Item eliminado.", trip.PlanRevision);
    }

    private async Task<(Trip Trip, TripDayBlock Block)> LoadEditableContextAsync(
        HttpContext httpContext,
        DateOnly date,
        string periodKey,
        int expectedRevision,
        CancellationToken cancellationToken)
    {
        var access = await accessService.GetAsync(httpContext, cancellationToken);
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

        var block = trip.DayPlans.Single(day => day.Date == date).Blocks.Single(item => item.PeriodKey == period.Key);
        return (trip, block);
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
        item.SortOrder, item.ProviderPlaceId);
}

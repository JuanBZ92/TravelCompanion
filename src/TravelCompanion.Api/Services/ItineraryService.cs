using Microsoft.EntityFrameworkCore;
using TravelCompanion.Api.Data;
using TravelCompanion.Api.Models;
using TravelCompanion.Shared;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Api.Services;

public sealed class ItineraryService(
    TravelCompanionDbContext dbContext,
    FreeTrialAccessService? freeTrialAccessService = null) : IItineraryService
{
    public async Task<SaveItineraryItemResponse> SaveItineraryItemAsync(
        AppUser user,
        SaveItineraryItemRequest request,
        CancellationToken cancellationToken)
    {
        var recommendation = await dbContext.Recommendations
            .AsNoTracking()
            .Include(existing => existing.Destination)
            .Include(existing => existing.Packages)
            .FirstOrDefaultAsync(existing => existing.Id == request.RecommendationId, cancellationToken);

        if (recommendation is null || recommendation.Destination is null)
        {
            return new SaveItineraryItemResponse(false, "No encontre esa recomendacion para guardarla.", null);
        }

        var trialGrant = freeTrialAccessService is null
            ? null
            : await freeTrialAccessService.GetGrantAsync(user.Id, cancellationToken);
        if (trialGrant is not null)
        {
            await freeTrialAccessService!.RequireEditingAsync(user.Id, startIfNeeded: false, cancellationToken);
            if (!await freeTrialAccessService.IsRecommendationInFreeRadiusAsync(recommendation, cancellationToken))
            {
                return new SaveItineraryItemResponse(false, "En la prueba gratuita solo puedes guardar lugares dentro del radio abierto.", null);
            }
        }

        var trip = await dbContext.Trips
            .AsSplitQuery()
            .Include(existing => existing.Destination)
            .Include(existing => existing.Reservations)
            .Include(existing => existing.DayPlans)
                .ThenInclude(day => day.Blocks)
            .Where(existing =>
                existing.PublicationStatus == TripPublicationStatus.Published
                && existing.AppUserId == user.Id
                && existing.DestinationId == recommendation.DestinationId
                && existing.StartsOn <= request.Date
                && existing.EndsOn >= request.Date)
            .FirstOrDefaultAsync(cancellationToken);

        if (trip is null)
        {
            return new SaveItineraryItemResponse(false, "No encontre un viaje propio para esa fecha y destino.", null);
        }

        if (!CanAccessRecommendation(user, recommendation))
        {
            return new SaveItineraryItemResponse(false, "Esa recomendacion no esta disponible para tu cuenta.", null);
        }

        if (request.ClientMutationId.HasValue)
        {
            var replayedReservation = trip.Reservations.FirstOrDefault(reservation =>
                reservation.ClientMutationId == request.ClientMutationId);
            if (replayedReservation is not null)
            {
                return AlreadySaved(replayedReservation, trip.PlanRevision);
            }
        }

        var existingReservation = trip.Reservations.FirstOrDefault(reservation =>
            reservation.Date == request.Date
            && (reservation.RecommendationId == recommendation.Id
                || string.Equals(reservation.Title, recommendation.Title, StringComparison.OrdinalIgnoreCase)));
        if (existingReservation is not null)
        {
            dbContext.RecommendationInteractionSignals.Add(CreateSavedSignal(user, trip.Id, recommendation.Id));
            await dbContext.SaveChangesAsync(cancellationToken);
            return new SaveItineraryItemResponse(
                true,
                "Ese plan ya estaba guardado en tu itinerario.",
                ToDto(existingReservation),
                trip.PlanRevision);
        }

        var endsAt = request.EndsAt
            ?? request.StartsAt.AddMinutes(recommendation.SuggestedDurationMinutes);
        var reservation = new Reservation
        {
            Id = Guid.NewGuid(),
            ClientMutationId = request.ClientMutationId,
            TripId = trip.Id,
            RecommendationId = recommendation.Id,
            ProviderPlaceId = recommendation.ProviderPlaceId,
            TripDayBlockId = trip.DayPlans
                .FirstOrDefault(day => day.Date == request.Date)?
                .Blocks.FirstOrDefault(block => block.PeriodKey == TripPlanPeriods.Resolve(request.StartsAt).Key)?.Id,
            Type = ReservationType.Event,
            PlanningKind = ScheduleItemKind.Recommendation,
            Owner = ItineraryItemOwner.Traveler,
            ItemSource = ItineraryItemSource.YukuRecommendation,
            TimePrecision = ItineraryTimePrecision.Exact,
            Date = request.Date,
            StartsAt = request.StartsAt,
            EndsAt = endsAt,
            Title = recommendation.Title,
            City = ResolveCity(recommendation, trip),
            LocationName = recommendation.Title,
            Address = recommendation.Neighborhood,
            ConfirmationCode = "AI-PLAN",
            Notes = "Guardado desde Travel Assistant.",
            Latitude = recommendation.Latitude,
            Longitude = recommendation.Longitude,
            SourceName = "Travel Assistant"
        };

        dbContext.Reservations.Add(reservation);
        dbContext.RecommendationInteractionSignals.Add(CreateSavedSignal(user, trip.Id, recommendation.Id));
        trip.PlanRevision++;
        trip.UpdatedAtUtc = DateTimeOffset.UtcNow;
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException) when (request.ClientMutationId.HasValue)
        {
            // A retry may race the original request. The unique database index is the
            // authority; reload the first committed result instead of creating a duplicate.
            dbContext.ChangeTracker.Clear();
            var committedReservation = await dbContext.Reservations
                .AsNoTracking()
                .Include(existing => existing.Trip)
                .FirstOrDefaultAsync(existing =>
                    existing.TripId == trip.Id
                    && existing.ClientMutationId == request.ClientMutationId,
                    cancellationToken);
            if (committedReservation is not null)
            {
                return AlreadySaved(committedReservation, committedReservation.Trip?.PlanRevision);
            }

            throw;
        }

        return new SaveItineraryItemResponse(
            true,
            "Plan guardado en tu itinerario.",
            ToDto(reservation),
            trip.PlanRevision);
    }

    private static SaveItineraryItemResponse AlreadySaved(Reservation reservation, int? revision) =>
        new(true, "Ese plan ya estaba guardado en tu itinerario.", ToDto(reservation), revision);

    private static bool CanAccessRecommendation(AppUser user, Recommendation recommendation)
    {
        var now = DateTimeOffset.UtcNow;
        var activeEntitlements = user.Entitlements
            .Where(entitlement => entitlement.ExpiresAt is null || entitlement.ExpiresAt > now)
            .ToList();

        var entitlements = new UserEntitlementsDto(
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

        return ContentAccessPolicy.IsRecommendationUnlocked(
            entitlements,
            recommendation.AccessLevel,
            recommendation.DestinationId,
            recommendation.Packages.Select(package => package.Id).ToList());
    }

    private static string ResolveCity(Recommendation recommendation, Trip trip)
    {
        var neighborhoodParts = recommendation.Neighborhood
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return neighborhoodParts.LastOrDefault(part => !string.IsNullOrWhiteSpace(part))
            ?? trip.Destination?.Name
            ?? "Destino";
    }

    private static ScheduleItemDto ToDto(Reservation reservation)
    {
        return new ScheduleItemDto(
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
            reservation.DestinationAirport,
            reservation.PlanningKind, reservation.Owner, reservation.ItemSource, reservation.TimePrecision,
            reservation.SortOrder, reservation.ProviderPlaceId, reservation.Latitude, reservation.Longitude);
    }

    private static RecommendationInteractionSignal CreateSavedSignal(
        AppUser user,
        Guid tripId,
        Guid recommendationId) =>
        new()
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            TripId = tripId,
            RecommendationId = recommendationId,
            Signal = RecommendationSignal.Saved,
            Source = "assistant_save_itinerary",
            OccurredAtUtc = DateTimeOffset.UtcNow,
            CreatedAtUtc = DateTimeOffset.UtcNow
        };
}

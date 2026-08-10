using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TravelCompanion.Api.Data;
using TravelCompanion.Api.Models;
using TravelCompanion.Shared;

namespace TravelCompanion.Api.Services;

public sealed class TripPlanEditorService(
    TravelCompanionDbContext dbContext,
    IPasswordHasher<Trip> tripPinHasher,
    ILogger<TripPlanEditorService> logger)
{
    public const string SourceName = "Trip Visual Editor";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public async Task<IReadOnlyList<TripPlanListItem>> ListTripsAsync(
        string? search,
        CancellationToken cancellationToken = default)
    {
        var query = dbContext.Trips
            .AsNoTracking()
            .Include(trip => trip.Destination)
            .Include(trip => trip.PlanDraft)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            query = query.Where(trip =>
                trip.TravelerName.ToLower().Contains(term)
                || (trip.Destination != null && trip.Destination.Name.ToLower().Contains(term)));
        }

        return await query
            .OrderByDescending(trip => trip.StartsOn)
            .ThenBy(trip => trip.TravelerName)
            .Select(trip => new TripPlanListItem(
                trip.Id,
                trip.TravelerName,
                trip.Destination != null ? trip.Destination.Name : "-",
                trip.StartsOn,
                trip.EndsOn,
                trip.PublicationStatus.ToString(),
                trip.PlanDraft != null,
                trip.PlanDraft != null ? trip.PlanDraft.UpdatedAtUtc : trip.UpdatedAtUtc))
            .ToListAsync(cancellationToken);
    }

    public async Task<Guid> CreateTripAsync(
        CreateTripPlanCommand command,
        CancellationToken cancellationToken = default)
    {
        var travelerName = command.TravelerName.Trim();
        var pin = NormalizePin(command.AccessPin);
        if (travelerName.Length == 0)
        {
            throw new ValidationException("El nombre del cliente es obligatorio.");
        }

        ValidateDateRange(command.StartsOn, command.EndsOn);
        var citySegments = NormalizeCitySegments(command.CitySegments, command.StartsOn, command.EndsOn);
        ValidatePinFormat(pin);
        if (!await IsPinAvailableAsync(pin, null, cancellationToken))
        {
            throw new ValidationException("Ese PIN ya pertenece a otro viaje.");
        }

        var destination = await dbContext.Destinations
            .FirstOrDefaultAsync(item => item.Id == command.DestinationId, cancellationToken)
            ?? throw new ValidationException("Selecciona un destino válido.");
        var tripId = Guid.NewGuid();
        var user = new AppUser
        {
            Id = Guid.NewGuid(),
            Email = $"trip-{tripId:N}@travelcompanion.local",
            DisplayName = travelerName,
            PasswordHash = string.Empty,
            MustChangePassword = false
        };
        var trip = new Trip
        {
            Id = tripId,
            AppUserId = user.Id,
            DestinationId = destination.Id,
            TravelerName = travelerName,
            StartsOn = command.StartsOn,
            EndsOn = command.EndsOn,
            TimeZoneId = string.IsNullOrWhiteSpace(command.TimeZoneId)
                ? destination.TimeZoneId
                : command.TimeZoneId.Trim(),
            PublicationStatus = TripPublicationStatus.Draft,
            PlanRevision = 0,
            UpdatedAtUtc = DateTimeOffset.UtcNow
        };
        var payload = CreatePayload(trip);
        ApplyCitySegments(payload, citySegments);
        var draft = new TripPlanDraft
        {
            TripId = trip.Id,
            BasePlanRevision = 0,
            PayloadJson = Serialize(payload),
            PendingAccessPinHash = tripPinHasher.HashPassword(trip, pin),
            UpdatedAtUtc = DateTimeOffset.UtcNow
        };

        dbContext.AppUsers.Add(user);
        dbContext.Trips.Add(trip);
        dbContext.TripPlanDrafts.Add(draft);
        await dbContext.SaveChangesAsync(cancellationToken);
        return trip.Id;
    }

    public async Task<TripPlanEditorState?> GetEditorAsync(
        Guid tripId,
        CancellationToken cancellationToken = default)
    {
        var trip = await LoadTripGraphAsync(tripId, cancellationToken);
        if (trip is null)
        {
            return null;
        }

        var payload = trip.PlanDraft is not null
            ? Deserialize(trip.PlanDraft.PayloadJson)
            : BuildPayloadFromPublished(trip);
        payload = NormalizePayload(payload);

        var recommendations = await dbContext.Recommendations
            .AsNoTracking()
            .Where(recommendation => recommendation.DestinationId == payload.DestinationId)
            .OrderBy(recommendation => recommendation.Title)
            .Select(recommendation => new TripPlanRecommendationCatalogItem(
                recommendation.Id,
                recommendation.Title,
                recommendation.Category,
                recommendation.Neighborhood,
                recommendation.CitySlug ?? string.Empty,
                recommendation.Description,
                recommendation.Tags,
                recommendation.PriceLevel,
                recommendation.SuggestedDurationMinutes,
                recommendation.Rating,
                recommendation.Latitude,
                recommendation.Longitude))
            .ToListAsync(cancellationToken);

        return new TripPlanEditorState(
            trip.Id,
            trip.PlanDraft?.BasePlanRevision ?? trip.PlanRevision,
            trip.PlanDraft is not null,
            !string.IsNullOrWhiteSpace(trip.PlanDraft?.PendingAccessPinHash),
            trip.PublicationStatus.ToString(),
            trip.PlanDraft?.UpdatedAtUtc,
            payload,
            recommendations);
    }

    public async Task<TripPlanOperationResult> SaveDraftAsync(
        Guid tripId,
        string payloadJson,
        int basePlanRevision,
        string? newPin,
        CancellationToken cancellationToken = default)
    {
        var trip = await dbContext.Trips
            .Include(item => item.PlanDraft)
            .FirstOrDefaultAsync(item => item.Id == tripId, cancellationToken);
        if (trip is null)
        {
            return TripPlanOperationResult.Fail("El viaje ya no existe.");
        }

        if (trip.PlanRevision != basePlanRevision
            || (trip.PlanDraft is not null && trip.PlanDraft.BasePlanRevision != basePlanRevision))
        {
            return TripPlanOperationResult.Fail(
                "El viaje cambió desde otra pantalla. Recargá el editor antes de guardar.",
                trip.PlanRevision);
        }

        TripPlanEditorPayload payload;
        try
        {
            payload = NormalizePayload(Deserialize(payloadJson));
            await ValidatePayloadAsync(payload, strict: false, cancellationToken);
        }
        catch (Exception exception) when (exception is JsonException or ValidationException)
        {
            return TripPlanOperationResult.Fail(exception.Message, trip.PlanRevision);
        }

        var draft = trip.PlanDraft ?? new TripPlanDraft
        {
            TripId = trip.Id,
            BasePlanRevision = trip.PlanRevision
        };
        if (trip.PlanDraft is null)
        {
            dbContext.TripPlanDrafts.Add(draft);
        }

        if (!string.IsNullOrWhiteSpace(newPin))
        {
            var pin = NormalizePin(newPin);
            try
            {
                ValidatePinFormat(pin);
            }
            catch (ValidationException exception)
            {
                return TripPlanOperationResult.Fail(exception.Message, trip.PlanRevision);
            }

            if (!await IsPinAvailableAsync(pin, trip.Id, cancellationToken))
            {
                return TripPlanOperationResult.Fail("Ese PIN ya pertenece a otro viaje.", trip.PlanRevision);
            }

            draft.PendingAccessPinHash = tripPinHasher.HashPassword(trip, pin);
        }

        draft.PayloadJson = Serialize(payload);
        draft.UpdatedAtUtc = DateTimeOffset.UtcNow;
        trip.UpdatedAtUtc = draft.UpdatedAtUtc;
        await dbContext.SaveChangesAsync(cancellationToken);
        return TripPlanOperationResult.Ok("Borrador guardado.", trip.PlanRevision);
    }

    public async Task<TripPlanOperationResult> PublishAsync(
        Guid tripId,
        string payloadJson,
        int basePlanRevision,
        string? newPin,
        CancellationToken cancellationToken = default)
    {
        var saveResult = await SaveDraftAsync(
            tripId,
            payloadJson,
            basePlanRevision,
            newPin,
            cancellationToken);
        if (!saveResult.Success)
        {
            return saveResult;
        }

        var trip = await LoadTripGraphAsync(tripId, cancellationToken);
        if (trip?.PlanDraft is null)
        {
            return TripPlanOperationResult.Fail("No hay un borrador para publicar.");
        }

        if (trip.PlanRevision != trip.PlanDraft.BasePlanRevision)
        {
            return TripPlanOperationResult.Fail(
                "La versión publicada cambió. Recargá el editor antes de aplicar.",
                trip.PlanRevision);
        }

        var payload = NormalizePayload(Deserialize(trip.PlanDraft.PayloadJson));
        try
        {
            await ValidatePayloadAsync(payload, strict: true, cancellationToken);
        }
        catch (ValidationException exception)
        {
            return TripPlanOperationResult.Fail(exception.Message, trip.PlanRevision);
        }

        if (string.IsNullOrWhiteSpace(trip.AccessPinHash)
            && string.IsNullOrWhiteSpace(trip.PlanDraft.PendingAccessPinHash))
        {
            return TripPlanOperationResult.Fail("Configurá un PIN antes de publicar.", trip.PlanRevision);
        }

        var publishedRevision = trip.PlanRevision;
        var strategy = dbContext.Database.CreateExecutionStrategy();
        try
        {
            await strategy.ExecuteAsync(async () =>
            {
                dbContext.ChangeTracker.Clear();
                var publishTrip = await LoadTripGraphAsync(tripId, cancellationToken);
                if (publishTrip?.PlanDraft is null
                    || publishTrip.PlanRevision != publishTrip.PlanDraft.BasePlanRevision)
                {
                    throw new DbUpdateConcurrencyException("Trip plan revision changed before publish.");
                }

                await using var transaction = dbContext.Database.IsRelational()
                    ? await dbContext.Database.BeginTransactionAsync(cancellationToken)
                    : null;
                await ApplyPublishedPayloadAsync(publishTrip, payload, cancellationToken);
                if (!string.IsNullOrWhiteSpace(publishTrip.PlanDraft.PendingAccessPinHash))
                {
                    publishTrip.AccessPinHash = publishTrip.PlanDraft.PendingAccessPinHash;
                    publishTrip.AccessPinUpdatedAt = DateTimeOffset.UtcNow;
                }

                publishTrip.PublicationStatus = TripPublicationStatus.Published;
                publishTrip.PlanRevision++;
                publishTrip.PublishedAtUtc = DateTimeOffset.UtcNow;
                publishTrip.UpdatedAtUtc = publishTrip.PublishedAtUtc.Value;
                publishedRevision = publishTrip.PlanRevision;
                dbContext.TripPlanDrafts.Remove(publishTrip.PlanDraft);
                await dbContext.SaveChangesAsync(cancellationToken);
                if (transaction is not null)
                {
                    await transaction.CommitAsync(cancellationToken);
                }
            });
        }
        catch (DbUpdateConcurrencyException)
        {
            return TripPlanOperationResult.Fail(
                "La versión publicada cambió. Recargá el editor antes de aplicar.",
                publishedRevision);
        }

        logger.LogInformation(
            "Trip plan published. TripId={TripId}; Revision={Revision}; Days={DayCount}.",
            tripId,
            publishedRevision,
            payload.Days.Count);
        return TripPlanOperationResult.Ok("Viaje aplicado a la app.", publishedRevision);
    }

    public async Task<bool> DiscardDraftAsync(Guid tripId, CancellationToken cancellationToken = default)
    {
        var trip = await dbContext.Trips
            .Include(item => item.PlanDraft)
            .Include(item => item.AppUser)
            .FirstOrDefaultAsync(item => item.Id == tripId, cancellationToken);
        if (trip is null)
        {
            return true;
        }

        if (trip.PublicationStatus == TripPublicationStatus.Draft)
        {
            var user = trip.AppUser;
            dbContext.Trips.Remove(trip);
            if (user is not null && !await dbContext.Trips.AnyAsync(
                    item => item.Id != trip.Id && item.AppUserId == user.Id,
                    cancellationToken))
            {
                dbContext.AppUsers.Remove(user);
            }

            await dbContext.SaveChangesAsync(cancellationToken);
            return true;
        }

        if (trip.PlanDraft is not null)
        {
            dbContext.TripPlanDrafts.Remove(trip.PlanDraft);
            trip.UpdatedAtUtc = DateTimeOffset.UtcNow;
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return false;
    }

    public string SerializeForPage(TripPlanEditorState state) => JsonSerializer.Serialize(state, JsonOptions);

    private async Task<Trip?> LoadTripGraphAsync(Guid tripId, CancellationToken cancellationToken) =>
        await dbContext.Trips
            .Include(trip => trip.AppUser)
            .Include(trip => trip.PlanDraft)
            .Include(trip => trip.DayPlans)
                .ThenInclude(day => day.Blocks)
            .Include(trip => trip.Reservations)
                .ThenInclude(reservation => reservation.Recommendation)
            .FirstOrDefaultAsync(trip => trip.Id == tripId, cancellationToken);

    private async Task ApplyPublishedPayloadAsync(
        Trip trip,
        TripPlanEditorPayload payload,
        CancellationToken cancellationToken)
    {
        trip.TravelerName = payload.TravelerName.Trim();
        trip.DestinationId = payload.DestinationId;
        trip.StartsOn = payload.StartsOn;
        trip.EndsOn = payload.EndsOn;
        trip.TimeZoneId = payload.TimeZoneId.Trim();
        if (trip.AppUser is not null)
        {
            trip.AppUser.DisplayName = trip.TravelerName;
        }

        var recommendationIds = payload.Days
            .SelectMany(day => day.Blocks)
            .SelectMany(block => block.Recommendations)
            .Select(item => item.RecommendationId)
            .Distinct()
            .ToList();
        var recommendations = await dbContext.Recommendations
            .Where(item => recommendationIds.Contains(item.Id))
            .ToDictionaryAsync(item => item.Id, cancellationToken);

        var existingDays = trip.DayPlans.ToDictionary(day => day.Id);
        var existingBlocks = trip.DayPlans.SelectMany(day => day.Blocks).ToDictionary(block => block.Id);
        var existingReservations = trip.Reservations.ToDictionary(item => item.Id);
        var desiredDayIds = new HashSet<Guid>();
        var desiredBlockIds = new HashSet<Guid>();
        var desiredReservationIds = new HashSet<Guid>();

        foreach (var dayDraft in payload.Days)
        {
            var day = existingDays.GetValueOrDefault(dayDraft.Id) ?? new TripDayPlan
            {
                Id = dayDraft.Id,
                TripId = trip.Id,
                Trip = trip
            };
            if (!existingDays.ContainsKey(day.Id))
            {
                dbContext.TripDayPlans.Add(day);
            }

            desiredDayIds.Add(day.Id);
            day.Date = dayDraft.Date;
            day.DayNumber = dayDraft.DayNumber;
            day.City = dayDraft.City.Trim();
            day.HotelBase = dayDraft.HotelBase.Trim();
            day.BaseLatitude = dayDraft.BaseLatitude;
            day.BaseLongitude = dayDraft.BaseLongitude;
            day.Introduction = dayDraft.Introduction.Trim();

            foreach (var blockDraft in dayDraft.Blocks)
            {
                var period = TripPlanPeriods.Find(blockDraft.PeriodKey)!;
                var block = existingBlocks.GetValueOrDefault(blockDraft.Id) ?? new TripDayBlock
                {
                    Id = blockDraft.Id,
                    TripDayPlanId = day.Id,
                    TripDayPlan = day
                };
                if (!existingBlocks.ContainsKey(block.Id))
                {
                    dbContext.TripDayBlocks.Add(block);
                }

                desiredBlockIds.Add(block.Id);
                block.TripDayPlanId = day.Id;
                block.PeriodKey = period.Key;
                block.SortOrder = period.SortOrder;
                block.CuratedDescription = blockDraft.CuratedDescription.Trim();
                block.AutofillEnabled = blockDraft.AutofillEnabled;

                for (var index = 0; index < blockDraft.Recommendations.Count; index++)
                {
                    var assignment = blockDraft.Recommendations[index];
                    var recommendation = recommendations[assignment.RecommendationId];
                    var startsAt = period.StartsAt.AddMinutes(index * 15);
                    var reservation = existingReservations.GetValueOrDefault(assignment.Id) ?? CreateReservation(trip.Id, assignment.Id);
                    if (!existingReservations.ContainsKey(reservation.Id))
                    {
                        dbContext.Reservations.Add(reservation);
                    }

                    desiredReservationIds.Add(reservation.Id);
                    reservation.TripDayBlockId = block.Id;
                    reservation.TripDayBlock = block;
                    reservation.Trip = trip;
                    reservation.RecommendationId = recommendation.Id;
                    reservation.Type = ReservationType.Event;
                    reservation.PlanningKind = ScheduleItemKind.Recommendation;
                    reservation.Date = day.Date;
                    reservation.StartsAt = startsAt;
                    reservation.EndsAt = startsAt.AddMinutes(Math.Max(30, recommendation.SuggestedDurationMinutes));
                    reservation.TimeZoneId = payload.TimeZoneId;
                    reservation.Title = recommendation.Title;
                    reservation.City = day.City;
                    reservation.LocationName = recommendation.Title;
                    reservation.Address = recommendation.Neighborhood;
                    reservation.ConfirmationCode = string.Empty;
                    reservation.Notes = string.Empty;
                    reservation.Latitude = recommendation.Latitude;
                    reservation.Longitude = recommendation.Longitude;
                    reservation.SourceName = SourceName;
                    reservation.SourceUrl = recommendation.SourceUrl;
                    ClearTravelFields(reservation);
                }

                foreach (var itemDraft in blockDraft.Items)
                {
                    var reservation = existingReservations.GetValueOrDefault(itemDraft.Id) ?? CreateReservation(trip.Id, itemDraft.Id);
                    if (!existingReservations.ContainsKey(reservation.Id))
                    {
                        dbContext.Reservations.Add(reservation);
                    }

                    desiredReservationIds.Add(reservation.Id);
                    ApplyItemDraft(reservation, itemDraft, trip, day, block);
                }
            }
        }

        dbContext.Reservations.RemoveRange(trip.Reservations.Where(item => !desiredReservationIds.Contains(item.Id)).ToList());
        dbContext.TripDayBlocks.RemoveRange(trip.DayPlans.SelectMany(day => day.Blocks).Where(block => !desiredBlockIds.Contains(block.Id)).ToList());
        dbContext.TripDayPlans.RemoveRange(trip.DayPlans.Where(day => !desiredDayIds.Contains(day.Id)).ToList());
    }

    private static Reservation CreateReservation(Guid tripId, Guid reservationId) => new()
    {
        Id = reservationId,
        TripId = tripId,
        Title = string.Empty,
        City = string.Empty,
        LocationName = string.Empty,
        Address = string.Empty,
        ConfirmationCode = string.Empty,
        Notes = string.Empty
    };

    private static void ApplyItemDraft(
        Reservation reservation,
        TripPlanItemDraft draft,
        Trip trip,
        TripDayPlan day,
        TripDayBlock block)
    {
        reservation.TripDayBlockId = block.Id;
        reservation.TripDayBlock = block;
        reservation.Trip = trip;
        reservation.RecommendationId = null;
        reservation.Type = draft.Type;
        reservation.PlanningKind = draft.PlanningKind == ScheduleItemKind.Recommendation
            ? ScheduleItemKind.ManualEvent
            : draft.PlanningKind;
        reservation.Date = day.Date;
        reservation.StartsAt = draft.StartsAt;
        reservation.EndsOn = draft.EndsOn;
        reservation.EndsAt = draft.EndsAt;
        reservation.TimeZoneId = trip.TimeZoneId;
        reservation.Title = draft.Title.Trim();
        reservation.City = string.IsNullOrWhiteSpace(draft.City) ? day.City : draft.City.Trim();
        reservation.LocationName = draft.LocationName.Trim();
        reservation.Address = draft.Address.Trim();
        reservation.ConfirmationCode = draft.ConfirmationCode.Trim();
        reservation.Notes = draft.Notes.Trim();
        reservation.Latitude = draft.Latitude;
        reservation.Longitude = draft.Longitude;
        reservation.Airline = NullIfEmpty(draft.Airline);
        reservation.FlightNumber = NullIfEmpty(draft.FlightNumber);
        reservation.OriginName = NullIfEmpty(draft.OriginName);
        reservation.DestinationName = NullIfEmpty(draft.DestinationName);
        reservation.OriginAirport = NullIfEmpty(draft.OriginAirport);
        reservation.DestinationAirport = NullIfEmpty(draft.DestinationAirport);
        reservation.SourceName = SourceName;
        reservation.SourceUrl = null;
    }

    private static void ClearTravelFields(Reservation reservation)
    {
        reservation.EndsOn = null;
        reservation.Airline = null;
        reservation.FlightNumber = null;
        reservation.OriginName = null;
        reservation.DestinationName = null;
        reservation.OriginAirport = null;
        reservation.DestinationAirport = null;
    }

    private TripPlanEditorPayload BuildPayloadFromPublished(Trip trip)
    {
        var payload = CreatePayload(trip);
        var existingDays = trip.DayPlans.ToDictionary(day => day.Date);
        foreach (var day in payload.Days)
        {
            var existingDay = existingDays.GetValueOrDefault(day.Date);
            var activeLodging = trip.Reservations
                .Where(item => item.Type == ReservationType.Lodging
                    && item.Date <= day.Date
                    && (item.EndsOn ?? item.Date) >= day.Date)
                .OrderByDescending(item => item.Date)
                .FirstOrDefault();
            var itemsForDay = trip.Reservations
                .Where(item => item.Date == day.Date)
                .OrderBy(item => item.StartsAt)
                .ToList();

            if (existingDay is not null)
            {
                day.Id = existingDay.Id;
                day.City = existingDay.City;
                day.HotelBase = existingDay.HotelBase;
                day.BaseLatitude = existingDay.BaseLatitude;
                day.BaseLongitude = existingDay.BaseLongitude;
                day.Introduction = existingDay.Introduction;
            }
            else
            {
                day.City = itemsForDay.Select(item => item.City).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))
                    ?? activeLodging?.City
                    ?? string.Empty;
                day.HotelBase = activeLodging?.LocationName ?? string.Empty;
                day.BaseLatitude = activeLodging?.Latitude;
                day.BaseLongitude = activeLodging?.Longitude;
            }

            foreach (var block in day.Blocks)
            {
                var period = TripPlanPeriods.Find(block.PeriodKey)!;
                var existingBlock = existingDay?.Blocks.FirstOrDefault(item => item.PeriodKey == period.Key);
                if (existingBlock is not null)
                {
                    block.Id = existingBlock.Id;
                    block.CuratedDescription = existingBlock.CuratedDescription;
                    block.AutofillEnabled = existingBlock.AutofillEnabled;
                }

                var blockItems = itemsForDay
                    .Where(item => item.TripDayBlockId == block.Id
                        || (item.TripDayBlockId is null && TripPlanPeriods.Resolve(item.StartsAt).Key == period.Key))
                    .ToList();
                block.Recommendations = blockItems
                    .Where(item => item.PlanningKind == ScheduleItemKind.Recommendation && item.RecommendationId.HasValue)
                    .Select(item => new TripPlanRecommendationDraft
                    {
                        Id = item.Id,
                        RecommendationId = item.RecommendationId!.Value
                    })
                    .Take(3)
                    .ToList();
                block.Items = blockItems
                    .Where(item => item.PlanningKind != ScheduleItemKind.Recommendation)
                    .Select(ToItemDraft)
                    .ToList();
                if (string.IsNullOrWhiteSpace(block.CuratedDescription))
                {
                    block.CuratedDescription = blockItems
                        .Select(item => ExtractLegacyCuratedDescription(item.Notes))
                        .FirstOrDefault(value => value.Length > 0) ?? string.Empty;
                }
            }
        }

        return payload;
    }

    private static TripPlanItemDraft ToItemDraft(Reservation item) => new()
    {
        Id = item.Id,
        Type = item.Type,
        PlanningKind = item.PlanningKind,
        StartsAt = item.StartsAt,
        EndsOn = item.EndsOn,
        EndsAt = item.EndsAt,
        Title = item.Title,
        City = item.City,
        LocationName = item.LocationName,
        Address = item.Address,
        ConfirmationCode = item.ConfirmationCode,
        Notes = item.Notes,
        Latitude = item.Latitude,
        Longitude = item.Longitude,
        Airline = item.Airline,
        FlightNumber = item.FlightNumber,
        OriginName = item.OriginName,
        DestinationName = item.DestinationName,
        OriginAirport = item.OriginAirport,
        DestinationAirport = item.DestinationAirport
    };

    private static TripPlanEditorPayload CreatePayload(Trip trip) => new()
    {
        TravelerName = trip.TravelerName,
        DestinationId = trip.DestinationId,
        StartsOn = trip.StartsOn,
        EndsOn = trip.EndsOn,
        TimeZoneId = trip.TimeZoneId,
        Days = Enumerable.Range(0, trip.EndsOn.DayNumber - trip.StartsOn.DayNumber + 1)
            .Select(index => CreateDay(trip.StartsOn.AddDays(index), index + 1))
            .ToList()
    };

    private static TripPlanDayDraft CreateDay(DateOnly date, int dayNumber) => new()
    {
        Id = Guid.NewGuid(),
        Date = date,
        DayNumber = dayNumber,
        Blocks = TripPlanPeriods.All.Select(period => new TripPlanBlockDraft
        {
            Id = Guid.NewGuid(),
            PeriodKey = period.Key,
            AutofillEnabled = true
        }).ToList()
    };

    private static TripPlanEditorPayload NormalizePayload(TripPlanEditorPayload payload)
    {
        ValidateDateRange(payload.StartsOn, payload.EndsOn);
        var sourceDays = payload.Days
            .GroupBy(day => day.Date)
            .ToDictionary(group => group.Key, group => group.First());
        var normalizedDays = new List<TripPlanDayDraft>();
        var dayNumber = 1;
        for (var date = payload.StartsOn; date <= payload.EndsOn; date = date.AddDays(1))
        {
            var day = sourceDays.GetValueOrDefault(date) ?? CreateDay(date, dayNumber);
            day.Id = day.Id == Guid.Empty ? Guid.NewGuid() : day.Id;
            day.Date = date;
            day.DayNumber = dayNumber++;
            day.City = (day.City ?? string.Empty).Trim();
            day.HotelBase = (day.HotelBase ?? string.Empty).Trim();
            day.Introduction = (day.Introduction ?? string.Empty).Trim();
            var sourceBlocks = day.Blocks
                .Where(block => TripPlanPeriods.Find(block.PeriodKey) is not null)
                .GroupBy(block => block.PeriodKey, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
            day.Blocks = TripPlanPeriods.All.Select(period =>
            {
                var block = sourceBlocks.GetValueOrDefault(period.Key) ?? new TripPlanBlockDraft
                {
                    Id = Guid.NewGuid(),
                    PeriodKey = period.Key,
                    AutofillEnabled = true
                };
                block.Id = block.Id == Guid.Empty ? Guid.NewGuid() : block.Id;
                block.PeriodKey = period.Key;
                block.CuratedDescription = (block.CuratedDescription ?? string.Empty).Trim();
                block.Recommendations ??= [];
                block.Items ??= [];
                foreach (var assignment in block.Recommendations)
                {
                    assignment.Id = assignment.Id == Guid.Empty ? Guid.NewGuid() : assignment.Id;
                }

                foreach (var item in block.Items)
                {
                    item.Id = item.Id == Guid.Empty ? Guid.NewGuid() : item.Id;
                    item.Title = (item.Title ?? string.Empty).Trim();
                    item.City = (item.City ?? string.Empty).Trim();
                    item.LocationName = (item.LocationName ?? string.Empty).Trim();
                    item.Address = (item.Address ?? string.Empty).Trim();
                    item.ConfirmationCode = (item.ConfirmationCode ?? string.Empty).Trim();
                    item.Notes = (item.Notes ?? string.Empty).Trim();
                }

                return block;
            }).ToList();
            normalizedDays.Add(day);
        }

        var inheritedHotelBase = string.Empty;
        foreach (var day in normalizedDays)
        {
            if (!string.IsNullOrWhiteSpace(day.HotelBase))
            {
                inheritedHotelBase = day.HotelBase;
            }
            else if (inheritedHotelBase.Length > 0)
            {
                day.HotelBase = inheritedHotelBase;
            }
        }

        payload.TravelerName = (payload.TravelerName ?? string.Empty).Trim();
        payload.TimeZoneId = (payload.TimeZoneId ?? string.Empty).Trim();
        payload.Days = normalizedDays;
        return payload;
    }

    private static IReadOnlyList<CreateTripCitySegment> NormalizeCitySegments(
        IReadOnlyList<CreateTripCitySegment>? segments,
        DateOnly startsOn,
        DateOnly endsOn)
    {
        if (segments is null || segments.Count == 0)
        {
            return [];
        }

        var normalized = segments
            .Select(segment => new CreateTripCitySegment(
                (segment.City ?? string.Empty).Trim(),
                segment.StartsOn,
                segment.EndsOn,
                string.IsNullOrWhiteSpace(segment.HotelBase) ? null : segment.HotelBase.Trim()))
            .OrderBy(segment => segment.StartsOn)
            .ToList();

        for (var index = 0; index < normalized.Count; index++)
        {
            var segment = normalized[index];
            if (segment.City.Length == 0)
            {
                throw new ValidationException($"Completá la ciudad del tramo {index + 1}.");
            }

            if (segment.StartsOn > segment.EndsOn)
            {
                throw new ValidationException($"Las fechas de {segment.City} no son válidas.");
            }

            if (segment.StartsOn < startsOn || segment.EndsOn > endsOn)
            {
                throw new ValidationException($"El tramo de {segment.City} queda fuera de las fechas del viaje.");
            }

            var expectedStart = index == 0 ? startsOn : normalized[index - 1].EndsOn.AddDays(1);
            if (segment.StartsOn != expectedStart)
            {
                throw new ValidationException("Los tramos de ciudad deben cubrir todos los días, sin huecos ni fechas superpuestas.");
            }
        }

        if (normalized[^1].EndsOn != endsOn)
        {
            throw new ValidationException("Los tramos de ciudad deben llegar hasta el último día del viaje.");
        }

        return normalized;
    }

    private static void ApplyCitySegments(
        TripPlanEditorPayload payload,
        IReadOnlyList<CreateTripCitySegment> segments)
    {
        foreach (var day in payload.Days)
        {
            var segment = segments.FirstOrDefault(item => day.Date >= item.StartsOn && day.Date <= item.EndsOn);
            if (segment is null)
            {
                continue;
            }

            day.City = segment.City;
            day.HotelBase = segment.HotelBase ?? string.Empty;
        }
    }

    private async Task ValidatePayloadAsync(
        TripPlanEditorPayload payload,
        bool strict,
        CancellationToken cancellationToken)
    {
        if (payload.TravelerName.Length == 0)
        {
            throw new ValidationException("El nombre del cliente es obligatorio.");
        }

        if (payload.TimeZoneId.Length == 0)
        {
            throw new ValidationException("La zona horaria es obligatoria.");
        }

        if (!await dbContext.Destinations.AnyAsync(item => item.Id == payload.DestinationId, cancellationToken))
        {
            throw new ValidationException("El destino seleccionado ya no existe.");
        }

        var recommendationIds = payload.Days
            .SelectMany(day => day.Blocks)
            .SelectMany(block => block.Recommendations)
            .Select(item => item.RecommendationId)
            .ToList();
        if (recommendationIds.Count != recommendationIds.Distinct().Count())
        {
            throw new ValidationException("Una recomendación no puede repetirse dentro del viaje.");
        }

        var validRecommendationCount = await dbContext.Recommendations.CountAsync(
            item => item.DestinationId == payload.DestinationId && recommendationIds.Contains(item.Id),
            cancellationToken);
        if (validRecommendationCount != recommendationIds.Count)
        {
            throw new ValidationException("Una o más recomendaciones no pertenecen al destino seleccionado.");
        }

        foreach (var day in payload.Days)
        {
            if (strict && day.City.Length == 0)
            {
                throw new ValidationException($"Completá la ciudad del día {day.DayNumber}.");
            }

            ValidateCoordinates(day.BaseLatitude, day.BaseLongitude, $"día {day.DayNumber}");
            foreach (var block in day.Blocks)
            {
                if (block.Recommendations.Count > 3)
                {
                    throw new ValidationException($"{day.DayNumber} · {block.PeriodKey} admite hasta 3 recomendaciones.");
                }

                foreach (var item in block.Items)
                {
                    if (strict && item.Title.Length == 0)
                    {
                        throw new ValidationException($"Hay un elemento sin título en el día {day.DayNumber}.");
                    }

                    if (strict && item.Type != ReservationType.Flight && item.LocationName.Length == 0)
                    {
                        throw new ValidationException($"Completá el lugar de '{item.Title}' en el día {day.DayNumber}.");
                    }

                    if (strict && item.Type == ReservationType.Flight
                        && (string.IsNullOrWhiteSpace(item.FlightNumber)
                            || string.IsNullOrWhiteSpace(item.OriginName)
                            || string.IsNullOrWhiteSpace(item.DestinationName)))
                    {
                        throw new ValidationException($"Completá vuelo, origen y destino de '{item.Title}'.");
                    }

                    if (strict && item.Type == ReservationType.Lodging && item.EndsOn is null)
                    {
                        throw new ValidationException($"Completá el checkout de '{item.Title}'.");
                    }

                    ValidateCoordinates(item.Latitude, item.Longitude, item.Title.Length > 0 ? item.Title : "elemento");
                }
            }
        }
    }

    private async Task<bool> IsPinAvailableAsync(
        string pin,
        Guid? exceptTripId,
        CancellationToken cancellationToken)
    {
        var trips = await dbContext.Trips
            .AsNoTracking()
            .Include(item => item.PlanDraft)
            .Where(item => !exceptTripId.HasValue || item.Id != exceptTripId.Value)
            .ToListAsync(cancellationToken);
        return trips.All(trip =>
            (string.IsNullOrWhiteSpace(trip.AccessPinHash)
                || tripPinHasher.VerifyHashedPassword(trip, trip.AccessPinHash, pin) == PasswordVerificationResult.Failed)
            && (string.IsNullOrWhiteSpace(trip.PlanDraft?.PendingAccessPinHash)
                || tripPinHasher.VerifyHashedPassword(trip, trip.PlanDraft.PendingAccessPinHash, pin) == PasswordVerificationResult.Failed));
    }

    private static void ValidateDateRange(DateOnly startsOn, DateOnly endsOn)
    {
        if (endsOn < startsOn)
        {
            throw new ValidationException("La fecha final no puede ser anterior al inicio.");
        }

        if (endsOn.DayNumber - startsOn.DayNumber > 90)
        {
            throw new ValidationException("El editor admite viajes de hasta 91 días.");
        }
    }

    private static void ValidatePinFormat(string pin)
    {
        if (pin.Length != 4 || pin.Any(character => !char.IsDigit(character)))
        {
            throw new ValidationException("El PIN debe tener exactamente 4 números.");
        }

        if (pin == Options.FreePreviewOptions.ReservedPin)
        {
            throw new ValidationException("El PIN 0000 está reservado para el mapa gratuito.");
        }
    }

    private static void ValidateCoordinates(decimal? latitude, decimal? longitude, string label)
    {
        if (latitude is < -90 or > 90 || longitude is < -180 or > 180)
        {
            throw new ValidationException($"Las coordenadas de {label} no son válidas.");
        }

        if (latitude.HasValue != longitude.HasValue)
        {
            throw new ValidationException($"Completá latitud y longitud de {label}, o dejá ambas vacías.");
        }
    }

    private static string ExtractLegacyCuratedDescription(string notes)
    {
        const string prefix = "Descripcion:";
        var index = notes.IndexOf(prefix, StringComparison.OrdinalIgnoreCase);
        if (index < 0)
        {
            return string.Empty;
        }

        var value = notes[(index + prefix.Length)..];
        var separator = value.IndexOf(" | ", StringComparison.Ordinal);
        return (separator >= 0 ? value[..separator] : value).Trim();
    }

    private static string Serialize(TripPlanEditorPayload payload) => JsonSerializer.Serialize(payload, JsonOptions);

    private static TripPlanEditorPayload Deserialize(string json) =>
        JsonSerializer.Deserialize<TripPlanEditorPayload>(json, JsonOptions)
        ?? throw new JsonException("El borrador está vacío o no tiene un formato válido.");

    private static string NormalizePin(string value) => value.Trim();
    private static string? NullIfEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

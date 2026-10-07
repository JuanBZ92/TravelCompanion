using System.Data;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TravelCompanion.Api.Data;
using TravelCompanion.Api.Models;
using TravelCompanion.Shared;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Api.Services;

public sealed class DayPlanService(TravelCompanionDbContext db,
    FreeTrialAccessService freeTrial, AssistantUsageService usage, IUserProfileService profiles,
    TravelChatService chat, TravelRecommendationPlanningService planning, ILogger<DayPlanService>? logger = null,
    ProductAnalyticsService? analytics = null)
{
    public async Task<DayPlanOptionsDto> OptionsAsync(TravelerAccessContext access, CancellationToken ct)
    {
        var trip = await db.Trips.AsNoTracking().Include(item => item.Destination).FirstOrDefaultAsync(item => item.Id == access.TripId
            && item.AppUserId == access.User.Id && !item.IsArchived && item.PublicationStatus == TripPublicationStatus.Published, ct);
        var enabled = access.Capabilities.CanEditItinerary && trip is not null
            && access.Session.AccessMode is SessionAccessMode.Builder or SessionAccessMode.FreeMapPreview;
        if (enabled)
            await RequireCurrentGrantAsync(access, trip!.Id, ct);
        var trial = await TrialStatusAsync(access, ct);
        var profile = await profiles.GetProfileDtoAsync(access.User.Id, ct);
        if (trip is not null)
            trip.Reservations = await db.Reservations.AsNoTracking().Where(item => item.TripId == trip.Id)
                .Select(item => new Reservation
                {
                    Date = item.Date, EndsOn = item.EndsOn, City = item.City, Title = string.Empty,
                    LocationName = string.Empty, Address = string.Empty, ConfirmationCode = string.Empty, Notes = string.Empty
                }).ToListAsync(ct);
        return new(enabled, trip?.Id,
            trip?.PlanRevision ?? 0, trip?.StartsOn, trip?.EndsOn,
            IsTrial(access) ? [1, 3] : [1, 3, 5, 7], profile, trial)
        {
            CityDays = trip is null ? [] : TravelChatService.GetDayPlanCityDays(trip)
        };
    }

    public async Task<DayPlanResponse> GenerateAsync(TravelerAccessContext access, DayPlanRequest request,
        CancellationToken ct)
    {
        if (DbExecutionStrategy.ShouldExecute(db))
            return await DbExecutionStrategy.ExecuteAsync(db, () => GenerateAsync(access, request, ct), ct);
        using var operation = DatabaseOperation.Begin("day_planning", logger);
        RequireAccess(access, request.TripId);
        Validate(request);
        await RequireCurrentGrantAsync(access, request.TripId, ct);
        var operationKey = OperationKey(request.OperationId);
        var hash = Hash(request);
        var previous = await db.AssistantUsageLeases.AsNoTracking().Where(item => item.OperationKey == operationKey
            && item.BuilderAccessGrant!.AppUserId == access.User.Id && item.BuilderAccessGrant.TripId == request.TripId)
            .OrderByDescending(item => item.CreatedAtUtc).FirstOrDefaultAsync(ct);
        if (previous is not null && previous.RequestHash != hash)
            throw new DayPlanException(409, "operation", "Esta operación ya se utilizó con otros parámetros.");
        if (previous?.ResponseJson is { } previousJson)
            return Read<DayPlanResponse>(previousJson) with { TrialAccess = await TrialStatusAsync(access, ct) };
        var trip = await LoadTripAsync(access, request.TripId, ct);
        RequireRevision(trip, request.ExpectedRevision);
        var last = request.StartDate.AddDays(request.DayCount - 1);
        if (request.StartDate < trip.StartsOn || last > trip.EndsOn)
            throw new DayPlanException(400, "selection", "Elegí días que estén dentro de tu viaje.");
        if (IsTrial(access))
        {
            if (request.DayCount > 3 || !FreePlanningPolicy.CanPlanDate(trip.StartsOn, request.StartDate)
                || !FreePlanningPolicy.CanPlanDate(trip.StartsOn, last))
                throw new DayPlanException(403, "upgrade", "La prueba permite planificar los tres primeros días. Activá el pase para más días.");
            await freeTrial.RequireEditingAsync(access.User.Id, false, ct);
        }
        var profile = EffectiveProfile(await profiles.GetProfileAsync(access.User.Id, ct), access.User.Id, request.Preferences);
        AssistantUsageLeaseResult lease;
        try { lease = await usage.ReserveAsync(access.User.Id, request.TripId, operationKey, ct, hash); }
        catch (TrialUpgradeRequiredException)
        {
            throw new DayPlanException(403, "quota", IsTrial(access)
                ? "Usaste las tres generaciones gratuitas. Activá el pase para continuar."
                : "Alcanzaste el límite diario. Se renueva a las 00:00 UTC.");
        }
        if (lease.IsTrial != IsTrial(access))
        {
            await usage.CancelAsync(lease.LeaseId, CancellationToken.None);
            throw new DayPlanException(403, "access", "El acceso de tu sesión cambió. Seleccioná otra vez el viaje.");
        }
        try
        {
            var response = await chat.GenerateDayPlansAsync(access.User, request, trip, profile, planning, ct);
            operation.Rows = response.Days.Sum(day => day.Stops.Count);
            var state = new DayPlanProposalState
            {
                Preferences = new(profile.TravelPace, profile.BudgetLevel, profile.Interests.ToList()),
                Locale = request.Locale,
                SeenRecommendationIds = response.Days.SelectMany(day => day.Stops).Select(stop => stop.RecommendationId).ToHashSet()
            };
            var json = await usage.CompletePlanAsync(lease.LeaseId, trip.Id, trip.PlanRevision,
                state.Serialize(response), ct, response.Days.Any(day => day.Stops.Count > 0));
            if (response.Days.Any(day => day.Stops.Count > 0))
                await TrackSafelyAsync(access.User.Id, trip.Id, "first_useful_response", request.OperationId);
            return Read<DayPlanResponse>(json) with { TrialAccess = await TrialStatusAsync(access, ct) };
        }
        catch
        {
            db.ChangeTracker.Clear();
            await usage.CancelAsync(lease.LeaseId, CancellationToken.None);
            throw;
        }
    }

    public async Task<DayPlanReplaceResponse> ReplaceAsync(TravelerAccessContext access, DayPlanReplaceRequest request,
        CancellationToken ct)
    {
        if (DbExecutionStrategy.ShouldExecute(db))
            return await DbExecutionStrategy.ExecuteAsync(db, () => ReplaceAsync(access, request, ct), ct);
        using var operation = DatabaseOperation.Begin("day_plan_replace", logger);
        RequireAccess(access, request.TripId);
        if (request.OperationId == Guid.Empty || request.MutationId == Guid.Empty || request.StopId == Guid.Empty
            || request.ExpectedRevision < 0 || request.ExpectedProposalRevision < 0 || request.Locale?.Length > 20)
            throw new DayPlanException(400, "selection", "Elegí una propuesta válida para buscar otra opción.");
        if (IsTrial(access)) await freeTrial.RequireEditingAsync(access.User.Id, false, ct);
        await using var transaction = db.Database.IsRelational()
            ? await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct) : null;
        // Apply and replacement share the trip lock: the persisted selection can never
        // change underneath an application, and only one proposal revision can advance.
        try { await TripConcurrencyLock.LockAsync(db, request.TripId, ct); }
        catch (KeyNotFoundException)
        {
            throw new DayPlanException(409, "stale", "El viaje ya no está disponible. Actualizá la sesión.");
        }
        await RequireCurrentGrantAsync(access, request.TripId, ct);
        var lease = await db.AssistantUsageLeases.Where(item => item.OperationKey == OperationKey(request.OperationId)
            && item.BuilderAccessGrant!.AppUserId == access.User.Id && item.BuilderAccessGrant.TripId == request.TripId
            && item.CompletedAtUtc != null && item.ResponseJson != null)
            .OrderByDescending(item => item.CreatedAtUtc).FirstOrDefaultAsync(ct);
        if (lease is null)
            throw new DayPlanException(400, "selection", "La propuesta no está disponible. Generá otra antes de buscar alternativas.");
        await db.Entry(lease).ReloadAsync(ct);
        var proposal = Read<DayPlanResponse>(lease.ResponseJson!);
        var state = DayPlanProposalState.Read(lease.ResponseJson!, proposal);
        var hash = Hash(request);
        var previous = state.Replacements.SingleOrDefault(item => item.MutationId == request.MutationId);
        if (previous is not null)
        {
            if (previous.RequestHash != hash)
                throw new DayPlanException(409, "operation", "Esta operación ya se utilizó con otra alternativa.");
            if (transaction is not null) { await transaction.CommitAsync(ct); await transaction.DisposeAsync(); }
            return new(previous.Replaced, previous.Code, previous.Message,
                state.Snapshot(proposal, previous.ProposalRevision) with { TrialAccess = await TrialStatusAsync(access, ct) });
        }
        var trip = await LoadTripCoreAsync(access.User.Id, request.TripId, ct, readOnly: true);
        RequireRevision(trip, request.ExpectedRevision);
        if (proposal.TripId != trip.Id || proposal.ProposalRevision != request.ExpectedProposalRevision)
            throw new DayPlanException(409, "stale", "La propuesta cambió. Recuperala antes de buscar otra opción.");
        var day = proposal.Days.SingleOrDefault(item => item.Stops.Any(stop => stop.Id == request.StopId));
        var target = day?.Stops.SingleOrDefault(stop => stop.Id == request.StopId);
        if (day is null || target is null || day.Date < trip.StartsOn || day.Date > trip.EndsOn)
            throw new DayPlanException(400, "selection", "Esta idea ya no pertenece a la propuesta actual.");
        if (IsTrial(access) && !FreePlanningPolicy.CanPlanDate(trip.StartsOn, day.Date))
            throw new DayPlanException(403, "upgrade", "Activá el pase para planificar después del tercer día.");
        if (state.AppliedStopIds.Contains(target.Id) || trip.Reservations.Any(item => item.ClientMutationId == target.Id
            || item.RecommendationId == target.RecommendationId))
            throw new DayPlanException(400, "selection", "Esta idea ya está guardada. Podés buscar alternativas para las otras ideas.");
        var cities = TravelChatService.GetDayPlanCityDays(trip).Single(item => item.Date == day.Date).Cities;
        if (!cities.SequenceEqual(day.Cities, StringComparer.OrdinalIgnoreCase))
            throw new DayPlanException(409, "stale", "Las ciudades del viaje cambiaron. Generá una propuesta actualizada.");
        if (request.OriginalRequest is { } original
            && (original.OperationId != request.OperationId || original.TripId != request.TripId || Hash(original) != lease.RequestHash))
            throw new DayPlanException(409, "operation", "Recuperá la solicitud original de esta propuesta para continuar.");
        if (state.Preferences is null)
        {
            if (request.OriginalRequest?.Preferences is not { } preferences)
                throw new DayPlanException(409, "operation", "Esta propuesta necesita recuperar sus preferencias originales o generar otra.");
            state.Preferences = preferences;
            state.Locale = request.OriginalRequest.Locale;
        }
        var profile = EffectiveProfile(await profiles.GetProfileAsync(access.User.Id, ct), access.User.Id, state.Preferences);
        var locale = request.Locale ?? state.Locale ?? "es";
        var replacement = await chat.ReplaceDayPlanStopAsync(access.User, request.OperationId, day, target, trip,
            profile, planning, state.SeenRecommendationIds, state.SeenTitles, state.SeenProviderPlaceIds, locale, ct);
        // Access can be revoked while ranking/catalog I/O is in progress; reject before persisting.
        await RequireCurrentGrantAsync(access, request.TripId, ct);
        if (!await db.Trips.AsNoTracking().AnyAsync(item => item.Id == trip.Id && item.PlanRevision == request.ExpectedRevision, ct))
            throw new DayPlanException(409, "stale", "El itinerario cambió. Actualizá el viaje antes de continuar.");
        var replaced = replacement is not null;
        if (replaced)
        {
            state.SeenRecommendationIds.Add(replacement!.RecommendationId);
            state.SeenTitles.Add(replacement.Card.Title.Trim());
            if (!string.IsNullOrWhiteSpace(replacement.Card.ProviderPlaceId))
                state.SeenProviderPlaceIds.Add(replacement.Card.ProviderPlaceId.Trim());
            proposal = proposal with
            {
                ProposalRevision = proposal.ProposalRevision + 1,
                Days = proposal.Days.Select(item => item.Date == day.Date
                    ? item with { Stops = item.Stops.Select(stop => stop.Id == target.Id ? replacement : stop).ToList() } : item).ToList()
            };
        }
        var english = locale.StartsWith("en", StringComparison.OrdinalIgnoreCase);
        var code = replaced ? "replaced" : "no_alternative";
        var message = replaced
            ? english ? "Another idea is ready. The rest of your proposal is unchanged." : "Otra idea lista. El resto de tu propuesta sigue igual."
            : english ? "No different suitable option is available for this moment. Your idea is unchanged."
                : "No hay otra opción compatible para este momento. Conservamos tu idea.";
        state.Replacements.Add(new(request.MutationId, hash, replaced, code, message, proposal.ProposalRevision,
            day.Date, replaced ? target : null, replacement));
        lease.ResponseJson = state.Serialize(proposal);
        await db.SaveChangesAsync(ct);
        if (transaction is not null) { await transaction.CommitAsync(ct); await transaction.DisposeAsync(); }
        operation.Rows = replaced ? 1 : 0;
        return new(replaced, code, message, proposal with { TrialAccess = await TrialStatusAsync(access, ct) });
    }

    public async Task<DayPlanApplyResponse> ApplyAsync(TravelerAccessContext access, DayPlanApplyRequest request,
        CancellationToken ct)
    {
        if (DbExecutionStrategy.ShouldExecute(db))
            return await DbExecutionStrategy.ExecuteAsync(db, () => ApplyAsync(access, request, ct), ct);
        using var operation = DatabaseOperation.Begin("day_plan_apply", logger);
        RequireAccess(access, request.TripId);
        if (request.OperationId == Guid.Empty || request.MutationId == Guid.Empty || request.ExpectedRevision < 0
            || request.Locale?.Length > 20
            || request.SelectedStopIds is null || request.SelectedStopIds.Count is < 1 or > 35
            || request.SelectedStopIds.Any(id => id == Guid.Empty)
            || request.SelectedStopIds.Distinct().Count() != request.SelectedStopIds.Count)
            throw new DayPlanException(400, "selection", "Elegí al menos una propuesta válida para guardar.");
        if (IsTrial(access)) await freeTrial.RequireEditingAsync(access.User.Id, false, ct);
        // Sort selection for identity; presentation order does not create a different mutation.
        var hash = Hash(request with { SelectedStopIds = request.SelectedStopIds.Order().ToList() });
        await using var transaction = db.Database.IsRelational()
            ? await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct) : null;
        await TripConcurrencyLock.LockAsync(db, request.TripId, ct);
        var now = DateTimeOffset.UtcNow;
        var currentGrant = await CurrentGrantQuery(access.User.Id, request.TripId, now).FirstOrDefaultAsync(ct);
        if (AccessGrantPolicy.ResolveState(currentGrant, now) is not (TrialAccessState.Editing or TrialAccessState.Paid))
            throw new DayPlanException(403, "upgrade", "El acceso al viaje cambió. Actualizá la sesión para continuar.");
        if (currentGrant!.IsTrial != IsTrial(access))
            throw new DayPlanException(403, "access", "El acceso de tu sesión cambió. Seleccioná otra vez el viaje.");
        var prior = await db.PlanningApplicationReceipts.AsNoTracking().SingleOrDefaultAsync(item =>
            item.TripId == request.TripId && item.AppUserId == access.User.Id && item.MutationId == request.MutationId, ct);
        if (prior is not null)
        {
            if (prior.RequestHash != hash)
                throw new DayPlanException(409, "operation", "Esta operación de guardado ya se utilizó con otra selección.");
            if (transaction is not null) await transaction.CommitAsync(ct);
            return Read<DayPlanApplyResponse>(prior.ResultJson) with { TrialAccess = await TrialStatusAsync(access, ct) };
        }
        var trip = await LoadTripAsync(access, request.TripId, ct);
        RequireRevision(trip, request.ExpectedRevision);
        var lease = await db.AssistantUsageLeases.Where(item => item.OperationKey == OperationKey(request.OperationId)
            && item.BuilderAccessGrant!.AppUserId == access.User.Id && item.BuilderAccessGrant.TripId == trip.Id
            && item.CompletedAtUtc != null && item.ResponseJson != null)
            .OrderByDescending(item => item.CreatedAtUtc).FirstOrDefaultAsync(ct);
        if (lease is null) throw new DayPlanException(400, "selection", "La propuesta no está disponible. Generá otra antes de guardar.");
        await db.Entry(lease).ReloadAsync(ct);
        var proposal = Read<DayPlanResponse>(lease.ResponseJson!);
        var selected = proposal.Days.SelectMany(day => day.Stops.Select(stop => (day.Date, day.Cities, Stop: stop)))
            .Where(item => request.SelectedStopIds.Contains(item.Stop.Id)).ToList();
        if (proposal.TripId != trip.Id || selected.Count != request.SelectedStopIds.Count
            || selected.Any(item => item.Date < trip.StartsOn || item.Date > trip.EndsOn))
            throw new DayPlanException(400, "selection", "La selección contiene propuestas que no pertenecen a este viaje.");
        if (IsTrial(access) && selected.Any(item => !FreePlanningPolicy.CanPlanDate(trip.StartsOn, item.Date)))
            throw new DayPlanException(403, "upgrade", "Activá el pase para guardar planes después del tercer día.");
        var ids = selected.Select(item => item.Stop.RecommendationId).ToHashSet();
        var catalog = (await planning.LoadUnlockedRecommendationsAsync(access.User, [trip.DestinationId], string.Empty, ct, ids))
            .ToDictionary(item => item.Id);
        if (catalog.Count != ids.Count)
            throw new DayPlanException(409, "stale", "Algún lugar ya no está disponible. Actualizá la propuesta.");
        var saved = new List<ScheduleItemDto>();
        var firstTravelerItem = !trip.Reservations.Any(item => item.Owner == ItineraryItemOwner.Traveler);
        var created = 0;
        foreach (var (date, cities, stop) in selected)
        {
            ct.ThrowIfCancellationRequested();
            var recommendation = catalog[stop.RecommendationId];
            // Reuse tracked references; attaching a second catalog instance can collide with existing plans.
            recommendation = db.Recommendations.Local.FirstOrDefault(item => item.Id == recommendation.Id) ?? recommendation;
            db.Entry(recommendation).State = EntityState.Unchanged;
            var existing = trip.Reservations.FirstOrDefault(item => item.ClientMutationId == stop.Id
                || item.RecommendationId == recommendation.Id
                || string.Equals(item.Title, recommendation.Title, StringComparison.OrdinalIgnoreCase));
            if (existing is not null) { saved.Add(TravelerItineraryService.ToDto(existing)); continue; }
            var period = TripPlanPeriods.Find(stop.PeriodKey)
                ?? throw new DayPlanException(400, "selection", "Momento del día inválido.");
            var day = trip.DayPlans.FirstOrDefault(item => item.Date == date);
            if (day is null)
            {
                day = new TripDayPlan { Id = Guid.NewGuid(), TripId = trip.Id, Trip = trip, Date = date,
                    DayNumber = date.DayNumber - trip.StartsOn.DayNumber + 1, City = cities.FirstOrDefault() ?? trip.Destination!.Name };
                trip.DayPlans.Add(day);
            }
            var block = day.Blocks.FirstOrDefault(item => item.PeriodKey == period.Key);
            if (block is null)
            {
                block = new TripDayBlock { Id = Guid.NewGuid(), TripDayPlanId = day.Id, TripDayPlan = day,
                    PeriodKey = period.Key, SortOrder = period.SortOrder };
                day.Blocks.Add(block);
            }
            var item = new Reservation
            {
                Id = Guid.NewGuid(), ClientMutationId = stop.Id, TripId = trip.Id, Trip = trip,
                RecommendationId = recommendation.Id, Recommendation = recommendation,
                TripDayBlockId = block.Id, TripDayBlock = block, Type = ReservationType.Event,
                PlanningKind = ScheduleItemKind.Recommendation, Owner = ItineraryItemOwner.Traveler,
                ItemSource = ItineraryItemSource.YukuRecommendation, TimePrecision = ItineraryTimePrecision.PeriodOnly,
                Flexibility = ItineraryFlexibility.Flexible, DurationMinutes = recommendation.SuggestedDurationMinutes,
                Date = date, StartsAt = period.StartsAt, Title = recommendation.Title,
                City = cities.FirstOrDefault(city => recommendation.Neighborhood.Contains(city, StringComparison.OrdinalIgnoreCase)
                    || recommendation.Description.Contains(city, StringComparison.OrdinalIgnoreCase)) ?? day.City,
                LocationName = recommendation.Title, Address = recommendation.Neighborhood,
                ConfirmationCode = "AI-PLAN", Notes = string.Empty, ProviderPlaceId = recommendation.ProviderPlaceId,
                Latitude = recommendation.Latitude, Longitude = recommendation.Longitude, SourceName = "Yuku",
                TimeZoneId = trip.TimeZoneId, SortOrder = block.Reservations.Count
            };
            trip.Reservations.Add(item);
            db.Reservations.Add(item);
            db.RecommendationInteractionSignals.Add(new RecommendationInteractionSignal
            {
                Id = Guid.NewGuid(), UserId = access.User.Id, TripId = trip.Id, RecommendationId = recommendation.Id,
                Signal = RecommendationSignal.Saved, Source = "day_plan_apply", CreatedAtUtc = DateTimeOffset.UtcNow,
                OccurredAtUtc = DateTimeOffset.UtcNow
            });
            saved.Add(TravelerItineraryService.ToDto(item));
            created++;
        }
        if (created > 0) { trip.PlanRevision++; trip.UpdatedAtUtc = DateTimeOffset.UtcNow; }
        var response = new DayPlanApplyResponse(true, request.Locale?.StartsWith("en", StringComparison.OrdinalIgnoreCase) == true
            ? "Ideas added to your trip." : "Planes añadidos al viaje.", trip.Id, trip.PlanRevision, saved);
        db.PlanningApplicationReceipts.Add(new PlanningApplicationReceipt
        {
            Id = Guid.NewGuid(), TripId = trip.Id, AppUserId = access.User.Id, MutationId = request.MutationId,
            RequestHash = hash, ResultJson = JsonSerializer.Serialize(response), CreatedAtUtc = DateTimeOffset.UtcNow
        });
        var proposalState = DayPlanProposalState.Read(lease.ResponseJson!, proposal);
        proposalState.AppliedStopIds.UnionWith(request.SelectedStopIds);
        lease.ResponseJson = proposalState.Serialize(proposal);
        await db.SaveChangesAsync(ct);
        if (transaction is not null) await transaction.CommitAsync(ct);
        if (transaction is not null) await transaction.DisposeAsync();
        operation.Rows = saved.Count;
        if (created > 0 && firstTravelerItem)
            await TrackSafelyAsync(access.User.Id, trip.Id, "first_item_saved", trip.Id);
        return response with { TrialAccess = await TrialStatusAsync(access, ct) };
    }

    private Task<Trip> LoadTripAsync(TravelerAccessContext access, Guid tripId, CancellationToken ct) =>
        LoadTripCoreAsync(access.User.Id, tripId, ct);

    private async Task<Trip> LoadTripCoreAsync(Guid userId, Guid tripId, CancellationToken ct, bool readOnly = false)
    {
        var query = db.Trips.AsSplitQuery().Include(item => item.Destination)
            .Include(item => item.Reservations).ThenInclude(item => item.Recommendation)
            .Include(item => item.Reservations).ThenInclude(item => item.TripDayBlock)
            .Include(item => item.DayPlans).ThenInclude(item => item.Blocks).AsQueryable();
        if (readOnly) query = query.AsNoTracking();
        return await query.FirstOrDefaultAsync(item => item.Id == tripId && item.AppUserId == userId
                && !item.IsArchived && item.PublicationStatus == TripPublicationStatus.Published, ct)
            ?? throw new DayPlanException(409, "stale", "El viaje ya no está disponible. Actualizá la sesión.");
    }

    private static void RequireAccess(TravelerAccessContext access, Guid tripId)
    {
        if (access.User.DeletedAtUtc.HasValue || access.TripId != tripId)
            throw new DayPlanException(403, "access", "No tenés acceso a este viaje.");
        if (access.Session.AccessMode is not (SessionAccessMode.Builder or SessionAccessMode.FreeMapPreview)
            || !access.Capabilities.CanEditItinerary)
            throw new DayPlanException(403, "upgrade", "Activá el pase para planificar este viaje.");
    }
    private static void RequireRevision(Trip trip, int revision)
    {
        if (trip.PlanRevision != revision)
            throw new DayPlanException(409, "stale", "El itinerario cambió. Actualizá el viaje antes de continuar.");
    }
    private async Task RequireCurrentGrantAsync(TravelerAccessContext access, Guid tripId, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var grant = await CurrentGrantQuery(access.User.Id, tripId, now).FirstOrDefaultAsync(ct);
        if (AccessGrantPolicy.ResolveState(grant, now) is not (TrialAccessState.Editing or TrialAccessState.Paid))
            throw new DayPlanException(403, "upgrade", "El acceso al viaje cambió. Actualizá la sesión para continuar.");
        if (grant!.IsTrial != IsTrial(access))
            throw new DayPlanException(403, "access", "El acceso de tu sesión cambió. Seleccioná otra vez el viaje.");
    }
    private IOrderedQueryable<BuilderAccessGrant> CurrentGrantQuery(Guid userId, Guid tripId, DateTimeOffset now) =>
        db.BuilderAccessGrants.AsNoTracking().Where(item => item.AppUserId == userId && item.TripId == tripId
            && item.Status == BuilderAccessStatus.Active && item.RevokedAtUtc == null
            && (item.ExpiresAtUtc == null || item.ExpiresAtUtc > now) && item.AppUser!.DeletedAtUtc == null
            && item.Trip!.AppUserId == userId && !item.Trip.IsArchived
            && item.Trip.PublicationStatus == TripPublicationStatus.Published).OrderByDescending(item => item.CreatedAtUtc);
    private static void Validate(DayPlanRequest request)
    {
        if (request.OperationId == Guid.Empty || request.TripId == Guid.Empty || request.ExpectedRevision < 0
            || request.DayCount is not (1 or 3 or 5 or 7)
            || request.StartDate.DayNumber > DateOnly.MaxValue.DayNumber - request.DayCount + 1
            || request.Locale?.Length > 20)
            throw new DayPlanException(400, "selection", "Elegí una fecha y una duración válidas.");
        if (request.Preferences is { } preferences && (preferences.TravelPace is not ("relaxed" or "balanced" or "efficient")
            || preferences.Budget is not ("low" or "medium" or "high") || preferences.Interests is null
            || preferences.Interests.Count > 3 || preferences.Interests.Any(value => string.IsNullOrWhiteSpace(value) || value.Length > 32)))
            throw new DayPlanException(400, "selection", "Elegí preferencias válidas para tu propuesta.");
    }
    private static TravelPreferenceProfile EffectiveProfile(TravelPreferenceProfile? saved, Guid userId, DayPlanPreferencesDto? preferences) => new()
    {
        UserId = userId, FoodPreferences = saved?.FoodPreferences.ToList() ?? [],
        DietaryRestrictions = saved?.DietaryRestrictions.ToList() ?? [], Dislikes = saved?.Dislikes.ToList() ?? [],
        AvoidTouristTraps = saved?.AvoidTouristTraps ?? true, MaxWalkingMinutes = saved?.MaxWalkingMinutes ?? 25,
        TravelPace = preferences?.TravelPace ?? (saved?.TravelPace is "relaxed" or "balanced" or "efficient" ? saved.TravelPace : "balanced"),
        BudgetLevel = preferences?.Budget ?? (saved?.BudgetLevel is "low" or "medium" or "high" ? saved.BudgetLevel : "medium"),
        Interests = (preferences?.Interests ?? saved?.Interests ?? []).Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Take(3).ToList()
    };
    private static bool IsTrial(TravelerAccessContext access) => access.Session.AccessMode == SessionAccessMode.FreeMapPreview;
    private Task<TrialAccessStatusDto?> TrialStatusAsync(TravelerAccessContext access, CancellationToken ct) =>
        IsTrial(access) ? TrialStatusCoreAsync(access.User.Id, ct) : Task.FromResult<TrialAccessStatusDto?>(null);
    private async Task<TrialAccessStatusDto?> TrialStatusCoreAsync(Guid userId, CancellationToken ct) => await freeTrial.GetStatusAsync(userId, ct);
    private static string OperationKey(Guid id) => AssistantUsageService.PlannerPrefix + id.ToString("N");
    private async Task TrackSafelyAsync(Guid userId, Guid tripId, string name, Guid identity)
    {
        if (analytics is null) return;
        // Existing event uniqueness is the authority for simultaneous replays. Analytics must not fail a committed plan.
        var eventId = new Guid(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new { userId, tripId, name, identity }))[..16]);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var openedConnection = false;
        try
        {
            // Keep the telemetry unit on one connection without extending its time budget.
            if (db.Database.IsRelational() && db.Database.GetDbConnection().State == ConnectionState.Closed)
            {
                await db.Database.OpenConnectionAsync(timeout.Token);
                openedConnection = true;
            }
            if (await db.ProductAnalyticsEvents.AsNoTracking().AnyAsync(item => item.EventId == eventId, timeout.Token)) return;
            await analytics.RecordServerEventAsync(userId, tripId, name, "assistant", null, timeout.Token, eventId: eventId);
        }
        catch (Exception exception)
        {
            foreach (var entry in db.ChangeTracker.Entries<ProductAnalyticsEvent>().Where(item => item.Entity.EventId == eventId).ToList())
                entry.State = EntityState.Detached;
            logger?.LogWarning("Planning funnel event was not recorded: {EventName}; FailureType={FailureType}",
                name, exception.GetType().Name);
        }
        finally
        {
            if (openedConnection)
            {
                try
                {
                    await db.Database.CloseConnectionAsync();
                }
                catch (Exception exception)
                {
                    logger?.LogWarning("Planning telemetry connection was not closed: FailureType={FailureType}",
                        exception.GetType().Name);
                }
            }
        }
    }
    private static string Hash<T>(T value) => Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value)));
    private static T Read<T>(string json) => JsonSerializer.Deserialize<T>(json) ?? throw new InvalidOperationException("Invalid persisted planning payload.");
}

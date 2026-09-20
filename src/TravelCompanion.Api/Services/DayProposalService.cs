using System.Data;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TravelCompanion.Api.Data;
using TravelCompanion.Api.Models;
using TravelCompanion.Shared;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Api.Services;

public sealed class DayProposalService(
    TravelCompanionDbContext dbContext,
    UserSessionService sessions,
    TravelerAccessService accessService,
    AssistantUsageService assistantUsage,
    ProductAnalyticsService? analytics = null,
    DeterministicDayPlanningEngine? planningEngine = null,
    Microsoft.Extensions.Options.IOptions<TravelCompanion.Api.Options.ProductFeatureOptions>? features = null,
    IRecommendationRanker? ranker = null,
    ITravelAiModelClient? modelClient = null,
    Microsoft.Extensions.Options.IOptions<TravelCompanion.Api.Options.OpenAiTravelOptions>? aiOptions = null,
    FreeTrialAccessService? freeTrialAccessService = null)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private DeterministicDayPlanningEngine Planner { get; } = planningEngine ?? new DeterministicDayPlanningEngine();

    public async Task<DayProposalDto> CreateAsync(HttpContext context, DayProposalRequestDto request, CancellationToken ct)
    {
        if (DbExecutionStrategy.ShouldExecute(dbContext))
            return await DbExecutionStrategy.ExecuteAsync(dbContext, () => CreateAsync(context, request, ct), ct);
        if (features?.Value.ProposalsEnabled == false) throw new InvalidOperationException("Las propuestas están desactivadas.");
        var access = await accessService.GetAsync(context, ct) ?? throw new UnauthorizedAccessException();
        if (!access.Capabilities.CanEditItinerary) throw new InvalidOperationException("Necesitas acceso de edición para crear propuestas.");
        if (access.TripId is null) throw new InvalidOperationException("Configura primero el viaje.");
        var prior = await dbContext.ItineraryProposals.AsNoTracking().FirstOrDefaultAsync(item =>
            item.TripId == access.TripId && item.AppUserId == access.User.Id && item.IdempotencyKey == request.IdempotencyKey, ct);
        if (prior is not null) return ToDto(prior);
        var trip = await dbContext.Trips.Include(item => item.Reservations).Include(item => item.DayPlans)
            .ThenInclude(day => day.Blocks).SingleAsync(item => item.Id == access.TripId && item.AppUserId == access.User.Id, ct);
        if (trip.PlanRevision != request.ExpectedRevision) throw new BuilderRevisionConflictException(trip.PlanRevision);
        if (request.Date < trip.StartsOn || request.Date > trip.EndsOn) throw new ArgumentException("La fecha está fuera del viaje.");
        var start = request.WindowStart ?? new TimeOnly(9, 0);
        var end = request.WindowEnd ?? new TimeOnly(21, 0);
        var window = Planner.CreateWindow(request.Date, start, end);
        if (window.DurationMinutes is < 30 or > 24 * 60)
            throw new ArgumentException("La ventana de planificación no es válida.");

        if (request.TargetItemId.HasValue)
        {
            return await CreateTargetedReplacementAsync(
                context, access, trip, request, window, spanish: ProductLanguage.IsSpanish(context), ct);
        }

        var dayItems = trip.Reservations.Where(item => Planner.Covers(item, request.Date)).OrderBy(item => Planner.GetInterval(item).Start).ToList();
        var protectedItems = dayItems.Where(IsProtected).ToList();
        var allFlexible = dayItems.Where(item => !IsProtected(item)).ToList();
        var flexible = OrderForGoal(allFlexible, request.Goal).Take(6).ToList();
        var changes = new List<ItineraryChangeDto>();
        var warnings = new List<string>();
        var spanish = ProductLanguage.IsSpanish(context);
        var cursor = window.Start;
        foreach (var item in flexible)
        {
            var duration = ResolveDuration(item);
            if (!duration.HasValue)
            {
                warnings.Add(spanish ? $"Indica la duración de {item.Title} antes de moverla."
                    : $"Add a duration for {item.Title} before moving it.");
                continue;
            }
            var current = Planner.GetInterval(item);
            if (request.Goal == DayPlanningGoal.Reorganize && current.Start >= window.Start && current.End <= window.End
                && protectedItems.All(other => !current.Overlaps(Planner.GetInterval(other))))
            {
                cursor = cursor > current.End ? cursor : current.End.AddMinutes(20);
                continue;
            }
            var slot = Planner.FindNextAvailable(cursor, duration.Value, protectedItems, window.End);
            if (!slot.HasValue) break;
            var slotEnd = slot.Value.AddMinutes(duration.Value);
            if (current.Start != slot.Value || current.End != slotEnd)
                changes.Add(new(Guid.NewGuid(), ItineraryChangeKind.Move, item.Id, item.RecommendationId,
                    item.Title, DateOnly.FromDateTime(slot.Value), TimeOnly.FromDateTime(slot.Value), TimeOnly.FromDateTime(slotEnd), false,
                    request.Goal == DayPlanningGoal.ReduceWalking
                        ? (spanish ? "Agrupada para reducir desplazamientos." : "Grouped to reduce travel time.")
                        : (spanish ? "Reordenada dentro de una ventana disponible." : "Reordered within an available window."),
                    changes.Count, DateOnly.FromDateTime(slotEnd) == DateOnly.FromDateTime(slot.Value) ? null : DateOnly.FromDateTime(slotEnd)));
            cursor = slotEnd.AddMinutes(request.Goal == DayPlanningGoal.Balance ? 30 : 20);
        }

        if (flexible.Count == 0)
        {
            var city = trip.DayPlans.FirstOrDefault(day => day.Date == request.Date)?.City ?? string.Empty;
            var used = trip.Reservations.Where(item => item.RecommendationId.HasValue).Select(item => item.RecommendationId!.Value).ToHashSet();
            var query = dbContext.Recommendations.AsNoTracking().Where(item => item.DestinationId == trip.DestinationId && !used.Contains(item.Id));
            if (access.Session.AccessMode == SessionAccessMode.FreeMapPreview) query = query.Where(item => item.AccessLevel == ContentAccessLevel.Free);
            var candidates = await query.Where(item => item.Neighborhood.ToLower().Contains(city.ToLower()))
                .OrderByDescending(item => item.Rating).ThenBy(item => item.Title).Take(40).ToListAsync(ct);
            if (access.Session.AccessMode == SessionAccessMode.FreeMapPreview && freeTrialAccessService is not null)
            {
                candidates = (await freeTrialAccessService
                    .FilterToFreeRadiusAsync(candidates, trip.DestinationId, ct))
                    .ToList();
            }
            var profile = await dbContext.TravelPreferenceProfiles.AsNoTracking()
                .SingleOrDefaultAsync(item => item.UserId == access.User.Id, ct) ?? new TravelPreferenceProfile { UserId = access.User.Id };
            var recommendations = ranker is null ? candidates.Take(6).ToList()
                : ranker.Rank(profile, trip.Reservations, candidates,
                    new TravelPlanningContext(city, request.Date, start, end,
                        window.DurationMinutes, null))
                    .Select(item => item.Recommendation).Take(6).ToList();
            foreach (var recommendation in recommendations)
            {
                var duration = recommendation.SuggestedDurationMinutes > 0 ? recommendation.SuggestedDurationMinutes : 60;
                var slot = Planner.FindNextAvailable(cursor, duration, protectedItems, window.End);
                if (!slot.HasValue) break;
                var slotEnd = slot.Value.AddMinutes(duration);
                changes.Add(new(Guid.NewGuid(), ItineraryChangeKind.Add, null, recommendation.Id, recommendation.Title,
                    DateOnly.FromDateTime(slot.Value), TimeOnly.FromDateTime(slot.Value), TimeOnly.FromDateTime(slotEnd), false,
                    spanish ? "Encaja en una ventana libre y evita duplicados del viaje."
                        : "Fits an open window without duplicating an existing trip item.", changes.Count,
                    DateOnly.FromDateTime(slotEnd) == DateOnly.FromDateTime(slot.Value) ? null : DateOnly.FromDateTime(slotEnd)));
                cursor = slotEnd.AddMinutes(20);
            }
        }
        if (Planner.FindOverlaps(protectedItems).Count > 0)
            warnings.Add(spanish ? "Hay reservas protegidas que se solapan. La propuesta no las modifica."
                : "Protected reservations overlap. The proposal leaves them unchanged.");
        if (changes.Count == 0 && warnings.Count == 0)
            warnings.Add(spanish ? "No encontramos cambios válidos para esta ventana."
                : "We could not find valid changes for this window.");

        var quotaLease = await assistantUsage.ReserveAsync(access.User.Id, access.TripId, $"proposal:{request.IdempotencyKey}", ct);
        var narrative = await CreateNarrativeAsync(context, access.User, trip, request.Date, start, end,
            request.Goal, changes, ct);
        var proposal = new ItineraryProposal
        {
            Id = Guid.NewGuid(), TripId = trip.Id, AppUserId = access.User.Id, Date = request.Date,
            Goal = request.Goal, BasedOnRevision = trip.PlanRevision, Version = 1,
            WindowStart = start, WindowEnd = end, WindowEndsNextDay = window.End.Date > window.Start.Date,
            CurrentContextJson = JsonSerializer.Serialize(dayItems.Select(ToSnapshot), JsonOptions),
            ProtectedItemsJson = JsonSerializer.Serialize(protectedItems.Select(ToSnapshot), JsonOptions),
            ChangesJson = JsonSerializer.Serialize(changes, JsonOptions),
            WarningsJson = JsonSerializer.Serialize(warnings, JsonOptions), IdempotencyKey = request.IdempotencyKey,
            Narrative = narrative,
            CreatedAtUtc = DateTimeOffset.UtcNow, ExpiresAtUtc = DateTimeOffset.UtcNow.AddHours(24)
        };
        try
        {
            await using var resultTransaction = dbContext.Database.IsRelational()
                ? await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct) : null;
            dbContext.ItineraryProposals.Add(proposal);
            if (changes.Count > 0) await assistantUsage.CompleteAsync(quotaLease.LeaseId, ct);
            else await assistantUsage.CancelAsync(quotaLease.LeaseId, ct);
            await dbContext.SaveChangesAsync(ct);
            if (resultTransaction is not null) await resultTransaction.CommitAsync(ct);
            if (changes.Count > 0 && analytics is not null)
                await analytics.RecordServerEventAsync(access.User.Id, trip.Id, "proposal_generated", request.Goal.ToString(), null, ct);
            return ToDto(proposal);
        }
        catch
        {
            if (dbContext.Entry(proposal).State == EntityState.Added)
                dbContext.Entry(proposal).State = EntityState.Detached;
            await assistantUsage.CancelAsync(quotaLease.LeaseId, CancellationToken.None);
            throw;
        }
    }

    private async Task<DayProposalDto> CreateTargetedReplacementAsync(
        HttpContext context,
        TravelerAccessContext access,
        Trip trip,
        DayProposalRequestDto request,
        LocalPlanningInterval window,
        bool spanish,
        CancellationToken ct)
    {
        var target = trip.Reservations.SingleOrDefault(item => item.Id == request.TargetItemId
            && Planner.Covers(item, request.Date))
            ?? throw new ArgumentException("El plan que necesita ajuste ya no está disponible.");
        if (IsProtected(target))
            throw new InvalidOperationException("Las reservas protegidas no se sustituyen automáticamente.");

        var used = trip.Reservations.Where(item => item.RecommendationId.HasValue)
            .Select(item => item.RecommendationId!.Value).ToHashSet();
        var candidates = await dbContext.Recommendations.AsNoTracking()
            .Where(item => item.DestinationId == trip.DestinationId && !used.Contains(item.Id))
            .OrderByDescending(item => item.Rating)
            .Take(80)
            .ToListAsync(ct);
        if (access.Session.AccessMode == SessionAccessMode.FreeMapPreview)
        {
            candidates = freeTrialAccessService is null
                ? candidates.Where(item => item.AccessLevel == ContentAccessLevel.Free).ToList()
                : (await freeTrialAccessService.FilterToFreeRadiusAsync(candidates, trip.DestinationId, ct)).ToList();
        }

        var targetRecommendation = target.RecommendationId.HasValue
            ? await dbContext.Recommendations.AsNoTracking().SingleOrDefaultAsync(item => item.Id == target.RecommendationId, ct)
            : null;
        var nearbyItems = trip.Reservations.Where(item => item.Id != target.Id && Planner.Covers(item, request.Date)).ToList();
        var packedDay = string.Equals(request.IssueKind, DayReviewIssueKinds.PackedDay, StringComparison.OrdinalIgnoreCase);
        var replacement = candidates
            .OrderBy(item => targetRecommendation is not null
                && string.Equals(item.Category, targetRecommendation.Category, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(item => packedDay ? Math.Max(15, item.SuggestedDurationMinutes) : 0)
            .ThenBy(item => ProximityScore(item, nearbyItems))
            .ThenByDescending(item => item.Rating)
            .FirstOrDefault()
            ?? throw new InvalidOperationException("No encontramos una alternativa disponible para este plan.");

        var duration = Math.Max(15, replacement.SuggestedDurationMinutes);
        var current = Planner.GetInterval(target);
        var cursor = current.Start < window.Start ? window.Start : current.Start;
        var slot = Planner.FindNextAvailable(cursor, duration, nearbyItems, window.End,
                string.Equals(request.IssueKind, DayReviewIssueKinds.TightTransfer, StringComparison.OrdinalIgnoreCase) ? 35 : 15)
            ?? Planner.FindNextAvailable(window.Start, duration, nearbyItems, window.End, 20)
            ?? throw new InvalidOperationException("No hay una franja libre para sustituir solamente ese plan.");
        var slotEnd = slot.AddMinutes(duration);
        var explanation = request.IssueKind switch
        {
            DayReviewIssueKinds.Overlap => spanish
                ? "Sustituye solo este plan y lo mueve a una franja sin solapamientos."
                : "Replaces only this plan and moves it to a slot without overlaps.",
            DayReviewIssueKinds.TightTransfer => spanish
                ? "Sustituye solo este plan por una alternativa más compatible con los desplazamientos del día."
                : "Replaces only this plan with an option that better fits the day's transfers.",
            DayReviewIssueKinds.PackedDay => spanish
                ? "Sustituye solo este plan por una alternativa más breve."
                : "Replaces only this plan with a shorter alternative.",
            _ => spanish
                ? "Sustituye solo el plan señalado por una alternativa con duración conocida."
                : "Replaces only the selected plan with an alternative of known duration."
        };
        var change = new ItineraryChangeDto(
            Guid.NewGuid(), ItineraryChangeKind.Replace, target.Id, replacement.Id, replacement.Title,
            DateOnly.FromDateTime(slot), TimeOnly.FromDateTime(slot), TimeOnly.FromDateTime(slotEnd),
            false, explanation, 0,
            DateOnly.FromDateTime(slotEnd) == DateOnly.FromDateTime(slot) ? null : DateOnly.FromDateTime(slotEnd));
        var dayItems = trip.Reservations.Where(item => Planner.Covers(item, request.Date)).ToList();
        var protectedItems = dayItems.Where(IsProtected).ToList();
        var quotaLease = await assistantUsage.ReserveAsync(
            access.User.Id, access.TripId, $"proposal:{request.IdempotencyKey}", ct);
        var proposal = new ItineraryProposal
        {
            Id = Guid.NewGuid(), TripId = trip.Id, AppUserId = access.User.Id, Date = request.Date,
            Goal = DayPlanningGoal.Reorganize, BasedOnRevision = trip.PlanRevision, Version = 1,
            WindowStart = TimeOnly.FromDateTime(window.Start), WindowEnd = TimeOnly.FromDateTime(window.End),
            WindowEndsNextDay = window.End.Date > window.Start.Date,
            CurrentContextJson = JsonSerializer.Serialize(dayItems.Select(ToSnapshot), JsonOptions),
            ProtectedItemsJson = JsonSerializer.Serialize(protectedItems.Select(ToSnapshot), JsonOptions),
            ChangesJson = JsonSerializer.Serialize(new[] { change }, JsonOptions), WarningsJson = "[]",
            IdempotencyKey = request.IdempotencyKey, Narrative = explanation,
            CreatedAtUtc = DateTimeOffset.UtcNow, ExpiresAtUtc = DateTimeOffset.UtcNow.AddHours(24)
        };
        try
        {
            dbContext.ItineraryProposals.Add(proposal);
            await assistantUsage.CompleteAsync(quotaLease.LeaseId, ct);
            await dbContext.SaveChangesAsync(ct);
            if (analytics is not null)
                await analytics.RecordServerEventAsync(access.User.Id, trip.Id, "proposal_generated", "targeted_issue", null, ct);
            return ToDto(proposal);
        }
        catch
        {
            if (dbContext.Entry(proposal).State == EntityState.Added) dbContext.Entry(proposal).State = EntityState.Detached;
            await assistantUsage.CancelAsync(quotaLease.LeaseId, CancellationToken.None);
            throw;
        }
    }

    private static decimal ProximityScore(Recommendation recommendation, IReadOnlyList<Reservation> items)
    {
        var located = items.Where(item => item.Latitude.HasValue && item.Longitude.HasValue).ToList();
        if (located.Count == 0) return 0;
        return located.Min(item =>
        {
            var latitude = recommendation.Latitude - item.Latitude!.Value;
            var longitude = recommendation.Longitude - item.Longitude!.Value;
            return latitude * latitude + longitude * longitude;
        });
    }

    public async Task<DayProposalDto> GetAsync(HttpContext context, Guid id, CancellationToken ct)
    {
        if (features?.Value.ProposalsEnabled == false) throw new InvalidOperationException("Las propuestas están desactivadas.");
        var session = await sessions.GetSessionContextAsync(context, ct) ?? throw new UnauthorizedAccessException();
        var proposal = await dbContext.ItineraryProposals.AsNoTracking().SingleOrDefaultAsync(item => item.Id == id && item.AppUserId == session.User.Id, ct)
            ?? throw new KeyNotFoundException();
        return ToDto(proposal);
    }

    public async Task<DayProposalDto> CreateForRouteAsync(HttpContext context, ThematicRoute route,
        ApplyThematicRouteDto request, CancellationToken ct)
    {
        if (features?.Value.ProposalsEnabled == false) throw new InvalidOperationException("Las propuestas están desactivadas.");
        var access = await accessService.GetAsync(context, ct) ?? throw new UnauthorizedAccessException();
        if (!access.Capabilities.CanEditItinerary) throw new InvalidOperationException("Necesitas acceso de edición para aplicar propuestas.");
        var session = access.Session;
        var trip = await dbContext.Trips.Include(item => item.Reservations).SingleAsync(item =>
            item.Id == access.TripId && item.AppUserId == session.User.Id, ct);
        if (trip.PlanRevision != request.ExpectedRevision) throw new BuilderRevisionConflictException(trip.PlanRevision);
        var existingRecommendations = trip.Reservations.Where(item => item.RecommendationId.HasValue)
            .Select(item => item.RecommendationId!.Value).ToHashSet();
        var occupiedItems = trip.Reservations.Where(item => Planner.Covers(item, request.Date)).ToList();
        var protectedItems = occupiedItems.Where(IsProtected).ToList();
        var window = Planner.CreateWindow(request.Date, request.WindowStart, request.WindowEnd);
        var cursor = window.Start;
        var changes = new List<ItineraryChangeDto>();
        var warnings = new List<string>();
        var spanish = ProductLanguage.IsSpanish(context);
        foreach (var stop in route.Stops.OrderBy(item => item.SortOrder).Where(item => !existingRecommendations.Contains(item.RecommendationId)))
        {
            var duration = Math.Max(15, stop.DurationMinutes);
            var slot = Planner.FindNextAvailable(cursor, duration, occupiedItems, window.End,
                stop.EstimatedTransferMinutes ?? 20);
            if (!slot.HasValue)
            {
                warnings.Add(spanish ? "Algunas paradas no caben en la ventana elegida."
                    : "Some stops do not fit within the selected window.");
                break;
            }
            var slotEnd = slot.Value.AddMinutes(duration);
            changes.Add(new(Guid.NewGuid(), ItineraryChangeKind.Add, null, stop.RecommendationId,
                stop.Recommendation?.Title ?? (spanish ? "Parada" : "Stop"), DateOnly.FromDateTime(slot.Value), TimeOnly.FromDateTime(slot.Value),
                TimeOnly.FromDateTime(slotEnd), false, spanish ? "Parada de la ruta guardada." : "Stop from the saved route.", changes.Count,
                DateOnly.FromDateTime(slotEnd) == DateOnly.FromDateTime(slot.Value) ? null : DateOnly.FromDateTime(slotEnd)));
            cursor = slotEnd.AddMinutes(stop.EstimatedTransferMinutes ?? 20);
        }
        var proposal = new ItineraryProposal
        {
            Id = Guid.NewGuid(), TripId = trip.Id, AppUserId = session.User.Id, SourceRouteId = route.Id, Date = request.Date,
            Goal = DayPlanningGoal.Reorganize, BasedOnRevision = trip.PlanRevision, Version = 1,
            WindowStart = request.WindowStart, WindowEnd = request.WindowEnd,
            WindowEndsNextDay = window.End.Date > window.Start.Date,
            CurrentContextJson = JsonSerializer.Serialize(occupiedItems.Select(ToSnapshot), JsonOptions),
            ProtectedItemsJson = JsonSerializer.Serialize(protectedItems.Select(ToSnapshot), JsonOptions),
            ChangesJson = JsonSerializer.Serialize(changes, JsonOptions), WarningsJson = JsonSerializer.Serialize(warnings, JsonOptions),
            Narrative = BuildNarrative(DayPlanningGoal.Reorganize, changes, spanish),
            IdempotencyKey = $"route:{route.Id:N}:{request.IdempotencyKey}", CreatedAtUtc = DateTimeOffset.UtcNow,
            ExpiresAtUtc = DateTimeOffset.UtcNow.AddHours(24)
        };
        dbContext.ItineraryProposals.Add(proposal);
        await dbContext.SaveChangesAsync(ct);
        return ToDto(proposal);
    }

    public async Task<DayProposalDto> ReviseAsync(HttpContext context, Guid id, ReviseDayProposalDto request, CancellationToken ct)
    {
        if (features?.Value.ProposalsEnabled == false) throw new InvalidOperationException("Las propuestas están desactivadas.");
        var access = await accessService.GetAsync(context, ct) ?? throw new UnauthorizedAccessException();
        if (!access.Capabilities.CanEditItinerary) throw new InvalidOperationException("Necesitas acceso de edición para modificar propuestas.");
        var session = access.Session;
        var proposal = await dbContext.ItineraryProposals.SingleOrDefaultAsync(item => item.Id == id && item.AppUserId == session.User.Id, ct)
            ?? throw new KeyNotFoundException();
        if (proposal.Version != request.Version || proposal.ExpiresAtUtc <= DateTimeOffset.UtcNow) throw new InvalidOperationException("Actualiza la propuesta antes de continuar.");
        var changes = ReadChanges(proposal).Where(item => !request.ExcludedChangeIds.Contains(item.ChangeId)).ToList();
        if (request.ReplaceChangeId is { } replaceId)
        {
            var index = changes.FindIndex(item => item.ChangeId == replaceId);
            if (index < 0) throw new ArgumentException("La sugerencia que quieres sustituir ya no está disponible.");
            var lease = await assistantUsage.ReserveAsync(session.User.Id, proposal.TripId,
                $"proposal-replace:{proposal.Id:N}:{proposal.Version}:{replaceId:N}", ct);
            try
            {
                var trip = await dbContext.Trips.AsNoTracking().SingleAsync(item => item.Id == proposal.TripId, ct);
                var used = changes.Where(item => item.RecommendationId.HasValue).Select(item => item.RecommendationId!.Value).ToHashSet();
                used.UnionWith(await dbContext.Reservations.AsNoTracking().Where(item => item.TripId == proposal.TripId
                    && item.RecommendationId.HasValue).Select(item => item.RecommendationId!.Value).ToListAsync(ct));
                var query = dbContext.Recommendations.AsNoTracking().Where(item => item.DestinationId == trip.DestinationId && !used.Contains(item.Id));
                if (session.AccessMode == SessionAccessMode.FreeMapPreview)
                    query = query.Where(item => item.AccessLevel == ContentAccessLevel.Free);
                var alternative = await query.OrderByDescending(item => item.Rating).ThenBy(item => item.Title).FirstOrDefaultAsync(ct)
                    ?? throw new InvalidOperationException("No hay otra alternativa disponible con tu acceso actual.");
                var current = changes[index];
                var duration = Math.Max(15, alternative.SuggestedDurationMinutes);
                changes[index] = current with
                {
                    Kind = current.ExistingItemId.HasValue ? ItineraryChangeKind.Replace : ItineraryChangeKind.Add,
                    RecommendationId = alternative.Id,
                    Title = alternative.Title,
                    EndsAt = current.StartsAt.AddMinutes(duration),
                    Explanation = ProductLanguage.Text(context, "Alternative recalculated for the same window.",
                        "Alternativa recalculada para la misma franja.")
                };
                await assistantUsage.CompleteAsync(lease.LeaseId, ct);
            }
            catch
            {
                await assistantUsage.CancelAsync(lease.LeaseId, CancellationToken.None);
                throw;
            }
        }
        if (request.OrderedChangeIds.Count > 0)
        {
            var order = request.OrderedChangeIds.Select((changeId, index) => (changeId, index)).ToDictionary(item => item.changeId, item => item.index);
            changes = changes.OrderBy(item => order.GetValueOrDefault(item.ChangeId, int.MaxValue)).Select((item, index) => item with { SortOrder = index }).ToList();
        }
        changes = await RecalculateChangesAsync(proposal, changes, ct);
        proposal.Version++;
        proposal.ChangesJson = JsonSerializer.Serialize(changes, JsonOptions);
        proposal.Narrative = BuildNarrative(proposal.Goal, changes, ProductLanguage.IsSpanish(context));
        await dbContext.SaveChangesAsync(ct);
        return ToDto(proposal);
    }

    private async Task<List<ItineraryChangeDto>> RecalculateChangesAsync(ItineraryProposal proposal,
        List<ItineraryChangeDto> changes, CancellationToken ct)
    {
        if (changes.Count == 0) return changes;
        var trip = await dbContext.Trips.AsNoTracking().Include(item => item.Reservations)
            .SingleAsync(item => item.Id == proposal.TripId, ct);
        if (trip.PlanRevision != proposal.BasedOnRevision)
            throw new BuilderRevisionConflictException(trip.PlanRevision);
        var movingIds = changes.Where(item => item.ExistingItemId.HasValue)
            .Select(item => item.ExistingItemId!.Value).ToHashSet();
        var occupied = trip.Reservations.Where(item => Planner.Covers(item, proposal.Date)
            && (IsProtected(item) || !movingIds.Contains(item.Id))).ToList();
        var window = Planner.CreateWindow(proposal.Date, proposal.WindowStart, proposal.WindowEnd);
        var cursor = window.Start;
        var result = new List<ItineraryChangeDto>();
        foreach (var change in changes.OrderBy(item => item.SortOrder))
        {
            var changeStart = change.Date.ToDateTime(change.StartsAt);
            var changeEndDate = change.EndsOn ?? change.Date;
            var changeEnd = changeEndDate.ToDateTime(change.EndsAt ?? change.StartsAt.AddMinutes(60));
            if (changeEnd <= changeStart) changeEnd = changeEnd.AddDays(1);
            var duration = Math.Max(15, (int)(changeEnd - changeStart).TotalMinutes);
            var slot = Planner.FindNextAvailable(cursor, duration, occupied, window.End);
            if (!slot.HasValue)
                throw new InvalidOperationException("Los cambios seleccionados ya no caben en la ventana elegida.");
            var slotEnd = slot.Value.AddMinutes(duration);
            result.Add(change with
            {
                Date = DateOnly.FromDateTime(slot.Value), StartsAt = TimeOnly.FromDateTime(slot.Value),
                EndsAt = TimeOnly.FromDateTime(slotEnd),
                EndsOn = DateOnly.FromDateTime(slotEnd) == DateOnly.FromDateTime(slot.Value) ? null : DateOnly.FromDateTime(slotEnd),
                SortOrder = result.Count
            });
            cursor = slotEnd.AddMinutes(20);
        }
        return result;
    }

    private async Task<string> CreateNarrativeAsync(HttpContext context, AppUser user, Trip trip, DateOnly date,
        TimeOnly start, TimeOnly end, DayPlanningGoal goal, IReadOnlyList<ItineraryChangeDto> changes,
        CancellationToken ct)
    {
        var spanish = ProductLanguage.IsSpanish(context);
        var fallback = BuildNarrative(goal, changes, spanish);
        if (changes.Count == 0 || modelClient is null) return fallback;
        var profile = await dbContext.TravelPreferenceProfiles.AsNoTracking()
            .SingleOrDefaultAsync(item => item.UserId == user.Id, ct) ?? new TravelPreferenceProfile { UserId = user.Id };
        var city = trip.DayPlans.FirstOrDefault(item => item.Date == date)?.City ?? string.Empty;
        var message = (spanish
            ? $"Explica brevemente esta propuesta ya validada para {date:yyyy-MM-dd}: "
            : $"Briefly explain this validated proposal for {date:yyyy-MM-dd}: ")
            + string.Join("; ", changes.Select(item => $"{item.Kind} {item.Title} {item.StartsAt:HH\\:mm}"));
        try
        {
            var result = await modelClient.CreateStructuredResponseAsync(new TravelAiModelRequest(
                $"day-proposal-{Guid.NewGuid():N}", "day_proposal_explanation", message,
                context.Request.Headers.AcceptLanguage.FirstOrDefault(), profile,
                new TravelPlanningContext(city, date, start, end,
                    (int)(end.ToTimeSpan() - start.ToTimeSpan()).TotalMinutes, null),
                trip.Reservations, [], [], aiOptions?.Value.PromptVersion ?? "travel-chat.v1"), ct);
            return string.IsNullOrWhiteSpace(result?.Message) ? fallback : result.Message[..Math.Min(1200, result.Message.Length)];
        }
        catch (Exception) when (!ct.IsCancellationRequested) { return fallback; }
    }

    private static string BuildNarrative(DayPlanningGoal goal, IReadOnlyList<ItineraryChangeDto> changes, bool spanish)
    {
        if (changes.Count == 0) return spanish ? "No encontramos cambios válidos para este día."
            : "We could not find valid changes for this day.";
        var added = changes.Count(item => item.Kind == ItineraryChangeKind.Add);
        var moved = changes.Count(item => item.Kind == ItineraryChangeKind.Move);
        var replaced = changes.Count(item => item.Kind == ItineraryChangeKind.Replace);
        var removed = changes.Count(item => item.Kind == ItineraryChangeKind.Remove);
        return spanish
            ? $"Propuesta para {goal}: {added} altas, {moved} movimientos, {replaced} sustituciones y {removed} retiradas. Las reservas protegidas permanecen intactas."
            : $"Proposal for {goal}: {added} additions, {moved} moves, {replaced} replacements and {removed} removals. Protected reservations remain unchanged.";
    }

    public async Task<ItineraryChangeSetDto> ApplyAsync(HttpContext context, Guid id, ApplyDayProposalDto request, CancellationToken ct)
    {
        if (DbExecutionStrategy.ShouldExecute(dbContext))
            return await DbExecutionStrategy.ExecuteAsync(dbContext, () => ApplyAsync(context, id, request, ct), ct);
        if (features?.Value.ProposalsEnabled == false) throw new InvalidOperationException("Las propuestas están desactivadas.");
        var access = await accessService.GetAsync(context, ct) ?? throw new UnauthorizedAccessException();
        if (!access.Capabilities.CanEditItinerary) throw new InvalidOperationException("Necesitas acceso de edición para aplicar propuestas.");
        var session = access.Session;
        var priorOperation = await dbContext.ItineraryOperations.AsNoTracking().FirstOrDefaultAsync(item =>
            item.TripId == session.TripId && item.IdempotencyKey == request.IdempotencyKey, ct);
        if (priorOperation is not null) return await ToChangeSetAsync(priorOperation, ct);
        await using var transaction = dbContext.Database.IsRelational()
            ? await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct) : null;
        var proposalIdentity = await dbContext.ItineraryProposals.AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == id && item.AppUserId == session.User.Id, ct)
            ?? throw new KeyNotFoundException();
        await TripConcurrencyLock.LockAsync(dbContext, proposalIdentity.TripId, ct);
        var currentGrant = await EnsureEditingAccessAsync(session.User.Id, proposalIdentity.TripId, ct);
        priorOperation = await dbContext.ItineraryOperations.AsNoTracking().FirstOrDefaultAsync(item =>
            item.TripId == proposalIdentity.TripId && item.IdempotencyKey == request.IdempotencyKey, ct);
        if (priorOperation is not null)
        {
            if (transaction is not null) await transaction.CommitAsync(ct);
            return await ToChangeSetAsync(priorOperation, ct);
        }
        var proposal = await dbContext.ItineraryProposals.SingleAsync(item => item.Id == id && item.AppUserId == session.User.Id, ct);
        if (proposal.Version != request.Version || proposal.ExpiresAtUtc <= DateTimeOffset.UtcNow || proposal.AppliedAtUtc.HasValue)
            throw new InvalidOperationException("Actualiza la propuesta antes de continuar.");
        var trip = await dbContext.Trips.Include(item => item.Reservations).Include(item => item.DayPlans).ThenInclude(day => day.Blocks)
            .SingleAsync(item => item.Id == proposal.TripId && item.AppUserId == session.User.Id, ct);
        if (trip.PlanRevision != request.ExpectedRevision || proposal.BasedOnRevision != trip.PlanRevision)
            throw new BuilderRevisionConflictException(trip.PlanRevision);
        var changes = ReadChanges(proposal);
        var recommendationIds = changes.Where(item => item.RecommendationId.HasValue)
            .Select(item => item.RecommendationId!.Value).Distinct().ToList();
        var eligibleRecommendationCount = await dbContext.Recommendations.AsNoTracking().CountAsync(item =>
            recommendationIds.Contains(item.Id) && item.DestinationId == trip.DestinationId
            && (!currentGrant.IsTrial || item.AccessLevel == ContentAccessLevel.Free), ct);
        if (eligibleRecommendationCount != recommendationIds.Count)
            throw new InvalidOperationException("Una recomendación ya no está disponible con el acceso actual.");
        var protectedItems = trip.Reservations.Where(IsProtected).ToList();
        if (changes.Any(change => change.Kind != ItineraryChangeKind.Remove && protectedItems.Any(item =>
            ChangeInterval(change).Overlaps(Planner.GetInterval(item)))))
            throw new InvalidOperationException("Una reserva protegida ocupa ahora esa franja. Actualiza la propuesta.");
        var originalConflicts = Planner.FindOverlaps(trip.Reservations)
            .Select(item => ConflictKey(item.First.Id, item.Second.Id)).ToHashSet();
        var snapshots = trip.Reservations.Where(item => !IsProtected(item)).Select(ToSnapshot).ToList();
        var routeLinks = await dbContext.ThematicRouteStops.AsNoTracking()
            .Where(item => item.ThematicRoute!.TripId == trip.Id && item.ItineraryItemId.HasValue)
            .Select(item => new RouteLinkSnapshot(item.Id, item.ItineraryItemId!.Value)).ToListAsync(ct);
        var appliedItems = changes.OrderBy(item => item.SortOrder).Select(change => ApplyChange(trip, change))
            .Where(item => item is not null).Cast<Reservation>().ToList();
        var resultingItems = trip.Reservations.Where(item => dbContext.Entry(item).State != EntityState.Deleted)
            .DistinctBy(item => item.Id).ToList();
        var newConflict = Planner.FindOverlaps(resultingItems)
            .FirstOrDefault(item => !originalConflicts.Contains(ConflictKey(item.First.Id, item.Second.Id)));
        if (newConflict.First is not null)
            throw new InvalidOperationException($"La agenda resultante solapa {newConflict.First.Title} y {newConflict.Second.Title}.");
        var operation = new ItineraryOperation
        {
            Id = Guid.NewGuid(), TripId = trip.Id, AppUserId = session.User.Id, IdempotencyKey = request.IdempotencyKey,
            PreviousStateJson = JsonSerializer.Serialize(new OperationSnapshot(snapshots, routeLinks), JsonOptions), PreviousRevision = trip.PlanRevision,
            AppliedRevision = trip.PlanRevision + 1, AppliedAtUtc = DateTimeOffset.UtcNow,
            UndoAvailableUntilUtc = DateTimeOffset.UtcNow.AddHours(24)
        };
        if (proposal.SourceRouteId is { } sourceRouteId)
        {
            var sourceRoute = await dbContext.ThematicRoutes.Include(item => item.Stops)
                .SingleOrDefaultAsync(item => item.Id == sourceRouteId && item.AppUserId == session.User.Id, ct);
            if (sourceRoute is not null)
            {
                var application = new ThematicRouteApplication
                {
                    Id = Guid.NewGuid(), ThematicRouteId = sourceRoute.Id, TripId = trip.Id,
                    ItineraryOperationId = operation.Id, Date = proposal.Date, CreatedAtUtc = DateTimeOffset.UtcNow,
                    Stops = sourceRoute.Stops.Select(stop => new
                    {
                        Stop = stop,
                        ItemId = appliedItems.FirstOrDefault(item => item.RecommendationId == stop.RecommendationId)?.Id
                            ?? resultingItems.FirstOrDefault(item => item.RecommendationId == stop.RecommendationId)?.Id
                    }).Where(item => item.ItemId.HasValue).Select(item => new ThematicRouteApplicationStop
                    {
                        Id = Guid.NewGuid(), ThematicRouteStopId = item.Stop.Id, ItineraryItemId = item.ItemId!.Value
                    }).ToList()
                };
                operation.RouteApplicationId = application.Id;
                sourceRoute.UpdatedAtUtc = DateTimeOffset.UtcNow;
                dbContext.ThematicRouteApplications.Add(application);
            }
        }
        trip.PlanRevision++;
        trip.UpdatedAtUtc = DateTimeOffset.UtcNow;
        proposal.AppliedAtUtc = DateTimeOffset.UtcNow;
        dbContext.ItineraryOperations.Add(operation);
        QueueSynchronization(trip, session.User.Id, "proposal_apply");
        await dbContext.SaveChangesAsync(ct);
        if (transaction is not null) await transaction.CommitAsync(ct);
        if (analytics is not null)
            await analytics.RecordServerEventAsync(session.User.Id, trip.Id, "proposal_applied", "day_planner", null, ct);
        return await ToChangeSetAsync(operation, ct);
    }

    public async Task<ItineraryChangeSetDto> UndoAsync(HttpContext context, Guid operationId, CancellationToken ct)
    {
        if (DbExecutionStrategy.ShouldExecute(dbContext))
            return await DbExecutionStrategy.ExecuteAsync(dbContext, () => UndoAsync(context, operationId, ct), ct);
        var session = await sessions.GetSessionContextAsync(context, ct) ?? throw new UnauthorizedAccessException();
        await using var transaction = dbContext.Database.IsRelational()
            ? await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct) : null;
        var operationIdentity = await dbContext.ItineraryOperations.AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == operationId && item.AppUserId == session.User.Id, ct)
            ?? throw new KeyNotFoundException();
        await TripConcurrencyLock.LockAsync(dbContext, operationIdentity.TripId, ct);
        await EnsureEditingAccessAsync(session.User.Id, operationIdentity.TripId, ct);
        var operation = await dbContext.ItineraryOperations.SingleAsync(item => item.Id == operationId && item.AppUserId == session.User.Id, ct);
        var trip = await dbContext.Trips.Include(item => item.Reservations).SingleAsync(item => item.Id == operation.TripId, ct);
        if (operation.UndoneAtUtc.HasValue) return await ToChangeSetAsync(operation, ct);
        if (operation.UndoAvailableUntilUtc <= DateTimeOffset.UtcNow || trip.PlanRevision != operation.AppliedRevision)
            throw new InvalidOperationException("Ya no se puede deshacer porque el viaje cambió.");
        var previous = ReadOperationSnapshot(operation.PreviousStateJson);
        var snapshots = previous.Reservations;
        var mutable = trip.Reservations.Where(item => !IsProtected(item)).ToList();
        var snapshotIds = snapshots.Select(item => item.Id).ToHashSet();
        dbContext.Reservations.RemoveRange(mutable.Where(item => !snapshotIds.Contains(item.Id)));
        foreach (var snapshot in snapshots)
        {
            var existing = mutable.FirstOrDefault(item => item.Id == snapshot.Id);
            if (existing is null) dbContext.Reservations.Add(FromSnapshot(snapshot));
            else RestoreSnapshot(existing, snapshot);
        }
        var routeStops = await dbContext.ThematicRouteStops
            .Where(item => item.ThematicRoute!.TripId == trip.Id).ToListAsync(ct);
        foreach (var stop in routeStops) stop.ItineraryItemId = null;
        foreach (var link in previous.RouteLinks)
        {
            var stop = routeStops.FirstOrDefault(item => item.Id == link.StopId);
            if (stop is not null) stop.ItineraryItemId = link.ItineraryItemId;
        }
        trip.PlanRevision++;
        trip.UpdatedAtUtc = DateTimeOffset.UtcNow;
        operation.UndoneAtUtc = DateTimeOffset.UtcNow;
        if (operation.RouteApplicationId is { } applicationId)
        {
            var application = await dbContext.ThematicRouteApplications
                .SingleOrDefaultAsync(item => item.Id == applicationId, ct);
            if (application is not null) dbContext.ThematicRouteApplications.Remove(application);
            operation.RouteApplicationId = null;
        }
        QueueSynchronization(trip, session.User.Id, "proposal_undo");
        await dbContext.SaveChangesAsync(ct);
        if (transaction is not null) await transaction.CommitAsync(ct);
        return await ToChangeSetAsync(operation, ct);
    }

    private Reservation? ApplyChange(Trip trip, ItineraryChangeDto change)
    {
        if (change.Kind == ItineraryChangeKind.Add)
        {
            var recommendation = dbContext.Recommendations.Find(change.RecommendationId!.Value)
                ?? throw new InvalidOperationException("Una recomendación ya no está disponible.");
            var block = trip.DayPlans.Single(day => day.Date == change.Date).Blocks.Single(item => item.PeriodKey == TripPlanPeriods.Resolve(change.StartsAt).Key);
            var reservation = new Reservation
            {
                Id = Guid.NewGuid(), TripId = trip.Id, TripDayBlockId = block.Id, RecommendationId = recommendation.Id,
                Type = ReservationType.Event, PlanningKind = ScheduleItemKind.Recommendation, Owner = ItineraryItemOwner.Traveler,
                ItemSource = ItineraryItemSource.YukuRecommendation, TimePrecision = ItineraryTimePrecision.Exact,
                Flexibility = ItineraryFlexibility.Flexible, DurationMinutes = recommendation.SuggestedDurationMinutes,
                Date = change.Date, StartsAt = change.StartsAt, EndsOn = change.EndsOn, EndsAt = change.EndsAt, TimeZoneId = trip.TimeZoneId,
                Title = recommendation.Title, City = recommendation.Neighborhood.Split(',')[0], LocationName = recommendation.Title,
                Address = recommendation.Neighborhood, ConfirmationCode = string.Empty, Notes = recommendation.Description,
                Latitude = recommendation.Latitude, Longitude = recommendation.Longitude, SourceName = "YUKU Japan",
                SourceUrl = recommendation.SourceUrl, SortOrder = change.SortOrder
            };
            dbContext.Reservations.Add(reservation);
            trip.Reservations.Add(reservation);
            return reservation;
        }
        var item = trip.Reservations.Single(existing => existing.Id == change.ExistingItemId);
        if (IsProtected(item)) throw new InvalidOperationException("Una actividad protegida cambió. Actualiza la propuesta.");
        if (change.Kind == ItineraryChangeKind.Remove) { dbContext.Reservations.Remove(item); return null; }
        if (change.Kind == ItineraryChangeKind.Replace)
        {
            var recommendation = dbContext.Recommendations.Find(change.RecommendationId!.Value)
                ?? throw new InvalidOperationException("La alternativa ya no está disponible.");
            item.RecommendationId = recommendation.Id;
            item.Title = recommendation.Title;
            item.City = recommendation.Neighborhood.Split(',')[0];
            item.LocationName = recommendation.Title;
            item.Address = recommendation.Neighborhood;
            item.Notes = recommendation.Description;
            item.Latitude = recommendation.Latitude;
            item.Longitude = recommendation.Longitude;
            item.ProviderPlaceId = recommendation.ProviderPlaceId;
            item.ItemSource = ItineraryItemSource.YukuRecommendation;
            item.PlanningKind = ScheduleItemKind.Recommendation;
            item.SourceName = "YUKU Japan";
            item.SourceUrl = recommendation.SourceUrl;
            item.DurationMinutes = recommendation.SuggestedDurationMinutes > 0
                ? recommendation.SuggestedDurationMinutes : item.DurationMinutes;
        }
        item.Date = change.Date; item.StartsAt = change.StartsAt; item.EndsOn = change.EndsOn;
        item.EndsAt = change.EndsAt; item.SortOrder = change.SortOrder;
        item.TimePrecision = ItineraryTimePrecision.Exact;
        item.TripDayBlockId = trip.DayPlans.Single(day => day.Date == change.Date).Blocks.Single(block => block.PeriodKey == TripPlanPeriods.Resolve(change.StartsAt).Key).Id;
        return item;
    }

    private async Task<ItineraryChangeSetDto> ToChangeSetAsync(ItineraryOperation operation, CancellationToken ct)
    {
        var trip = await dbContext.Trips.AsNoTracking().Include(item => item.Reservations).SingleAsync(item => item.Id == operation.TripId, ct);
        return new(operation.Id, trip.Id, trip.PlanRevision, trip.Reservations.OrderBy(item => item.Date).ThenBy(item => item.StartsAt)
            .Select(TravelerItineraryService.ToDto).ToList(), operation.UndoAvailableUntilUtc);
    }

    private static bool IsProtected(Reservation item) => item.Owner == ItineraryItemOwner.Yuku
        || item.Type is ReservationType.Flight or ReservationType.Lodging
        || item.Flexibility is ItineraryFlexibility.FixedByTraveler or ItineraryFlexibility.ConfirmedReservation;
    private async Task<BuilderAccessGrant> EnsureEditingAccessAsync(Guid userId, Guid tripId, CancellationToken ct)
    {
        var grant = await dbContext.BuilderAccessGrants.AsNoTracking().SingleOrDefaultAsync(item =>
            item.AppUserId == userId && item.TripId == tripId, ct);
        var state = AccessGrantPolicy.ResolveState(grant, DateTimeOffset.UtcNow);
        if (state is not TrialAccessState.Editing and not TrialAccessState.NotStarted and not TrialAccessState.Paid)
            throw new InvalidOperationException("Necesitas acceso de edición activo para modificar este viaje.");
        return grant!;
    }
    private static LocalPlanningInterval ChangeInterval(ItineraryChangeDto change)
    {
        var start = DateTime.SpecifyKind(change.Date.ToDateTime(change.StartsAt), DateTimeKind.Unspecified);
        var endDate = change.EndsOn ?? change.Date;
        var end = DateTime.SpecifyKind(endDate.ToDateTime(change.EndsAt ?? change.StartsAt.AddMinutes(60)), DateTimeKind.Unspecified);
        if (end <= start) end = end.AddDays(1);
        return new(start, end);
    }
    private static string ConflictKey(Guid first, Guid second) => first.CompareTo(second) < 0
        ? $"{first:N}:{second:N}" : $"{second:N}:{first:N}";
    private void QueueSynchronization(Trip trip, Guid userId, string kind)
    {
        dbContext.TripSynchronizationWorks.Add(new TripSynchronizationWork
        {
            Id = Guid.NewGuid(), TripId = trip.Id, AppUserId = userId, Revision = trip.PlanRevision,
            Kind = kind, CreatedAtUtc = DateTimeOffset.UtcNow, NextAttemptAtUtc = DateTimeOffset.UtcNow
        });
    }
    private static IReadOnlyList<Reservation> OrderForGoal(IReadOnlyList<Reservation> items, DayPlanningGoal goal)
    {
        var chronological = items.OrderBy(item => item.Date).ThenBy(item => item.StartsAt).ToList();
        if (goal != DayPlanningGoal.ReduceWalking || chronological.Count < 3) return chronological;
        var remaining = new List<Reservation>(chronological);
        var ordered = new List<Reservation> { remaining[0] };
        remaining.RemoveAt(0);
        while (remaining.Count > 0)
        {
            var previous = ordered[^1];
            var next = remaining.OrderBy(item => DistanceSquared(previous, item)).ThenBy(item => item.StartsAt).First();
            ordered.Add(next);
            remaining.Remove(next);
        }
        return ordered;
    }
    private static decimal DistanceSquared(Reservation first, Reservation second)
    {
        if (!first.Latitude.HasValue || !first.Longitude.HasValue || !second.Latitude.HasValue || !second.Longitude.HasValue)
            return decimal.MaxValue;
        var latitude = first.Latitude.Value - second.Latitude.Value;
        var longitude = first.Longitude.Value - second.Longitude.Value;
        return latitude * latitude + longitude * longitude;
    }
    private int? ResolveDuration(Reservation item) => item.DurationMinutes is > 0 ? item.DurationMinutes
        : item.EndsAt.HasValue ? Planner.GetInterval(item).DurationMinutes : null;
    private static List<ItineraryChangeDto> ReadChanges(ItineraryProposal proposal) =>
        JsonSerializer.Deserialize<List<ItineraryChangeDto>>(proposal.ChangesJson, JsonOptions) ?? [];
    private static DayProposalDto ToDto(ItineraryProposal proposal) => new(proposal.Id, proposal.TripId, proposal.Date,
        proposal.Goal, proposal.BasedOnRevision, proposal.Version, proposal.ExpiresAtUtc, ReadChanges(proposal),
        JsonSerializer.Deserialize<List<string>>(proposal.WarningsJson, JsonOptions) ?? [],
        proposal.ExpiresAtUtc > DateTimeOffset.UtcNow && !proposal.AppliedAtUtc.HasValue, proposal.Narrative,
        proposal.WindowStart, proposal.WindowEnd, proposal.WindowEndsNextDay);
    private static OperationSnapshot ReadOperationSnapshot(string json)
    {
        try
        {
            var snapshot = JsonSerializer.Deserialize<OperationSnapshot>(json, JsonOptions);
            if (snapshot is not null) return snapshot;
        }
        catch (JsonException) { }
        try
        {
            return new(JsonSerializer.Deserialize<List<ReservationSnapshot>>(json, JsonOptions) ?? [], []);
        }
        catch (JsonException) { return new([], []); }
    }
    private static ReservationSnapshot ToSnapshot(Reservation item) => new(item.Id, item.TripId, item.TripDayBlockId, item.RecommendationId,
        item.Type, item.PlanningKind, item.Owner, item.ItemSource, item.TimePrecision, item.Flexibility, item.DurationMinutes, item.SortOrder,
        item.ProviderPlaceId, item.Date, item.StartsAt, item.EndsOn, item.EndsAt, item.TimeZoneId, item.Title, item.City, item.LocationName,
        item.Address, item.ConfirmationCode, item.Notes, item.Latitude, item.Longitude, item.SourceName, item.SourceUrl);
    private static Reservation FromSnapshot(ReservationSnapshot x) => new()
    {
        Id=x.Id, TripId=x.TripId, TripDayBlockId=x.TripDayBlockId, RecommendationId=x.RecommendationId, Type=x.Type,
        PlanningKind=x.PlanningKind, Owner=x.Owner, ItemSource=x.ItemSource, TimePrecision=x.TimePrecision, Flexibility=x.Flexibility,
        DurationMinutes=x.DurationMinutes, SortOrder=x.SortOrder, ProviderPlaceId=x.ProviderPlaceId, Date=x.Date, StartsAt=x.StartsAt,
        EndsOn=x.EndsOn, EndsAt=x.EndsAt, TimeZoneId=x.TimeZoneId, Title=x.Title, City=x.City, LocationName=x.LocationName,
        Address=x.Address, ConfirmationCode=x.ConfirmationCode, Notes=x.Notes, Latitude=x.Latitude, Longitude=x.Longitude,
        SourceName=x.SourceName, SourceUrl=x.SourceUrl
    };
    private static void RestoreSnapshot(Reservation item, ReservationSnapshot x)
    {
        item.TripDayBlockId=x.TripDayBlockId; item.RecommendationId=x.RecommendationId; item.Type=x.Type;
        item.PlanningKind=x.PlanningKind; item.Owner=x.Owner; item.ItemSource=x.ItemSource; item.TimePrecision=x.TimePrecision;
        item.Flexibility=x.Flexibility; item.DurationMinutes=x.DurationMinutes; item.SortOrder=x.SortOrder;
        item.ProviderPlaceId=x.ProviderPlaceId; item.Date=x.Date; item.StartsAt=x.StartsAt; item.EndsOn=x.EndsOn; item.EndsAt=x.EndsAt;
        item.TimeZoneId=x.TimeZoneId; item.Title=x.Title; item.City=x.City; item.LocationName=x.LocationName; item.Address=x.Address;
        item.ConfirmationCode=x.ConfirmationCode; item.Notes=x.Notes; item.Latitude=x.Latitude; item.Longitude=x.Longitude;
        item.SourceName=x.SourceName; item.SourceUrl=x.SourceUrl;
    }
    private sealed record ReservationSnapshot(Guid Id, Guid TripId, Guid? TripDayBlockId, Guid? RecommendationId,
        ReservationType Type, ScheduleItemKind PlanningKind, ItineraryItemOwner Owner, ItineraryItemSource ItemSource,
        ItineraryTimePrecision TimePrecision, ItineraryFlexibility Flexibility, int? DurationMinutes, int SortOrder,
        string? ProviderPlaceId, DateOnly Date, TimeOnly StartsAt, DateOnly? EndsOn, TimeOnly? EndsAt, string? TimeZoneId,
        string Title, string City, string LocationName, string Address, string ConfirmationCode, string Notes,
        decimal? Latitude, decimal? Longitude, string? SourceName, string? SourceUrl);
    private sealed record RouteLinkSnapshot(Guid StopId, Guid ItineraryItemId);
    private sealed record OperationSnapshot(IReadOnlyList<ReservationSnapshot> Reservations,
        IReadOnlyList<RouteLinkSnapshot> RouteLinks);
}

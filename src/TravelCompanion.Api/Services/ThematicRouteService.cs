using System.Text.Json;
using System.Data;
using Microsoft.EntityFrameworkCore;
using TravelCompanion.Api.Data;
using TravelCompanion.Api.Models;
using TravelCompanion.Shared;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Api.Services;

public sealed class ThematicRouteService(
    TravelCompanionDbContext dbContext,
    TravelerAccessService accessService,
    AssistantUsageService usage,
    DayProposalService proposals,
    Microsoft.Extensions.Options.IOptions<TravelCompanion.Api.Options.ProductFeatureOptions> features,
    ProductAnalyticsService? analytics = null,
    IGoogleRoutesService? routes = null,
    DeterministicDayPlanningEngine? planningEngine = null)
{
    private DeterministicDayPlanningEngine Planner { get; } = planningEngine ?? new DeterministicDayPlanningEngine();
    public async Task<IReadOnlyList<ThematicRouteDto>> ListAsync(HttpContext context, CancellationToken ct)
    {
        if (!features.Value.RoutesEnabled) throw new InvalidOperationException("Las rutas están desactivadas.");
        var access = await accessService.GetAsync(context, ct) ?? throw new UnauthorizedAccessException();
        var destinationId = access.TripId.HasValue
            ? await dbContext.Trips.Where(item => item.Id == access.TripId).Select(item => item.DestinationId).SingleAsync(ct)
            : await dbContext.BuilderAccessGrants.Where(item => item.AppUserId == access.User.Id)
                .OrderByDescending(item => item.CreatedAtUtc).Select(item => item.DestinationId).FirstAsync(ct);
        var routes = await dbContext.ThematicRoutes.AsNoTracking().Include(item => item.Stops).ThenInclude(stop => stop.Recommendation)
            .Include(item => item.Stops).ThenInclude(stop => stop.ItineraryItem)
            .Include(item => item.Applications).ThenInclude(item => item.Stops).ThenInclude(item => item.ItineraryItem)
            .Where(item => item.Status == RoutePublicationStatus.Published && item.DestinationId == destinationId
                && (item.Origin == RouteOrigin.Yuku || item.AppUserId == access.User.Id))
            .OrderBy(item => item.Origin).ThenBy(item => item.Name).ToListAsync(ct);
        var restrictedCatalog = access.Session.AccessMode is SessionAccessMode.FreeMapPreview or SessionAccessMode.BuilderReadOnly;
        return routes.Select(route => ToDto(route, restrictedCatalog && route.Origin == RouteOrigin.Yuku
            && route.AccessLevel == RouteAccessLevel.Premium)).ToList();
    }

    public async Task<ThematicRouteDto> CreateAsync(HttpContext context, CreateThematicRouteDto request, CancellationToken ct)
    {
        if (DbExecutionStrategy.ShouldExecute(dbContext))
            return await DbExecutionStrategy.ExecuteAsync(dbContext, () => CreateAsync(context, request, ct), ct);
        if (!features.Value.RoutesEnabled) throw new InvalidOperationException("Las rutas están desactivadas.");
        var access = await accessService.GetAsync(context, ct) ?? throw new UnauthorizedAccessException();
        if (!access.Capabilities.CanEditItinerary) throw new InvalidOperationException("Necesitas un pase activo para crear rutas.");
        if (access.TripId is null) throw new InvalidOperationException("Configura primero el viaje.");
        var pace = request.Pace.Trim().ToLowerInvariant();
        if (pace is not ("slow" or "balanced" or "fast"))
            throw new ArgumentException("El ritmo debe ser slow, balanced o fast.");
        var lease = await usage.ReserveAsync(access.User.Id, access.TripId, $"route:{request.Name}:{request.Date:yyyyMMdd}:{request.Theme}", ct);
        try
        {
            var trip = await dbContext.Trips.AsNoTracking().SingleAsync(item => item.Id == access.TripId && item.AppUserId == access.User.Id, ct);
            if (request.Date < trip.StartsOn || request.Date > trip.EndsOn)
                throw new ArgumentException("La fecha está fuera del viaje.");
            var window = Planner.CreateWindow(request.Date, request.WindowStart, request.WindowEnd);
            if (window.DurationMinutes is < 30 or > 24 * 60)
                throw new ArgumentException("La ventana de la ruta no es válida.");
            var normalizedCity = request.City.Trim().ToLowerInvariant();
            var query = dbContext.Recommendations.AsNoTracking().Where(item => item.DestinationId == trip.DestinationId
                && item.Neighborhood.ToLower().Contains(normalizedCity));
            if (access.Session.AccessMode == SessionAccessMode.FreeMapPreview) query = query.Where(item => item.AccessLevel == ContentAccessLevel.Free);
            var candidates = (await query.Take(100).ToListAsync(ct)).OrderByDescending(item => ThemeScore(item, request.Theme))
                .ThenByDescending(item => item.Rating).ThenBy(item => item.Title).Take(12).ToList();
            var available = window.DurationMinutes;
            var paceBuffer = pace switch { "slow" => 35, "fast" => 10, _ => 20 };
            var maximumStops = pace == "slow" ? 4 : 6;
            var selected = new List<Recommendation>();
            var transfers = new List<int?>();
            var usedMinutes = 0;
            foreach (var recommendation in candidates)
            {
                var duration = recommendation.SuggestedDurationMinutes > 0 ? recommendation.SuggestedDurationMinutes : 60;
                var transfer = selected.Count == 0 ? (int?)null
                    : await EstimateTransferAsync(selected[^1], recommendation, request.Date, request.WindowStart, ct);
                var required = duration + (selected.Count == 0 ? 0 : transfer ?? paceBuffer);
                if (usedMinutes + required > available) continue;
                selected.Add(recommendation); transfers.Add(transfer); usedMinutes += required;
                if (selected.Count == maximumStops) break;
            }
            if (selected.Count < 2) throw new InvalidOperationException("No hay al menos dos paradas compatibles con esa ventana.");
            var route = new ThematicRoute
            {
                Id = Guid.NewGuid(), AppUserId = access.User.Id, TripId = trip.Id, DestinationId = trip.DestinationId,
                Name = request.Name.Trim(), Theme = request.Theme, City = request.City.Trim(), Origin = RouteOrigin.Personal,
                Status = RoutePublicationStatus.Published, Version = 1,
                AccessLevel = RouteAccessLevel.Premium, PlannedDate = request.Date,
                WindowStart = request.WindowStart, WindowEnd = request.WindowEnd, Pace = pace,
                VisitMinutes = selected.Sum(item => item.SuggestedDurationMinutes > 0 ? item.SuggestedDurationMinutes : 60),
                EstimatedTransferMinutes = transfers.Skip(1).Sum(item => item ?? paceBuffer),
                WarningsJson = JsonSerializer.Serialize(RouteWarnings(context,
                    transfers.Skip(1).Any(item => !item.HasValue), true)),
                CreatedAtUtc = DateTimeOffset.UtcNow, UpdatedAtUtc = DateTimeOffset.UtcNow,
                Stops = selected.Select((item, index) => new ThematicRouteStop
                {
                    Id = Guid.NewGuid(), RecommendationId = item.Id,
                    DurationMinutes = item.SuggestedDurationMinutes > 0 ? item.SuggestedDurationMinutes : 60,
                    EstimatedTransferMinutes = transfers[index], SortOrder = index
                }).ToList()
            };
            await using var transaction = dbContext.Database.IsRelational()
                ? await dbContext.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct) : null;
            dbContext.ThematicRoutes.Add(route);
            await usage.CompleteAsync(lease.LeaseId, ct);
            await dbContext.SaveChangesAsync(ct);
            if (transaction is not null) await transaction.CommitAsync(ct);
            foreach (var stop in route.Stops) stop.Recommendation = selected.Single(item => item.Id == stop.RecommendationId);
            if (analytics is not null)
                await analytics.RecordServerEventAsync(access.User.Id, trip.Id, "route_saved", request.Theme.ToString(), null, ct);
            return ToDto(route, false);
        }
        catch
        {
            foreach (var entry in dbContext.ChangeTracker.Entries<ThematicRouteStop>().Where(item => item.State == EntityState.Added))
                entry.State = EntityState.Detached;
            foreach (var entry in dbContext.ChangeTracker.Entries<ThematicRoute>().Where(item => item.State == EntityState.Added))
                entry.State = EntityState.Detached;
            await usage.CancelAsync(lease.LeaseId, ct);
            throw;
        }
    }

    public async Task<ThematicRouteDto> UpdateAsync(HttpContext context, Guid id, UpdateThematicRouteDto request, CancellationToken ct)
    {
        if (!features.Value.RoutesEnabled) throw new InvalidOperationException("Las rutas están desactivadas.");
        var access = await accessService.GetAsync(context, ct) ?? throw new UnauthorizedAccessException();
        if (!access.Capabilities.CanEditItinerary) throw new InvalidOperationException("Necesitas acceso de edición para modificar rutas.");
        var session = access.Session;
        var route = await dbContext.ThematicRoutes.Include(item => item.Stops).ThenInclude(stop => stop.Recommendation)
            .Include(item => item.Stops).ThenInclude(stop => stop.ItineraryItem)
            .Include(item => item.Applications).ThenInclude(item => item.Stops).ThenInclude(item => item.ItineraryItem)
            .SingleOrDefaultAsync(item => item.Id == id && item.AppUserId == session.User.Id && item.Origin == RouteOrigin.Personal, ct)
            ?? throw new KeyNotFoundException();
        if (route.Version != request.ExpectedVersion) throw new InvalidOperationException("La ruta cambió en otro dispositivo.");
        var available = await dbContext.Recommendations.AsNoTracking().Where(item => request.OrderedRecommendationIds.Contains(item.Id)
            && item.DestinationId == route.DestinationId).ToDictionaryAsync(item => item.Id, ct);
        if (request.OrderedRecommendationIds.Count != request.OrderedRecommendationIds.Distinct().Count()
            || available.Count != request.OrderedRecommendationIds.Count || available.Count is < 2 or > 6)
            throw new ArgumentException("La ruta debe tener entre dos y seis paradas válidas.");
        var appliedItems = route.Stops
            .Where(stop => stop.ItineraryItemId.HasValue)
            .GroupBy(stop => stop.RecommendationId)
            .ToDictionary(group => group.Key, group => group.First().ItineraryItemId);
        dbContext.ThematicRouteStops.RemoveRange(route.Stops);
        var ordered = request.OrderedRecommendationIds.Select(id => available[id]).ToList();
        var transfers = new List<int?>();
        var plannedDate = route.PlannedDate ?? DateOnly.FromDateTime(DateTime.UtcNow);
        var plannedStart = route.WindowStart ?? new TimeOnly(9, 0);
        for (var index = 0; index < ordered.Count; index++)
            transfers.Add(index == 0 ? null : await EstimateTransferAsync(ordered[index - 1], ordered[index],
                plannedDate, plannedStart, ct));
        route.Stops = request.OrderedRecommendationIds.Select((recommendationId, index) => new ThematicRouteStop
        {
            Id = Guid.NewGuid(), ThematicRouteId = route.Id, RecommendationId = recommendationId,
            ItineraryItemId = appliedItems.GetValueOrDefault(recommendationId),
            DurationMinutes = Math.Max(15, available[recommendationId].SuggestedDurationMinutes),
            SortOrder = index, EstimatedTransferMinutes = transfers[index]
        }).ToList();
        route.Name = request.Name.Trim(); route.Version++; route.UpdatedAtUtc = DateTimeOffset.UtcNow;
        route.VisitMinutes = route.Stops.Sum(item => item.DurationMinutes);
        route.EstimatedTransferMinutes = transfers.Skip(1).Sum(item => item ?? PaceBuffer(route.Pace));
        route.WarningsJson = JsonSerializer.Serialize(RouteWarnings(context,
            transfers.Skip(1).Any(item => !item.HasValue), false));
        await dbContext.SaveChangesAsync(ct);
        foreach (var stop in route.Stops) stop.Recommendation = available[stop.RecommendationId];
        return ToDto(route, false);
    }

    public async Task<DayProposalDto> PrepareApplicationAsync(HttpContext context, Guid id, ApplyThematicRouteDto request, CancellationToken ct)
    {
        if (!features.Value.RoutesEnabled) throw new InvalidOperationException("Las rutas están desactivadas.");
        var access = await accessService.GetAsync(context, ct) ?? throw new UnauthorizedAccessException();
        if (!access.Capabilities.CanEditItinerary) throw new InvalidOperationException("Necesitas acceso de edición para aplicar rutas.");
        var session = access.Session;
        var route = await dbContext.ThematicRoutes.AsNoTracking().Include(item => item.Stops).ThenInclude(stop => stop.Recommendation)
            .Include(item => item.Stops).ThenInclude(stop => stop.ItineraryItem)
            .SingleOrDefaultAsync(item => item.Id == id
                && (item.AppUserId == session.User.Id || item.Origin == RouteOrigin.Yuku && item.Status == RoutePublicationStatus.Published), ct)
            ?? throw new KeyNotFoundException();
        if (access.Session.AccessMode == SessionAccessMode.FreeMapPreview && route.AccessLevel == RouteAccessLevel.Premium)
            throw new InvalidOperationException("Esta ruta editorial requiere un pase activo.");
        if (access.Session.AccessMode == SessionAccessMode.FreeMapPreview && route.Origin == RouteOrigin.Yuku)
            route.Stops = route.Stops.Where(item => item.Recommendation?.AccessLevel == ContentAccessLevel.Free).ToList();
        if (route.Stops.Count < 2) throw new InvalidOperationException("Esta ruta necesita más paradas disponibles con tu acceso actual.");
        if (route.Origin == RouteOrigin.Yuku)
        {
            var template = route;
            var now = DateTimeOffset.UtcNow;
            route = new ThematicRoute
            {
                Id = Guid.NewGuid(), AppUserId = access.User.Id, TripId = access.TripId,
                DestinationId = template.DestinationId, Name = template.Name, Theme = template.Theme,
                City = template.City, Origin = RouteOrigin.Personal, Status = RoutePublicationStatus.Published,
                Version = 1, AccessLevel = template.AccessLevel, TemplateSourceId = template.Id,
                TemplateVersion = template.Version, PlannedDate = request.Date,
                WindowStart = request.WindowStart, WindowEnd = request.WindowEnd, Pace = template.Pace,
                VisitMinutes = template.Stops.Sum(item => item.DurationMinutes),
                EstimatedTransferMinutes = template.Stops.Skip(1).Sum(item => item.EstimatedTransferMinutes ?? PaceBuffer(template.Pace)),
                WarningsJson = template.WarningsJson, CreatedAtUtc = now, UpdatedAtUtc = now,
                Stops = template.Stops.OrderBy(item => item.SortOrder).Select((item, index) => new ThematicRouteStop
                {
                    Id = Guid.NewGuid(), RecommendationId = item.RecommendationId, Recommendation = item.Recommendation,
                    DurationMinutes = item.DurationMinutes, EstimatedTransferMinutes = item.EstimatedTransferMinutes,
                    SortOrder = index
                }).ToList()
            };
            dbContext.ThematicRoutes.Add(route);
            await dbContext.SaveChangesAsync(ct);
        }
        return await proposals.CreateForRouteAsync(context, route, request, ct);
    }

    public async Task<ThematicRouteDto> CopyTemplateAsync(HttpContext context, Guid id, CancellationToken ct)
    {
        if (!features.Value.RoutesEnabled) throw new InvalidOperationException("Las rutas están desactivadas.");
        var access = await accessService.GetAsync(context, ct) ?? throw new UnauthorizedAccessException();
        if (!access.Capabilities.CanEditItinerary || access.TripId is null)
            throw new InvalidOperationException("Necesitas acceso de edición para guardar una ruta.");
        var template = await dbContext.ThematicRoutes.AsNoTracking().Include(item => item.Stops).ThenInclude(item => item.Recommendation)
            .SingleOrDefaultAsync(item => item.Id == id && item.Origin == RouteOrigin.Yuku
                && item.Status == RoutePublicationStatus.Published, ct) ?? throw new KeyNotFoundException();
        if (access.Session.AccessMode == SessionAccessMode.FreeMapPreview && template.AccessLevel == RouteAccessLevel.Premium)
            throw new InvalidOperationException("Esta ruta editorial requiere un pase activo.");
        var stops = template.Stops.OrderBy(item => item.SortOrder).ToList();
        if (access.Session.AccessMode == SessionAccessMode.FreeMapPreview)
            stops = stops.Where(item => item.Recommendation?.AccessLevel == ContentAccessLevel.Free).ToList();
        if (stops.Count < 2) throw new InvalidOperationException("Esta plantilla necesita más paradas disponibles con tu acceso actual.");
        var now = DateTimeOffset.UtcNow;
        var recommendations = stops.Where(item => item.Recommendation is not null)
            .ToDictionary(item => item.RecommendationId, item => item.Recommendation!);
        var route = new ThematicRoute
        {
            Id = Guid.NewGuid(), AppUserId = access.User.Id, TripId = access.TripId,
            DestinationId = template.DestinationId, Name = template.Name, Theme = template.Theme,
            City = template.City, Origin = RouteOrigin.Personal, Status = RoutePublicationStatus.Published,
            Version = 1, AccessLevel = template.AccessLevel, TemplateSourceId = template.Id, TemplateVersion = template.Version,
            PlannedDate = template.PlannedDate, WindowStart = template.WindowStart, WindowEnd = template.WindowEnd,
            Pace = template.Pace, VisitMinutes = stops.Sum(item => item.DurationMinutes),
            EstimatedTransferMinutes = stops.Sum(item => item.EstimatedTransferMinutes ?? 0),
            WarningsJson = template.WarningsJson, CreatedAtUtc = now, UpdatedAtUtc = now,
            Stops = stops.Select((item, index) => new ThematicRouteStop
            {
                Id = Guid.NewGuid(), RecommendationId = item.RecommendationId,
                DurationMinutes = item.DurationMinutes, EstimatedTransferMinutes = item.EstimatedTransferMinutes,
                SortOrder = index
            }).ToList()
        };
        dbContext.ThematicRoutes.Add(route);
        await dbContext.SaveChangesAsync(ct);
        foreach (var stop in route.Stops) stop.Recommendation = recommendations.GetValueOrDefault(stop.RecommendationId);
        return ToDto(route, false);
    }

    public async Task DeleteAsync(HttpContext context, Guid id, bool removeActivities, int? expectedRevision, CancellationToken ct)
    {
        if (DbExecutionStrategy.ShouldExecute(dbContext))
        {
            await DbExecutionStrategy.ExecuteAsync(dbContext,
                () => DeleteAsync(context, id, removeActivities, expectedRevision, ct), ct);
            return;
        }
        if (!features.Value.RoutesEnabled) throw new InvalidOperationException("Las rutas están desactivadas.");
        var access = await accessService.GetAsync(context, ct) ?? throw new UnauthorizedAccessException();
        if (removeActivities && !access.Capabilities.CanEditItinerary)
            throw new InvalidOperationException("Necesitas acceso de edición para retirar actividades del viaje.");
        await using var transaction = dbContext.Database.IsRelational()
            ? await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct) : null;
        var routeTripId = await dbContext.ThematicRoutes.Where(item => item.Id == id && item.AppUserId == access.User.Id)
            .Select(item => item.TripId).SingleOrDefaultAsync(ct);
        if (!routeTripId.HasValue) throw new KeyNotFoundException();
        await TripConcurrencyLock.LockAsync(dbContext, routeTripId.Value, ct);
        var route = await dbContext.ThematicRoutes
            .Include(item => item.Applications).ThenInclude(item => item.Stops).ThenInclude(item => item.ItineraryItem)
            .SingleOrDefaultAsync(item => item.Id == id && item.AppUserId == access.User.Id, ct)
            ?? throw new KeyNotFoundException();
        var trip = await dbContext.Trips.SingleAsync(item => item.Id == routeTripId.Value && item.AppUserId == access.User.Id, ct);
        if (removeActivities)
        {
            if (!expectedRevision.HasValue || trip.PlanRevision != expectedRevision.Value)
                throw new BuilderRevisionConflictException(trip.PlanRevision);
            var removable = route.Applications.SelectMany(item => item.Stops)
                .Select(item => item.ItineraryItem).OfType<Reservation>()
                .Where(item => item.Owner == ItineraryItemOwner.Traveler
                    && item.Flexibility == ItineraryFlexibility.Flexible)
                .DistinctBy(item => item.Id).ToList();
            dbContext.Reservations.RemoveRange(removable);
            if (removable.Count > 0)
            {
                trip.PlanRevision++;
                trip.UpdatedAtUtc = DateTimeOffset.UtcNow;
                dbContext.TripSynchronizationWorks.Add(new TripSynchronizationWork
                {
                    Id = Guid.NewGuid(), TripId = trip.Id, AppUserId = access.User.Id,
                    Revision = trip.PlanRevision, Kind = "route-activities-removed",
                    CreatedAtUtc = DateTimeOffset.UtcNow, NextAttemptAtUtc = DateTimeOffset.UtcNow
                });
            }
        }
        dbContext.ThematicRoutes.Remove(route);
        await dbContext.SaveChangesAsync(ct);
        if (transaction is not null) await transaction.CommitAsync(ct);
    }

    private static int ThemeScore(Recommendation item, RouteTheme theme)
    {
        var text = $"{item.Category} {item.RefinedType} {string.Join(' ', item.Tags)}".ToLowerInvariant();
        var terms = theme switch
        {
            RouteTheme.Food => new[] { "food", "restaurant", "cafe", "gastronom", "comida" },
            RouteTheme.HistoryAndTemples => new[] { "history", "temple", "shrine", "historia", "templo" },
            RouteTheme.ArtAndDesign => new[] { "art", "museum", "design", "arte", "museo" },
            RouteTheme.NatureAndGardens => new[] { "nature", "garden", "park", "naturaleza", "jardin" },
            _ => new[] { "shopping", "market", "neighborhood", "compras", "barrio" }
        };
        return terms.Count(text.Contains);
    }

    private static string[] RouteWarnings(HttpContext context, bool estimated, bool includesDefaultMinutes) =>
        ProductLanguage.IsSpanish(context)
            ? estimated
                ? [includesDefaultMinutes
                    ? "Algunos traslados no tienen una ruta disponible y usan una estimación de 20 minutos. Verifica horarios y disponibilidad."
                    : "Algunos traslados no tienen una ruta disponible y usan una estimación. Verifica horarios y disponibilidad."]
                : ["Los traslados proceden de estimaciones de ruta; verifica horarios y disponibilidad."]
            : estimated
                ? [includesDefaultMinutes
                    ? "Some transfers have no available route and use a 20-minute estimate. Check opening times and availability."
                    : "Some transfers have no available route and use an estimate. Check opening times and availability."]
                : ["Transfer times are route estimates. Check opening times and availability."];

    private static int PaceBuffer(string? pace) => pace?.Trim().ToLowerInvariant() switch
    {
        "slow" => 35,
        "fast" => 10,
        _ => 20
    };

    private async Task<int?> EstimateTransferAsync(Recommendation origin, Recommendation destination,
        DateOnly date, TimeOnly start, CancellationToken ct)
    {
        if (routes is null) return null;
        var from = new RouteWaypoint(origin.ProviderPlaceId, origin.Latitude, origin.Longitude);
        var to = new RouteWaypoint(destination.ProviderPlaceId, destination.Latitude, destination.Longitude);
        if (!from.IsValid || !to.IsValid) return null;
        var arrival = new DateTimeOffset(date.ToDateTime(start), TimeSpan.Zero).AddHours(2);
        return (await routes.EstimateAsync(from, to, "TRANSIT", arrival, ct))?.Minutes;
    }

    private static ThematicRouteDto ToDto(ThematicRoute route, bool summarize)
    {
        var latestApplication = route.Applications.OrderByDescending(item => item.CreatedAtUtc).FirstOrDefault();
        var latestLinks = latestApplication?.Stops.ToDictionary(item => item.ThematicRouteStopId) ?? [];
        return new(route.Id, route.Name, route.Theme, route.City,
            route.Origin, route.Version, route.VisitMinutes, route.EstimatedTransferMinutes,
            JsonSerializer.Deserialize<List<string>>(route.WarningsJson) ?? [],
            route.Stops.OrderBy(item => item.SortOrder).Take(summarize ? 2 : 6).Select(item =>
            {
                latestLinks.TryGetValue(item.Id, out var applied);
                return new RouteStopDto(item.Id, item.RecommendationId,
                    applied?.ItineraryItem?.Title ?? item.ItineraryItem?.Title ?? item.Recommendation?.Title ?? "Parada",
                    applied?.ItineraryItem?.DurationMinutes ?? item.ItineraryItem?.DurationMinutes ?? item.DurationMinutes,
                    item.SortOrder, item.EstimatedTransferMinutes, applied?.ItineraryItemId ?? item.ItineraryItemId);
            }).ToList(), route.Status,
            route.PlannedDate, route.WindowStart, route.WindowEnd, route.Pace, route.AccessLevel,
            route.TemplateSourceId, route.TemplateVersion,
            route.Applications.OrderByDescending(item => item.CreatedAtUtc).Select(item => new RouteApplicationDto(
                item.Id, item.Date, item.Version, item.Stops.ToDictionary(link => link.ThematicRouteStopId, link => link.ItineraryItemId))).ToList());
    }
}

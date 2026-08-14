using Microsoft.EntityFrameworkCore;
using TravelCompanion.Api.Data;
using TravelCompanion.Api.Models;
using TravelCompanion.Shared;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Api.Services;

public interface ITodayRecommendationService
{
    Task<TodayDto?> GetTodayAsync(
        AppUser user,
        Guid? sessionTripId,
        DateOnly? date,
        GeoPointDto? currentLocation,
        CancellationToken cancellationToken);

    Task<RecommendationSignalResponse> RecordSignalAsync(
        AppUser user,
        Guid recommendationId,
        Guid? sessionTripId,
        RecommendationSignalRequest request,
        CancellationToken cancellationToken);
}

public sealed class TodayRecommendationService(
    TravelCompanionDbContext dbContext,
    ILogger<TodayRecommendationService> logger) : ITodayRecommendationService
{
    private const string AutomaticSuggestionSourcePrefix = "today_auto:";
    private const int DefaultSuggestionsPerFreePeriod = 2;
    private const int MaxSuggestionsPerFreePeriod = 3;
    private static readonly int SuggestionsPerFreePeriod = Math.Clamp(
        DefaultSuggestionsPerFreePeriod,
        1,
        MaxSuggestionsPerFreePeriod);

    public async Task<TodayDto?> GetTodayAsync(
        AppUser user,
        Guid? sessionTripId,
        DateOnly? date,
        GeoPointDto? currentLocation,
        CancellationToken cancellationToken)
    {
        var trip = await LoadTripAsync(user.Id, sessionTripId, cancellationToken);
        if (trip?.Destination is null)
        {
            return null;
        }

        var selectedDate = date ?? ResolveDefaultDate(trip);
        if (selectedDate < trip.StartsOn || selectedDate > trip.EndsOn)
        {
            selectedDate = ResolveDefaultDate(trip);
        }

        var profile = await dbContext.TravelPreferenceProfiles
            .AsNoTracking()
            .FirstOrDefaultAsync(existing => existing.UserId == user.Id, cancellationToken);
        var entitlements = ToEntitlementsDto(user);
        var recommendations = await dbContext.Recommendations
            .AsNoTracking()
            .Include(recommendation => recommendation.Packages)
            .Where(recommendation => recommendation.DestinationId == trip.DestinationId)
            .ToListAsync(cancellationToken);
        var unlocked = recommendations
            .Where(recommendation => ContentAccessPolicy.IsRecommendationUnlocked(
                entitlements,
                recommendation.AccessLevel,
                recommendation.DestinationId,
                recommendation.Packages.Select(package => package.Id).ToList()))
            .ToList();

        var tripRecommendationIds = trip.Reservations
            .Where(reservation => reservation.RecommendationId.HasValue)
            .Select(reservation => reservation.RecommendationId!.Value)
            .ToHashSet();
        var visitedRecommendationIds = await LoadSignalIdsAsync(
            user.Id,
            trip.Id,
            RecommendationSignal.VisitedConfirmed,
            cancellationToken);
        var dismissedRecommendationIds = await LoadRecentDismissedIdsAsync(
            user.Id,
            trip.Id,
            cancellationToken);
        var persistedAssignments = await LoadAutomaticAssignmentsAsync(
            user.Id,
            trip.Id,
            cancellationToken);
        var allocatedRecommendationIds = new HashSet<Guid>();
        var blockedForNewAssignments = tripRecommendationIds
            .Concat(persistedAssignments.Values.SelectMany(assignments => assignments).Select(assignment => assignment.RecommendationId))
            .ToHashSet();
        var recommendationsById = recommendations.ToDictionary(recommendation => recommendation.Id);
        var unlockedById = unlocked.ToDictionary(recommendation => recommendation.Id);
        var sections = new List<TodaySectionDto>();
        var assignmentsAdded = 0;
        var selectedStoredIds = new HashSet<Guid>();
        var selectedMissingSuggestionCount = 0;
        foreach (var period in TodayPeriod.All)
        {
            var selectedBlock = FindDayBlock(trip, selectedDate, period.Key);
            var selectedPeriodHasPlan = trip.Reservations.Any(reservation =>
                    reservation.Date == selectedDate && period.Contains(reservation.StartsAt))
                || selectedBlock?.AutofillEnabled == false;
            if (selectedPeriodHasPlan)
            {
                continue;
            }

            var selectedKey = new TodayAssignmentKey(selectedDate, period.Key);
            var validStoredCount = (persistedAssignments.GetValueOrDefault(selectedKey) ?? [])
                .OrderBy(assignment => assignment.Rank)
                .Count(assignment => selectedStoredIds.Add(assignment.RecommendationId)
                    && !tripRecommendationIds.Contains(assignment.RecommendationId)
                    && unlockedById.ContainsKey(assignment.RecommendationId));
            selectedMissingSuggestionCount += Math.Max(0, SuggestionsPerFreePeriod - validStoredCount);
        }

        var availableNewSuggestionCount = unlocked.Count(recommendation =>
            !blockedForNewAssignments.Contains(recommendation.Id));
        var reservedNewSuggestionsForSelectedDate = Math.Min(
            selectedMissingSuggestionCount,
            availableNewSuggestionCount);

        for (var allocationDate = trip.StartsOn; allocationDate <= selectedDate; allocationDate = allocationDate.AddDays(1))
        {
            var allocationCity = ResolveCityForDate(trip, allocationDate);
            var allocationLocation = allocationDate == selectedDate ? currentLocation : null;
            foreach (var period in TodayPeriod.All)
            {
                var dayBlock = FindDayBlock(trip, allocationDate, period.Key);
                var periodItems = trip.Reservations
                    .Where(reservation => reservation.Date == allocationDate && period.Contains(reservation.StartsAt))
                    .OrderBy(reservation => reservation.StartsAt)
                    .ToList();
                var automaticSuggestions = new List<TodayRecommendationDto>();
                if (periodItems.Count == 0 && dayBlock?.AutofillEnabled != false)
                {
                    var assignmentKey = new TodayAssignmentKey(allocationDate, period.Key);
                    var storedForPeriod = persistedAssignments.GetValueOrDefault(assignmentKey) ?? [];
                    foreach (var stored in storedForPeriod.OrderBy(assignment => assignment.Rank))
                    {
                        if (automaticSuggestions.Count >= SuggestionsPerFreePeriod
                            || tripRecommendationIds.Contains(stored.RecommendationId)
                            || allocatedRecommendationIds.Contains(stored.RecommendationId)
                            || !unlockedById.TryGetValue(stored.RecommendationId, out var storedRecommendation))
                        {
                            continue;
                        }

                        automaticSuggestions.Add(CreateAutomaticSuggestion(
                            storedRecommendation,
                            period,
                            allocationCity,
                            profile,
                            allocationLocation,
                            visitedRecommendationIds,
                            dismissedRecommendationIds));
                        allocatedRecommendationIds.Add(stored.RecommendationId);
                    }

                    var missingCount = SuggestionsPerFreePeriod - automaticSuggestions.Count;
                    if (allocationDate != selectedDate && missingCount > 0)
                    {
                        var availableForHistoricalAllocation = unlocked.Count(recommendation =>
                            !blockedForNewAssignments.Contains(recommendation.Id)
                            && !allocatedRecommendationIds.Contains(recommendation.Id));
                        missingCount = Math.Min(
                            missingCount,
                            Math.Max(0, availableForHistoricalAllocation - reservedNewSuggestionsForSelectedDate));
                    }

                    if (missingCount > 0)
                    {
                        var newSuggestions = SelectSuggestions(
                            period,
                            allocationCity,
                            unlocked,
                            profile,
                            allocationLocation,
                            blockedForNewAssignments,
                            visitedRecommendationIds,
                            dismissedRecommendationIds,
                            allocatedRecommendationIds,
                            missingCount);
                        var nextRank = storedForPeriod.Count == 0
                            ? 1
                            : storedForPeriod.Max(assignment => assignment.Rank) + 1;
                        foreach (var suggestion in newSuggestions)
                        {
                            dbContext.RecommendationInteractionSignals.Add(CreateAutomaticAssignmentSignal(
                                user.Id,
                                trip.Id,
                                suggestion.Recommendation.Id,
                                allocationDate,
                                period.Key,
                                nextRank++));
                            assignmentsAdded++;
                        }

                        automaticSuggestions.AddRange(newSuggestions);
                    }
                }

                if (allocationDate != selectedDate)
                {
                    continue;
                }

                var reservations = periodItems
                    .Where(item => item.PlanningKind != ScheduleItemKind.Recommendation)
                    .Select(ToScheduleItemDto)
                    .ToList();
                var assignedRecommendations = periodItems
                    .Where(item => item.PlanningKind == ScheduleItemKind.Recommendation && item.RecommendationId.HasValue)
                    .Select(item => recommendationsById.GetValueOrDefault(item.RecommendationId!.Value))
                    .Where(recommendation => recommendation is not null)
                    .Select(recommendation => CreateAssignedRecommendation(
                        recommendation!,
                        period,
                        currentLocation,
                        visitedRecommendationIds))
                    .ToList();
                var recommendationsForSection = assignedRecommendations
                    .Concat(automaticSuggestions)
                    .ToList();

                sections.Add(new TodaySectionDto(
                    period.Key,
                    period.Label,
                    CreateDescription(period, dayBlock, periodItems, recommendationsForSection),
                    reservations,
                    recommendationsForSection));
            }
        }

        if (assignmentsAdded > 0)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        logger.LogInformation(
            "Today recommendations loaded. UserId={UserId}; TripId={TripId}; Date={Date}; Sections={SectionCount}; Suggestions={SuggestionCount}; HasLocation={HasLocation}.",
            user.Id,
            trip.Id,
            selectedDate,
            sections.Count,
            sections.Sum(section => section.Recommendations.Count),
            currentLocation is not null);

        return new TodayDto(DateTimeOffset.UtcNow, selectedDate, currentLocation, sections);
    }

    public async Task<RecommendationSignalResponse> RecordSignalAsync(
        AppUser user,
        Guid recommendationId,
        Guid? sessionTripId,
        RecommendationSignalRequest request,
        CancellationToken cancellationToken)
    {
        var trip = await LoadTripAsync(user.Id, sessionTripId, cancellationToken);
        if (trip is null)
        {
            return new RecommendationSignalResponse(false, "No encontre un viaje activo para registrar la senal.");
        }

        var recommendation = await dbContext.Recommendations
            .AsNoTracking()
            .Include(existing => existing.Packages)
            .FirstOrDefaultAsync(existing => existing.Id == recommendationId, cancellationToken);
        if (recommendation is null || recommendation.DestinationId != trip.DestinationId)
        {
            return new RecommendationSignalResponse(false, "La recomendacion no esta disponible para este viaje.");
        }

        if (!ContentAccessPolicy.IsRecommendationUnlocked(
            ToEntitlementsDto(user),
            recommendation.AccessLevel,
            recommendation.DestinationId,
            recommendation.Packages.Select(package => package.Id).ToList()))
        {
            return new RecommendationSignalResponse(false, "La recomendacion no esta disponible para tu cuenta.");
        }

        dbContext.RecommendationInteractionSignals.Add(new RecommendationInteractionSignal
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            TripId = trip.Id,
            RecommendationId = recommendationId,
            Signal = request.Signal,
            Source = TrimToMax(string.IsNullOrWhiteSpace(request.Source) ? "mobile" : request.Source.Trim(), 80),
            Latitude = request.Latitude,
            Longitude = request.Longitude,
            DistanceMeters = request.DistanceMeters,
            Confidence = request.Confidence,
            OccurredAtUtc = request.OccurredAtUtc ?? DateTimeOffset.UtcNow,
            CreatedAtUtc = DateTimeOffset.UtcNow
        });

        await dbContext.SaveChangesAsync(cancellationToken);
        return new RecommendationSignalResponse(true, CreateSignalMessage(request.Signal));
    }

    private IReadOnlyList<TodayRecommendationDto> SelectSuggestions(
        TodayPeriod period,
        string selectedCity,
        IReadOnlyList<Recommendation> recommendations,
        TravelPreferenceProfile? profile,
        GeoPointDto? currentLocation,
        ISet<Guid> tripRecommendationIds,
        ISet<Guid> visitedRecommendationIds,
        ISet<Guid> dismissedRecommendationIds,
        ISet<Guid> allocatedRecommendationIds,
        int maxSuggestions = DefaultSuggestionsPerFreePeriod)
    {
        var scored = recommendations
            .Where(recommendation => !tripRecommendationIds.Contains(recommendation.Id))
            .Where(recommendation => !allocatedRecommendationIds.Contains(recommendation.Id))
            .Select(recommendation => ScoreRecommendation(
                recommendation,
                period,
                selectedCity,
                profile,
                currentLocation,
                visitedRecommendationIds.Contains(recommendation.Id),
                dismissedRecommendationIds.Contains(recommendation.Id)))
            .Where(candidate => candidate.Score > -50)
            .OrderBy(candidate => candidate.IsVisited)
            .ThenByDescending(candidate => candidate.Score)
            .ThenBy(candidate => candidate.DistanceKm ?? decimal.MaxValue)
            .ThenBy(candidate => candidate.Recommendation.Title)
            .Take(Math.Clamp(maxSuggestions, 0, MaxSuggestionsPerFreePeriod))
            .ToList();

        foreach (var candidate in scored)
        {
            allocatedRecommendationIds.Add(candidate.Recommendation.Id);
        }

        return scored
            .Select(candidate => new TodayRecommendationDto(
                ToRecommendationDto(candidate.Recommendation, candidate.DistanceKm),
                candidate.DistanceKm,
                candidate.Reason,
                candidate.IsVisited,
                candidate.IsVisited ? "Ya visitado" : null,
                period.Label))
            .ToList();
    }

    private static TodayRecommendationDto CreateAutomaticSuggestion(
        Recommendation recommendation,
        TodayPeriod period,
        string selectedCity,
        TravelPreferenceProfile? profile,
        GeoPointDto? currentLocation,
        ISet<Guid> visitedRecommendationIds,
        ISet<Guid> dismissedRecommendationIds)
    {
        var candidate = ScoreRecommendation(
            recommendation,
            period,
            selectedCity,
            profile,
            currentLocation,
            visitedRecommendationIds.Contains(recommendation.Id),
            dismissedRecommendationIds.Contains(recommendation.Id));
        return new TodayRecommendationDto(
            ToRecommendationDto(recommendation, candidate.DistanceKm),
            candidate.DistanceKm,
            candidate.Reason,
            candidate.IsVisited,
            candidate.IsVisited ? "Ya visitado" : null,
            period.Label);
    }

    private static TodayRecommendationDto CreateAssignedRecommendation(
        Recommendation recommendation,
        TodayPeriod period,
        GeoPointDto? currentLocation,
        ISet<Guid> visitedRecommendationIds)
    {
        var distanceKm = CalculateDistanceKm(currentLocation, recommendation);
        var isVisited = visitedRecommendationIds.Contains(recommendation.Id);
        return new TodayRecommendationDto(
            ToRecommendationDto(recommendation, distanceKm),
            distanceKm,
            "Seleccionada para este bloque",
            isVisited,
            isVisited ? "Ya visitado" : null,
            period.Label,
            IsAssigned: true);
    }

    private static ScoredTodayRecommendation ScoreRecommendation(
        Recommendation recommendation,
        TodayPeriod period,
        string selectedCity,
        TravelPreferenceProfile? profile,
        GeoPointDto? currentLocation,
        bool isVisited,
        bool isDismissed)
    {
        var text = CreateSearchableText(recommendation);
        if (ContainsAny(text, profile?.Dislikes) || ContainsAny(text, profile?.DietaryRestrictions))
        {
            return new ScoredTodayRecommendation(recommendation, -100, null, "No encaja con tus preferencias", isVisited);
        }

        var score = 0;
        var reasons = new List<string>();
        var keywordMatches = period.Keywords.Count(keyword => text.Contains(keyword, StringComparison.OrdinalIgnoreCase));
        if (keywordMatches > 0)
        {
            score += keywordMatches * 8;
            reasons.Add($"encaja con {period.Label.ToLowerInvariant()}");
        }

        if (!string.IsNullOrWhiteSpace(selectedCity)
            && text.Contains(selectedCity.ToLowerInvariant(), StringComparison.OrdinalIgnoreCase))
        {
            score += 8;
            reasons.Add($"queda en {selectedCity}");
        }

        var distanceKm = CalculateDistanceKm(currentLocation, recommendation);
        if (distanceKm is <= 1)
        {
            score += 18;
            reasons.Add("esta muy cerca");
        }
        else if (distanceKm is <= 3)
        {
            score += 12;
            reasons.Add("queda cerca");
        }
        else if (distanceKm is <= 6)
        {
            score += 6;
        }
        else if (distanceKm.HasValue)
        {
            score -= Math.Min(12, (int)Math.Floor(distanceKm.Value));
        }

        if (profile is not null)
        {
            score += ScoreProfileMatch(text, recommendation, profile, reasons);
        }

        if (recommendation.SuggestedDurationMinutes is > 0 and <= 120)
        {
            score += 3;
        }

        if (recommendation.Rating is >= 4)
        {
            score += 2;
        }

        if (isDismissed)
        {
            score -= 35;
        }

        if (isVisited)
        {
            score -= 80;
        }

        return new ScoredTodayRecommendation(
            recommendation,
            score,
            distanceKm,
            reasons.FirstOrDefault() ?? "buena opcion para este hueco libre",
            isVisited);
    }

    private static int ScoreProfileMatch(
        string text,
        Recommendation recommendation,
        TravelPreferenceProfile profile,
        List<string> reasons)
    {
        var score = 0;
        if (ContainsAny(text, profile.Interests))
        {
            score += 10;
            reasons.Add("coincide con tus intereses");
        }

        if (ContainsAny(text, profile.FoodPreferences))
        {
            score += 8;
            reasons.Add("coincide con tus gustos de comida");
        }

        if (string.Equals(profile.BudgetLevel, recommendation.PriceLevel, StringComparison.OrdinalIgnoreCase))
        {
            score += 6;
            reasons.Add("encaja con tu presupuesto");
        }

        if (string.Equals(profile.TravelPace, "relaxed", StringComparison.OrdinalIgnoreCase)
            && recommendation.SuggestedDurationMinutes <= 75)
        {
            score += 4;
        }

        return score;
    }

    private async Task<Trip?> LoadTripAsync(
        Guid userId,
        Guid? sessionTripId,
        CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var query = dbContext.Trips
            .Include(trip => trip.Destination)
            .Include(trip => trip.Reservations)
            .Include(trip => trip.DayPlans)
                .ThenInclude(day => day.Blocks)
            .Where(trip => trip.AppUserId == userId
                && trip.PublicationStatus == TripPublicationStatus.Published);

        if (sessionTripId.HasValue)
        {
            query = query.Where(trip => trip.Id == sessionTripId.Value);
        }

        return await query
            .OrderBy(trip => trip.StartsOn < today)
            .ThenBy(trip => trip.StartsOn)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private static DateOnly ResolveDefaultDate(Trip trip)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        if (today >= trip.StartsOn && today <= trip.EndsOn)
        {
            return today;
        }

        return trip.StartsOn;
    }

    private static string ResolveCityForDate(Trip trip, DateOnly date)
    {
        var plannedCity = trip.DayPlans
            .Where(day => day.Date == date)
            .Select(day => NormalizeCity(day.City))
            .FirstOrDefault(city => !string.Equals(city, "Unknown City", StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(plannedCity))
        {
            return plannedCity;
        }

        var sameDayCity = trip.Reservations
            .Where(reservation => reservation.Date == date)
            .Select(reservation => NormalizeCity(reservation.City))
            .FirstOrDefault(city => !string.Equals(city, "Unknown City", StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(sameDayCity))
        {
            return sameDayCity;
        }

        var stayCity = trip.Reservations
            .Where(reservation => reservation.Type == ReservationType.Lodging)
            .Where(reservation => reservation.Date <= date && (reservation.EndsOn is null || reservation.EndsOn >= date))
            .Select(reservation => NormalizeCity(reservation.City))
            .FirstOrDefault(city => !string.Equals(city, "Unknown City", StringComparison.OrdinalIgnoreCase));

        return string.IsNullOrWhiteSpace(stayCity)
            ? trip.Destination?.Name ?? string.Empty
            : stayCity;
    }

    private async Task<HashSet<Guid>> LoadSignalIdsAsync(
        Guid userId,
        Guid tripId,
        RecommendationSignal signal,
        CancellationToken cancellationToken)
    {
        return (await dbContext.RecommendationInteractionSignals
                .AsNoTracking()
                .Where(existing => existing.UserId == userId
                    && existing.TripId == tripId
                    && existing.Signal == signal)
                .Select(existing => existing.RecommendationId)
                .Distinct()
                .ToListAsync(cancellationToken))
            .ToHashSet();
    }

    private async Task<HashSet<Guid>> LoadRecentDismissedIdsAsync(
        Guid userId,
        Guid tripId,
        CancellationToken cancellationToken)
    {
        var cutoff = DateTimeOffset.UtcNow.AddHours(-24);
        return (await dbContext.RecommendationInteractionSignals
                .AsNoTracking()
                .Where(existing => existing.UserId == userId
                    && existing.TripId == tripId
                    && existing.Signal == RecommendationSignal.Dismissed
                    && existing.CreatedAtUtc >= cutoff)
                .Select(existing => existing.RecommendationId)
                .Distinct()
                .ToListAsync(cancellationToken))
            .ToHashSet();
    }

    private async Task<Dictionary<TodayAssignmentKey, List<PersistedTodayAssignment>>> LoadAutomaticAssignmentsAsync(
        Guid userId,
        Guid tripId,
        CancellationToken cancellationToken)
    {
        var storedSignals = await dbContext.RecommendationInteractionSignals
            .AsNoTracking()
            .Where(signal => signal.UserId == userId
                && signal.TripId == tripId
                && signal.Signal == RecommendationSignal.Suggested
                && signal.Source.StartsWith(AutomaticSuggestionSourcePrefix))
            .Select(signal => new { signal.RecommendationId, signal.Source, signal.CreatedAtUtc })
            .ToListAsync(cancellationToken);

        return storedSignals
            .Select(signal => TryParseAutomaticAssignment(
                signal.RecommendationId,
                signal.Source,
                signal.CreatedAtUtc))
            .Where(assignment => assignment is not null)
            .Select(assignment => assignment!)
            .GroupBy(assignment => new { assignment.Date, assignment.PeriodKey, assignment.Rank })
            .Select(group => group.OrderByDescending(assignment => assignment.CreatedAtUtc).First())
            .GroupBy(assignment => new TodayAssignmentKey(assignment.Date, assignment.PeriodKey))
            .ToDictionary(group => group.Key, group => group.OrderBy(assignment => assignment.Rank).ToList());
    }

    private static RecommendationInteractionSignal CreateAutomaticAssignmentSignal(
        Guid userId,
        Guid tripId,
        Guid recommendationId,
        DateOnly date,
        string periodKey,
        int rank)
    {
        var now = DateTimeOffset.UtcNow;
        return new RecommendationInteractionSignal
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TripId = tripId,
            RecommendationId = recommendationId,
            Signal = RecommendationSignal.Suggested,
            Source = $"{AutomaticSuggestionSourcePrefix}{date:yyyyMMdd}:{periodKey}:{rank}",
            OccurredAtUtc = now,
            CreatedAtUtc = now
        };
    }

    private static PersistedTodayAssignment? TryParseAutomaticAssignment(
        Guid recommendationId,
        string source,
        DateTimeOffset createdAtUtc)
    {
        if (!source.StartsWith(AutomaticSuggestionSourcePrefix, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var parts = source[AutomaticSuggestionSourcePrefix.Length..].Split(':', StringSplitOptions.TrimEntries);
        if (parts.Length != 3
            || !DateOnly.TryParseExact(parts[0], "yyyyMMdd", out var date)
            || string.IsNullOrWhiteSpace(parts[1])
            || !int.TryParse(parts[2], out var rank)
            || rank <= 0)
        {
            return null;
        }

        return new PersistedTodayAssignment(date, parts[1], rank, recommendationId, createdAtUtc);
    }

    private static ScheduleItemDto ToScheduleItemDto(Reservation reservation) =>
        new(
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
            reservation.PlanningKind,
            reservation.Owner,
            reservation.ItemSource,
            reservation.TimePrecision,
            reservation.SortOrder,
            reservation.ProviderPlaceId);

    private static RecommendationDto ToRecommendationDto(Recommendation recommendation, decimal? distanceKm) =>
        new(
            recommendation.Id,
            recommendation.DestinationId,
            recommendation.Title,
            recommendation.Category,
            recommendation.Neighborhood,
            recommendation.Description,
            recommendation.Tags,
            recommendation.PriceLevel,
            recommendation.Latitude,
            recommendation.Longitude,
            recommendation.SuggestedDurationMinutes,
            recommendation.Rating,
            recommendation.OpeningHours,
            recommendation.AccessLevel,
            recommendation.Packages.Select(package => package.Id).ToList(),
            distanceKm);

    private static string CreateDescription(
        TodayPeriod period,
        TripDayBlock? block,
        IReadOnlyList<Reservation> reservations,
        IReadOnlyList<TodayRecommendationDto> suggestions)
    {
        if (!string.IsNullOrWhiteSpace(block?.CuratedDescription))
        {
            return block.CuratedDescription;
        }

        if (reservations.Count > 0)
        {
            var curatedDescription = reservations
                .Where(reservation => string.Equals(reservation.SourceName, TripWorkbookImportService.SourceName, StringComparison.OrdinalIgnoreCase))
                .Select(reservation => ExtractCuratedDescription(reservation.Notes))
                .FirstOrDefault(description => !string.IsNullOrWhiteSpace(description));
            if (!string.IsNullOrWhiteSpace(curatedDescription))
            {
                return curatedDescription;
            }

            return string.Empty;
        }

        return suggestions.Count > 0
            ? $"{period.Label}: hueco libre. Te dejo opciones cortas de nuestra base para completar el dia sin llenarlo de mas."
            : $"{period.Label}: hueco libre, sin recomendaciones suficientemente buenas por ahora.";
    }

    private static TripDayBlock? FindDayBlock(Trip trip, DateOnly date, string periodKey) =>
        trip.DayPlans
            .FirstOrDefault(day => day.Date == date)?
            .Blocks.FirstOrDefault(block => string.Equals(block.PeriodKey, periodKey, StringComparison.OrdinalIgnoreCase));

    private static string ExtractCuratedDescription(string notes)
    {
        if (string.IsNullOrWhiteSpace(notes))
        {
            return string.Empty;
        }

        const string prefix = "Descripcion:";
        var index = notes.IndexOf(prefix, StringComparison.OrdinalIgnoreCase);
        if (index < 0)
        {
            return string.Empty;
        }

        var description = notes[(index + prefix.Length)..];
        var separatorIndex = description.IndexOf(" | ", StringComparison.Ordinal);
        return separatorIndex >= 0
            ? description[..separatorIndex].Trim()
            : description.Trim();
    }

    private static decimal? CalculateDistanceKm(GeoPointDto? origin, Recommendation recommendation)
    {
        if (origin is null)
        {
            return null;
        }

        const double earthRadiusKm = 6371;
        static double ToRadians(decimal degrees) => (double)degrees * Math.PI / 180;

        var latitudeDelta = ToRadians(recommendation.Latitude - origin.Latitude);
        var longitudeDelta = ToRadians(recommendation.Longitude - origin.Longitude);
        var originLatitudeRadians = ToRadians(origin.Latitude);
        var targetLatitudeRadians = ToRadians(recommendation.Latitude);
        var a = Math.Sin(latitudeDelta / 2) * Math.Sin(latitudeDelta / 2)
            + Math.Cos(originLatitudeRadians) * Math.Cos(targetLatitudeRadians)
            * Math.Sin(longitudeDelta / 2) * Math.Sin(longitudeDelta / 2);
        var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
        return Math.Round((decimal)(earthRadiusKm * c), 1);
    }

    private static UserEntitlementsDto ToEntitlementsDto(AppUser user)
    {
        var now = DateTimeOffset.UtcNow;
        var activeEntitlements = user.Entitlements
            .Where(entitlement => entitlement.ExpiresAt is null || entitlement.ExpiresAt > now)
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

    private static bool ContainsAny(string text, IReadOnlyList<string>? values)
    {
        return values is not null
            && values.Any(value => !string.IsNullOrWhiteSpace(value)
                && text.Contains(value.Trim().ToLowerInvariant(), StringComparison.OrdinalIgnoreCase));
    }

    private static string CreateSearchableText(Recommendation recommendation) =>
        string.Join(
            ' ',
            recommendation.Title,
            recommendation.Category,
            recommendation.Neighborhood,
            recommendation.Description,
            recommendation.PriceLevel,
            string.Join(' ', recommendation.Tags))
        .ToLowerInvariant();

    private static string NormalizeCity(string? city) =>
        string.IsNullOrWhiteSpace(city) ? "Unknown City" : city.Trim();

    private static string TrimToMax(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength];

    private static string CreateSignalMessage(RecommendationSignal signal) =>
        signal switch
        {
            RecommendationSignal.VisitedConfirmed => "Listo, marque esta recomendacion como visitada.",
            RecommendationSignal.Dismissed => "Listo, la voy a bajar para hoy.",
            RecommendationSignal.Saved => "Listo, registre que guardaste esta recomendacion.",
            RecommendationSignal.Viewed => "Listo, registre la vista.",
            RecommendationSignal.VisitedCandidate => "Listo, registre la posible visita.",
            _ => "Listo, registre la senal."
        };

    private sealed record ScoredTodayRecommendation(
        Recommendation Recommendation,
        int Score,
        decimal? DistanceKm,
        string Reason,
        bool IsVisited);

    private sealed record TodayAssignmentKey(DateOnly Date, string PeriodKey);

    private sealed record PersistedTodayAssignment(
        DateOnly Date,
        string PeriodKey,
        int Rank,
        Guid RecommendationId,
        DateTimeOffset CreatedAtUtc);

    private sealed record TodayPeriod(
        string Key,
        string Label,
        TimeOnly Start,
        TimeOnly End,
        IReadOnlyList<string> Keywords)
    {
        public static IReadOnlyList<TodayPeriod> All { get; } =
        [
            new("morning", "Mañana", new TimeOnly(5, 0), new TimeOnly(12, 0), ["coffee", "cafe", "breakfast", "desayuno", "temple", "shrine", "market", "walk", "culture"]),
            new("midday", "Medio día", new TimeOnly(12, 0), new TimeOnly(15, 0), ["food", "lunch", "almuerzo", "ramen", "sushi", "restaurant", "shopping", "market"]),
            new("afternoon", "Tarde", new TimeOnly(15, 0), new TimeOnly(20, 0), ["walk", "culture", "shopping", "museum", "garden", "route", "tea", "cafe"]),
            new("night", "Noche", new TimeOnly(20, 0), new TimeOnly(5, 0), ["dinner", "cena", "bar", "night", "izakaya", "food", "view", "dance"])
        ];

        public bool Contains(TimeOnly time)
        {
            return Start < End
                ? time >= Start && time < End
                : time >= Start || time < End;
        }
    }
}

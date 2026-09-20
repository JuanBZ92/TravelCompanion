using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Shared;

public static class ScheduleReviewAnalyzer
{
    private const int TransferSafetyMinutes = 10;

    public static IReadOnlyList<DayReviewDto> Analyze(
        IReadOnlyList<ScheduleItemDto>? items,
        DateOnly startsOn,
        DateOnly endsOn)
    {
        if (endsOn < startsOn)
        {
            return [];
        }

        var source = items ?? [];
        var reviews = new List<DayReviewDto>();
        for (var date = startsOn; date <= endsOn; date = date.AddDays(1))
        {
            reviews.Add(AnalyzeDay(date, source.Where(item => CoversDate(item, date)).ToList()));
        }

        return reviews;
    }

    public static DayReviewDto AnalyzeDay(DateOnly date, IReadOnlyList<ScheduleItemDto>? items)
    {
        var timedItems = (items ?? [])
            .Where(HasFixedSchedule)
            .OrderBy(item => GetStart(item, date))
            .ThenBy(item => item.SortOrder)
            .ToList();
        var issues = new List<DayReviewIssueDto>();

        AddIncompleteInformationIssues(timedItems, issues);
        AddOverlapIssues(date, timedItems, issues);
        AddTransferIssues(date, timedItems, issues);
        AddPackedDayIssue(date, timedItems, issues);

        var orderedIssues = issues
            .OrderBy(issue => SeverityOrder(issue.Severity))
            .ThenBy(issue => issue.Kind, StringComparer.Ordinal)
            .ToList();
        if (orderedIssues.Count == 0)
        {
            var summary = timedItems.Count switch
            {
                0 => "No hay horarios fijos que puedan entrar en conflicto.",
                1 => "Tu único plan con horario tiene margen suficiente.",
                _ => $"Tus {timedItems.Count} planes con horario tienen margen suficiente."
            };
            return new(date, DayReviewStatuses.Balanced, "Día bien equilibrado", summary, []);
        }

        var criticalCount = orderedIssues.Count(issue => issue.Severity == DayReviewSeverities.Critical);
        var status = criticalCount > 0 ? DayReviewStatuses.NeedsAttention : DayReviewStatuses.Tight;
        var title = criticalCount > 0 ? "Tu día necesita ajustes" : "Tu día tiene poco margen";
        var summaryText = orderedIssues.Count == 1
            ? "Encontramos 1 punto que conviene revisar."
            : $"Encontramos {orderedIssues.Count} puntos que conviene revisar.";
        return new(date, status, title, summaryText, orderedIssues);
    }

    public static bool HasTimedReservation(DateOnly date, IReadOnlyList<ScheduleItemDto>? items) =>
        (items ?? []).Any(item => CoversDate(item, date) && IsTimedReservation(item));

    private static void AddIncompleteInformationIssues(
        IReadOnlyList<ScheduleItemDto> timedItems,
        ICollection<DayReviewIssueDto> issues)
    {
        var missingEnd = timedItems.Where(item => !item.EndsAt.HasValue).ToList();
        if (missingEnd.Count == 0) return;
        issues.Add(new(DayReviewIssueKinds.IncompleteInformation, DayReviewSeverities.Info,
            "Información incompleta",
            $"Hay {missingEnd.Count} {(missingEnd.Count == 1 ? "reserva" : "reservas")} sin hora de fin; no podemos comprobar todos los conflictos.",
            missingEnd.Select(item => item.Id).ToList()));
    }

    private static bool HasFixedSchedule(ScheduleItemDto item) =>
        item.Type != ReservationType.Lodging
        && item.HasExactTime
        && (item.Type == ReservationType.Flight
            || item.PlanningKind == ScheduleItemKind.ConfirmedReservation
            || item.Owner == ItineraryItemOwner.Yuku
            || item.Flexibility is ItineraryFlexibility.FixedByTraveler
                or ItineraryFlexibility.ConfirmedReservation);

    private static bool IsTimedReservation(ScheduleItemDto item) =>
        item.Type != ReservationType.Lodging
        && item.HasExactTime
        && (item.Type == ReservationType.Flight
            || item.PlanningKind == ScheduleItemKind.ConfirmedReservation
            || item.Flexibility == ItineraryFlexibility.ConfirmedReservation);

    private static void AddOverlapIssues(DateOnly date,
        IReadOnlyList<ScheduleItemDto> items,
        ICollection<DayReviewIssueDto> issues)
    {
        for (var index = 0; index < items.Count; index++)
        {
            var current = items[index];
            var currentStart = GetStart(current, date);
            var currentEnd = GetExplicitEnd(current, date);
            if (!currentEnd.HasValue)
            {
                continue;
            }

            for (var candidateIndex = index + 1; candidateIndex < items.Count; candidateIndex++)
            {
                var candidate = items[candidateIndex];
                var candidateStart = GetStart(candidate, date);
                if (candidateStart >= currentEnd.Value)
                {
                    break;
                }

                var candidateEnd = GetExplicitEnd(candidate, date) ?? currentEnd.Value;
                var overlapStart = currentStart > candidateStart ? currentStart : candidateStart;
                var overlapEnd = currentEnd.Value < candidateEnd ? currentEnd.Value : candidateEnd;
                var overlapMinutes = Math.Max(1, (int)Math.Ceiling((overlapEnd - overlapStart).TotalMinutes));
                issues.Add(new(
                    DayReviewIssueKinds.Overlap,
                    DayReviewSeverities.Critical,
                    "Horarios solapados",
                    $"{current.Title} y {candidate.Title} se pisan durante {overlapMinutes} min.",
                    [current.Id, candidate.Id],
                    AvailableMinutes: -overlapMinutes,
                    RecommendedMinutes: 0));
            }
        }
    }

    private static void AddTransferIssues(DateOnly date,
        IReadOnlyList<ScheduleItemDto> items,
        ICollection<DayReviewIssueDto> issues)
    {
        for (var index = 0; index < items.Count - 1; index++)
        {
            var origin = items[index];
            var destination = items[index + 1];
            var originEnd = GetExplicitEnd(origin, date);
            if (!originEnd.HasValue)
            {
                continue;
            }

            var availableMinutes = (int)Math.Floor((GetStart(destination, date) - originEnd.Value).TotalMinutes);
            if (availableMinutes < 0)
            {
                continue;
            }

            var travelMinutes = EstimateTravelMinutes(origin, destination);
            if (!travelMinutes.HasValue)
            {
                continue;
            }

            var recommendedMinutes = travelMinutes.Value + TransferSafetyMinutes;
            if (availableMinutes >= recommendedMinutes)
            {
                continue;
            }

            var missingMinutes = recommendedMinutes - availableMinutes;
            issues.Add(new(
                DayReviewIssueKinds.TightTransfer,
                missingMinutes >= 30 ? DayReviewSeverities.Critical : DayReviewSeverities.Warning,
                "Traslado estimado con poco margen",
                $"Entre {origin.Title} y {destination.Title} tienes {availableMinutes} min; el traslado estimado necesita unos {recommendedMinutes} min.",
                [origin.Id, destination.Id],
                availableMinutes,
                recommendedMinutes));
        }
    }

    private static void AddPackedDayIssue(DateOnly date,
        IReadOnlyList<ScheduleItemDto> items,
        ICollection<DayReviewIssueDto> issues)
    {
        if (items.Count == 0)
        {
            return;
        }

        var explicitMinutes = items
            .Select(item => (Start: GetStart(item, date), End: GetExplicitEnd(item, date)))
            .Where(window => window.End.HasValue && window.End.Value > window.Start)
            .Sum(window => (window.End!.Value - window.Start).TotalMinutes);
        var spanMinutes = (GetStart(items[^1], date) - GetStart(items[0], date)).TotalMinutes;
        if (items.Count < 5 && explicitMinutes < 8 * 60 && (items.Count < 4 || spanMinutes < 12 * 60))
        {
            return;
        }

        issues.Add(new(
            DayReviewIssueKinds.PackedDay,
            DayReviewSeverities.Warning,
            "Día muy cargado",
            $"Tienes {items.Count} planes con horario. Deja un bloque libre para descansos e imprevistos.",
            items.Select(item => item.Id).ToList()));
    }

    private static int? EstimateTravelMinutes(ScheduleItemDto origin, ScheduleItemDto destination)
    {
        if (origin.Latitude.HasValue && origin.Longitude.HasValue
            && destination.Latitude.HasValue && destination.Longitude.HasValue)
        {
            var distanceKm = HaversineKm(
                origin.Latitude.Value,
                origin.Longitude.Value,
                destination.Latitude.Value,
                destination.Longitude.Value);
            var minutes = distanceKm <= 2
                ? distanceKm * 1.25 / 4.5 * 60 + 5
                : distanceKm * 1.25 / 22 * 60 + 15;
            return Math.Max(10, (int)Math.Ceiling(minutes));
        }

        if (!string.IsNullOrWhiteSpace(origin.City)
            && !string.IsNullOrWhiteSpace(destination.City)
            && !string.Equals(origin.City.Trim(), destination.City.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return 90;
        }

        return null;
    }

    private static double HaversineKm(decimal originLatitude, decimal originLongitude, decimal destinationLatitude, decimal destinationLongitude)
    {
        const double earthRadiusKm = 6371;
        static double Radians(decimal degrees) => (double)degrees * Math.PI / 180;

        var latitudeDelta = Radians(destinationLatitude - originLatitude);
        var longitudeDelta = Radians(destinationLongitude - originLongitude);
        var originLatitudeRadians = Radians(originLatitude);
        var destinationLatitudeRadians = Radians(destinationLatitude);
        var a = Math.Sin(latitudeDelta / 2) * Math.Sin(latitudeDelta / 2)
            + Math.Cos(originLatitudeRadians) * Math.Cos(destinationLatitudeRadians)
            * Math.Sin(longitudeDelta / 2) * Math.Sin(longitudeDelta / 2);
        return earthRadiusKm * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
    }

    private static bool CoversDate(ScheduleItemDto item, DateOnly date)
    {
        var endDate = ResolveEndDate(item);
        return item.Date <= date && endDate >= date;
    }

    private static DateOnly ResolveEndDate(ScheduleItemDto item) => item.EndsOn
        ?? (item.EndsAt.HasValue && item.EndsAt.Value <= item.StartsAt ? item.Date.AddDays(1) : item.Date);

    private static DateTime GetStart(ScheduleItemDto item, DateOnly date) => item.Date < date
        ? date.ToDateTime(TimeOnly.MinValue)
        : item.Date.ToDateTime(item.StartsAt);

    private static DateTime? GetExplicitEnd(ScheduleItemDto item, DateOnly date)
    {
        if (!item.EndsAt.HasValue)
        {
            return null;
        }

        var end = ResolveEndDate(item).ToDateTime(item.EndsAt.Value);
        var start = GetStart(item, date);
        var dayEnd = date.AddDays(1).ToDateTime(TimeOnly.MinValue);
        if (end > dayEnd) end = dayEnd;
        return end > start ? end : null;
    }

    private static int SeverityOrder(string severity) => severity switch
    {
        DayReviewSeverities.Critical => 0,
        DayReviewSeverities.Warning => 1,
        _ => 2
    };
}

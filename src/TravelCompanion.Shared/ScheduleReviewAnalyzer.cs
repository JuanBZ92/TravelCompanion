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
            reviews.Add(AnalyzeDay(date, source.Where(item => item.Date == date).ToList()));
        }

        return reviews;
    }

    public static DayReviewDto AnalyzeDay(DateOnly date, IReadOnlyList<ScheduleItemDto>? items)
    {
        var timedItems = (items ?? [])
            .Where(item => item.Type != ReservationType.Lodging && item.HasExactTime)
            .OrderBy(GetStart)
            .ThenBy(item => item.SortOrder)
            .ToList();
        var issues = new List<DayReviewIssueDto>();

        AddOverlapIssues(timedItems, issues);
        AddTransferIssues(timedItems, issues);
        AddPackedDayIssue(timedItems, issues);

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

    private static void AddOverlapIssues(
        IReadOnlyList<ScheduleItemDto> items,
        ICollection<DayReviewIssueDto> issues)
    {
        for (var index = 0; index < items.Count; index++)
        {
            var current = items[index];
            var currentEnd = GetExplicitEnd(current);
            if (!currentEnd.HasValue)
            {
                continue;
            }

            for (var candidateIndex = index + 1; candidateIndex < items.Count; candidateIndex++)
            {
                var candidate = items[candidateIndex];
                var candidateStart = GetStart(candidate);
                if (candidateStart >= currentEnd.Value)
                {
                    break;
                }

                var overlapMinutes = Math.Max(1, (int)Math.Ceiling((currentEnd.Value - candidateStart).TotalMinutes));
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

    private static void AddTransferIssues(
        IReadOnlyList<ScheduleItemDto> items,
        ICollection<DayReviewIssueDto> issues)
    {
        for (var index = 0; index < items.Count - 1; index++)
        {
            var origin = items[index];
            var destination = items[index + 1];
            var originEnd = GetExplicitEnd(origin);
            if (!originEnd.HasValue)
            {
                continue;
            }

            var availableMinutes = (int)Math.Floor((GetStart(destination) - originEnd.Value).TotalMinutes);
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
                "Traslado con poco margen",
                $"Entre {origin.Title} y {destination.Title} tienes {availableMinutes} min; conviene reservar unos {recommendedMinutes} min.",
                [origin.Id, destination.Id],
                availableMinutes,
                recommendedMinutes));
        }
    }

    private static void AddPackedDayIssue(
        IReadOnlyList<ScheduleItemDto> items,
        ICollection<DayReviewIssueDto> issues)
    {
        if (items.Count == 0)
        {
            return;
        }

        var explicitMinutes = items
            .Select(item => (Start: GetStart(item), End: GetExplicitEnd(item)))
            .Where(window => window.End.HasValue && window.End.Value > window.Start)
            .Sum(window => (window.End!.Value - window.Start).TotalMinutes);
        var spanMinutes = (GetStart(items[^1]) - GetStart(items[0])).TotalMinutes;
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

    private static DateTime GetStart(ScheduleItemDto item) => item.Date.ToDateTime(item.StartsAt);

    private static DateTime? GetExplicitEnd(ScheduleItemDto item)
    {
        if (!item.EndsAt.HasValue)
        {
            return null;
        }

        var end = (item.EndsOn ?? item.Date).ToDateTime(item.EndsAt.Value);
        var start = GetStart(item);
        return end > start ? end : null;
    }

    private static int SeverityOrder(string severity) => severity switch
    {
        DayReviewSeverities.Critical => 0,
        DayReviewSeverities.Warning => 1,
        _ => 2
    };
}

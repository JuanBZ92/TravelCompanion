using TravelCompanion.Shared;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Mobile.Services;

public static class TripDayOverview
{
    // Only a confirmed itinerary can establish that today's timed activities ended.
    // Remote enrichment is deliberately not required: cached schedules work offline.
    public static TripDayContext ResolveContext(TripScheduleDto? schedule, DateOnly? selectedDate, DateTimeOffset instant)
    {
        if (schedule is null) return TripDayContext.Empty;
        var today = DateOnly.FromDateTime(UpcomingActivitySelector.GetTripNow(schedule.TimeZoneId, instant));
        if (selectedDate != today || today < schedule.StartsOn || today > schedule.EndsOn)
            return TripDayContext.Empty;
        var activity = UpcomingActivitySelector.Select(schedule.Items, today, schedule.TimeZoneId, instant);
        return activity is not null
            ? new(activity, UpcomingActivitySelector.IsInProgress(activity, schedule.TimeZoneId, instant), null)
            : new(null, false, Tomorrow(schedule, today));
    }

    public static string TodayMessage(IEnumerable<ScheduleItemDto> items, DateOnly date, bool hasSuggestedIdeas = false)
    {
        var day = items.Where(x => x.Date == date || x.Date < date && x.EndsOn >= date).ToArray();
        if (day.Length == 0 && !hasSuggestedIdeas) return "TodayEmpty";
        if (hasSuggestedIdeas || day.Any(x => x.Type == ReservationType.Event && !x.HasExactTime)) return "TodayFlexible";
        return "TodayNoUpcoming";
    }
    public static DateOnly? Tomorrow(TripScheduleDto? schedule, DateOnly today)
    {
        var tomorrow = today.AddDays(1);
        return schedule is not null && tomorrow >= schedule.StartsOn && tomorrow <= schedule.EndsOn ? tomorrow : null;
    }
    public static ScheduleItemDto? FirstBooking(TripScheduleDto schedule, DateOnly date) => schedule.Items
        .Where(x => x.Date == date && x.HasExactTime && x.Type != ReservationType.Lodging)
        .DistinctBy(x => x.Id).OrderBy(x => x.StartsAt).ThenBy(x => x.Id).FirstOrDefault();
    public static ScheduleItemDto? Hotel(TripScheduleDto schedule, DateOnly date) => schedule.Items
        .Where(x => x.Type == ReservationType.Lodging && x.Date <= date && (x.EndsOn ?? x.Date) >= date)
        .OrderByDescending(x => x.Date).ThenBy(x => x.Id).FirstOrDefault();
    public static IReadOnlyList<Guid> DocumentReservations(TripScheduleDto schedule, DateOnly date) =>
        new[] { FirstBooking(schedule, date), Hotel(schedule, date) }.OfType<ScheduleItemDto>()
            .Select(item => item.Id).Distinct().ToArray();
    public static IReadOnlyList<TripDayDocument> DistinctDocuments(IEnumerable<TripDayDocument> documents) =>
        documents.DistinctBy(document => document.Link.CuratedUrl ?? document.Local?.SourceUrl
            ?? "local:" + document.Link.LocalDocumentId).ToArray();
    public static bool CanOpenDocument(ReservationDocumentLink? link, LocalTripDocument? local, bool hasCuratedAccess) =>
        link?.LocalDocumentId is { } id
            ? local is not null && local.Id == id && (local.SourceUrl is null || hasCuratedAccess)
            : link?.CuratedUrl is not null && hasCuratedAccess;
}

public sealed record TripDayContext(ScheduleItemDto? Activity, bool IsInProgress, DateOnly? Tomorrow)
{
    public static TripDayContext Empty { get; } = new(null, false, null);
    public bool HasContent => Activity is not null || Tomorrow.HasValue;
}

public sealed record TripDayDocument(ReservationDocumentLink Link, LocalTripDocument? Local);

// A failed refresh keeps the last confirmed metadata. The page still checks
// current document permissions when rendering and when opening each file.
public sealed class TripDayMetadataLoader
{
    public IReadOnlyList<TripDayDocument>? Documents { get; private set; }
    public bool? OfflineReady { get; private set; }

    public async Task<bool> RefreshAsync(
        Func<CancellationToken, Task<IReadOnlyList<TripDayDocument>>> readDocuments,
        Func<CancellationToken, Task<bool>> readOfflineReady,
        Func<bool> isCurrent, CancellationToken ct, Action? publish = null,
        Action<string, Exception>? failed = null)
    {
        void Check() { ct.ThrowIfCancellationRequested(); if (!isCurrent()) throw new OperationCanceledException(); }
        var incomplete = false;
        Check();
        try
        {
            var documents = await readDocuments(ct).WaitAsync(ct);
            Check(); Documents = documents;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception) { Check(); incomplete = true; failed?.Invoke("documents", exception); }
        Check(); publish?.Invoke();
        try
        {
            var offlineReady = await readOfflineReady(ct).WaitAsync(ct);
            Check(); OfflineReady = offlineReady;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception) { Check(); incomplete = true; failed?.Invoke("offline", exception); }
        Check(); publish?.Invoke();
        return incomplete;
    }
}

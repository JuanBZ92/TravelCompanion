using TravelCompanion.Shared;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Mobile.Services;

public sealed record TripPreparationProgress(bool HasSchedule, int ActivityDays, int Issues,
    string OfflineStatusKey, int DownloadedResources, int DownloadableResources)
{
    public static TripPreparationProgress Create(Guid tripId, TripScheduleDto? schedule,
        OfflineTripManifest? manifest, long? catalogVersion = null, long? documentsVersion = null)
    {
        var matching = schedule?.TripId == tripId;
        if (manifest?.TripId != tripId) manifest = null;
        var days = matching ? schedule!.Items.Where(x => x.Type == ReservationType.Event
            && x.Date >= schedule.StartsOn && x.Date <= schedule.EndsOn).Select(x => x.Date).Distinct().Count() : 0;
        var issues = matching ? ScheduleReviewAnalyzer.Analyze(schedule!.Items, schedule.StartsOn, schedule.EndsOn)
            .Sum(x => x.Issues.Count) : 0;
        var status = manifest is null ? "OfflineNotPrepared" : !manifest.IsComplete || !matching ? "OfflineIncomplete"
            : manifest.HasUpdate(schedule!.Revision, catalogVersion, documentsVersion) ? "OfflineUpdate" : "OfflineReady";
        return new(matching, days, issues, status,
            manifest?.Resources.Count(x => x.State == "available") ?? 0,
            manifest?.Resources.Count(x => x.State != "external") ?? 0);
    }
}

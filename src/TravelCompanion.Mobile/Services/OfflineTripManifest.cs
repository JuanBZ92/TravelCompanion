namespace TravelCompanion.Mobile.Services;

public sealed record OfflineTripResource(string Key, string Title, string State, string? SourceUrl = null);
public sealed record OfflineTripManifest(Guid TripId, int Revision, DateTimeOffset? DownloadedAt,
    IReadOnlyList<OfflineTripResource> Resources, bool Interrupted = false,
    long? CatalogVersion = null, long? DocumentsVersion = null)
{
    public bool IsComplete => !Interrupted && DownloadedAt.HasValue && Resources.Any(item => item.Key == "itinerary" && item.State == "available")
        && Resources.All(item => item.State is "available" or "external");
    public bool HasUpdate(int revision, long? catalogVersion = null, long? documentsVersion = null) => DownloadedAt.HasValue
        && (revision != Revision || catalogVersion.HasValue && catalogVersion != CatalogVersion
            || documentsVersion.HasValue && documentsVersion != DocumentsVersion);
}

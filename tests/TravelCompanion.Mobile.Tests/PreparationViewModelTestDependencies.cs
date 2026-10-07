namespace TravelCompanion.Mobile.Services;

// Platform/server boundaries only: tests execute the production preparation ViewModel and local stores.
public sealed class OfflineTripPreparationService
{
    public Task<OfflineTripManifest?> GetAsync(CancellationToken ct = default) => Task.FromResult<OfflineTripManifest?>(null);
    public Task PrepareAsync(CancellationToken ct = default) => Task.CompletedTask;
}

public sealed class ProductAnalyticsTracker
{
    public Func<Task> Track { get; set; } = () => Task.CompletedTask;
    public Task TrackAsync(string name, string source, Guid? tripId = null, CancellationToken cancellationToken = default) => Track();
    public Task TrackAsync(string name, string source, string paywallVariant, Guid? tripId, CancellationToken cancellationToken = default) => Track();
}

public static class ClientDiagnostics
{
    public static void Record(string name, object? details = null, Exception? exception = null) { }
}

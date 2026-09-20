using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Mobile.Services;

public sealed class ProductAnalyticsTracker(
    ProductAnalyticsQueueService queue,
    AnalyticsConsentService consent,
    AuthSessionService sessions)
{
    public async Task TrackAsync(
        string name,
        string? source = null,
        string? paywallVariant = null,
        Guid? tripId = null,
        CancellationToken cancellationToken = default)
    {
        if (!consent.IsGranted || !sessions.HasSession) return;
        try
        {
            await queue.EnqueueAndFlushAsync(new ProductAnalyticsEventDto(
                Guid.NewGuid(), name, DateTimeOffset.UtcNow, source,
                AppInfo.Current.VersionString, DeviceInfo.Platform.ToString(), paywallVariant,
                tripId ?? sessions.CurrentTripId, true), cancellationToken);
        }
        catch when (!cancellationToken.IsCancellationRequested)
        {
            // Optional product analytics never interrupts the user flow.
        }
    }
}

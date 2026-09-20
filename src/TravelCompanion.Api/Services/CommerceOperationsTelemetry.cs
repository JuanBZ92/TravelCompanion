using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.DataContracts;

namespace TravelCompanion.Api.Services;

public sealed class CommerceOperationsTelemetry(TelemetryClient telemetry)
{
    public void RecordBacklog(int paymentsWithoutPass, int exhaustedNotifications,
        int pendingFinalizations, int repeatedSyncFailures, int duplicatePurchases)
    {
        telemetry.TrackMetric("commerce.payment_without_pass.current", paymentsWithoutPass);
        telemetry.TrackMetric("commerce.store_notification_exhausted.current", exhaustedNotifications);
        telemetry.TrackMetric("commerce.store_finalization_pending.current", pendingFinalizations);
        telemetry.TrackMetric("commerce.trip_sync_repeated_failure.current", repeatedSyncFailures);
        telemetry.TrackMetric("commerce.duplicate_purchase_review.current", duplicatePurchases);
    }

    public void PaymentWithoutPass(Guid transactionId, Guid tripId) =>
        Track("commerce.payment_without_pass", transactionId, tripId);

    public void DuplicatePurchase(Guid transactionId, Guid tripId) =>
        Track("commerce.duplicate_purchase_review", transactionId, tripId);

    public void StoreNotificationExhausted(Guid receiptId, int attempts, string? error)
    {
        var properties = new Dictionary<string, string>
        {
            ["receiptId"] = receiptId.ToString("N"),
            ["attempts"] = attempts.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["errorCode"] = NormalizeError(error)
        };
        telemetry.TrackEvent("commerce.store_notification_exhausted", properties);
        telemetry.TrackMetric("commerce.store_notification_exhausted.count", 1);
    }

    public void StoreFinalizationPending(Guid transactionId, Guid tripId) =>
        Track("commerce.store_finalization_pending", transactionId, tripId);

    public void TripSynchronizationRepeatedFailure(Guid workId, Guid tripId, int attempts, string? error)
    {
        var properties = new Dictionary<string, string>
        {
            ["workId"] = workId.ToString("N"),
            ["tripId"] = tripId.ToString("N"),
            ["attempts"] = attempts.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["errorCode"] = NormalizeError(error)
        };
        telemetry.TrackEvent("commerce.trip_sync_repeated_failure", properties);
        telemetry.TrackMetric("commerce.trip_sync_repeated_failure.count", 1);
    }

    private void Track(string name, Guid transactionId, Guid tripId)
    {
        telemetry.TrackEvent(name, new Dictionary<string, string>
        {
            ["transactionId"] = transactionId.ToString("N"),
            ["tripId"] = tripId.ToString("N")
        });
        telemetry.TrackMetric($"{name}.count", 1);
    }

    private static string NormalizeError(string? error)
    {
        if (string.IsNullOrWhiteSpace(error)) return "unknown";
        var normalized = new string(error.Take(80).Select(character =>
            char.IsLetterOrDigit(character) || character is '_' or '-' ? character : '_').ToArray());
        return normalized.ToLowerInvariant();
    }
}

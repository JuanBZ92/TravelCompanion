using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Mobile.Services;

internal enum BuilderTripDeletionResult { Deleted, Cancelled, Unavailable, TripChanged }

internal static class BuilderTripDeletionFlow
{
    public static async Task<BuilderTripDeletionResult> ExecuteAsync(
        Guid tripId,
        Func<CancellationToken, Task<BuilderTripSetupDto?>> loadFresh,
        Func<BuilderTripSetupDto, Task<bool>> confirm,
        Func<DeleteBuilderTripSetupRequest, CancellationToken, Task> delete,
        CancellationToken cancellationToken)
    {
        // A cached revision (including an offline fallback) cannot authorize deletion.
        var setup = await loadFresh(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (setup is null) return BuilderTripDeletionResult.Unavailable;
        if (setup.TripId != tripId) return BuilderTripDeletionResult.TripChanged;
        if (!await confirm(setup)) return BuilderTripDeletionResult.Cancelled;
        cancellationToken.ThrowIfCancellationRequested();

        // Keep the revision the user confirmed. A conflict must require another confirmation.
        await delete(new DeleteBuilderTripSetupRequest(tripId, setup.Revision), cancellationToken);
        return BuilderTripDeletionResult.Deleted;
    }
}

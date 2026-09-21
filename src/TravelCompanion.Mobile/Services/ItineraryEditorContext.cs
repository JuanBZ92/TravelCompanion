using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Mobile.Services;

internal sealed class ItineraryEditorContext
{
    private long? _sessionVersion;
    public int Revision { get; set; }
    public bool IsCurrent(long sessionVersion) => _sessionVersion == sessionVersion;

    public async Task<BuilderTripSetupDto?> LoadAsync(long sessionVersion, Guid? tripId,
        Func<Task<BuilderTripSetupDto?>> loadFromServer, Func<long> currentSessionVersion)
    {
        _sessionVersion = null;
        var setup = await loadFromServer();
        if (setup is null || !setup.IsConfigured || !tripId.HasValue || setup.TripId != tripId || currentSessionVersion() != sessionVersion)
            return null;
        Revision = setup.Revision;
        _sessionVersion = sessionVersion;
        return setup;
    }
}

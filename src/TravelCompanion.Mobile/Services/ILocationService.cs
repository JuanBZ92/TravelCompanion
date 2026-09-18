using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Mobile.Services;

public interface ILocationService
{
    Task<GeoPointDto?> GetLastKnownLocationAsync(CancellationToken cancellationToken = default);
    Task<GeoPointDto?> GetCurrentLocationAsync(CancellationToken cancellationToken = default);
}

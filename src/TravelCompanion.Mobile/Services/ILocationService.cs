using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Mobile.Services;

public interface ILocationService
{
    Task<GeoPointDto?> GetCurrentLocationAsync(CancellationToken cancellationToken = default);
}

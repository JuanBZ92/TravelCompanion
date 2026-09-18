using Microsoft.Extensions.Logging;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Mobile.Services;

public sealed class LocationService(ILogger<LocationService> logger) : ILocationService
{
    private static readonly TimeSpan LocationTimeout = TimeSpan.FromSeconds(8);
    private static readonly TimeSpan LastKnownMaximumAge = TimeSpan.FromMinutes(5);

    public async Task<GeoPointDto?> GetLastKnownLocationAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var lastKnownLocation = await Geolocation.Default.GetLastKnownLocationAsync();
            cancellationToken.ThrowIfCancellationRequested();
            return lastKnownLocation is not null
                && lastKnownLocation.Timestamp >= DateTimeOffset.UtcNow.Subtract(LastKnownMaximumAge)
                ? ToGeoPoint(lastKnownLocation)
                : null;
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (Exception ex) when (ex is FeatureNotSupportedException or FeatureNotEnabledException or PermissionException)
        {
            logger.LogInformation(ex, "Last known location is unavailable on this device.");
            return null;
        }
    }

    public async Task<GeoPointDto?> GetCurrentLocationAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var permissionStatus = await Permissions.CheckStatusAsync<Permissions.LocationWhenInUse>();
            if (permissionStatus != PermissionStatus.Granted)
            {
                if (Permissions.ShouldShowRationale<Permissions.LocationWhenInUse>())
                {
                    await Shell.Current.DisplayAlertAsync(
                        "Ubicacion",
                        "La ubicacion ayuda a ordenar planes cercanos a donde estas ahora.",
                        "Entendido");
                }

                permissionStatus = await Permissions.RequestAsync<Permissions.LocationWhenInUse>();
            }

            if (permissionStatus is not (PermissionStatus.Granted or PermissionStatus.Limited))
            {
                return null;
            }

            var lastKnownLocation = await GetLastKnownLocationAsync(cancellationToken);
            if (lastKnownLocation is not null)
            {
                return lastKnownLocation;
            }

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(LocationTimeout);
            var currentLocation = await Geolocation.Default.GetLocationAsync(
                new GeolocationRequest(GeolocationAccuracy.Medium, LocationTimeout),
                timeoutCts.Token);

            return currentLocation is null ? null : ToGeoPoint(currentLocation);
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (FeatureNotSupportedException ex)
        {
            logger.LogInformation(ex, "Location is not supported on this device.");
            return null;
        }
        catch (FeatureNotEnabledException ex)
        {
            logger.LogInformation(ex, "Location is disabled on this device.");
            return null;
        }
        catch (PermissionException ex)
        {
            logger.LogInformation(ex, "Location permission was not granted.");
            return null;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Unable to resolve current location.");
            return null;
        }
    }

    private static GeoPointDto ToGeoPoint(Location location)
    {
        return new GeoPointDto(
            (decimal)location.Latitude,
            (decimal)location.Longitude);
    }
}

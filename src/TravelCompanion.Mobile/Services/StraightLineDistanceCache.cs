using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace TravelCompanion.Mobile.Services;

public static class StraightLineDistanceCache
{
    private const string AlgorithmVersion = "haversine-v1";

    public static double GetOrCalculate(decimal fromLatitude, decimal fromLongitude, decimal toLatitude, decimal toLongitude)
    {
        var raw = string.Create(CultureInfo.InvariantCulture,
            $"{AlgorithmVersion}:{fromLatitude:0.######}:{fromLongitude:0.######}:{toLatitude:0.######}:{toLongitude:0.######}");
        var key = $"distance_{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw)))}";
        var cached = Preferences.Default.Get(key, -1d);
        if (cached >= 0d) return cached;

        const double earthRadiusKm = 6371d;
        static double Radians(decimal degrees) => (double)degrees * Math.PI / 180d;
        var latitudeDelta = Radians(toLatitude - fromLatitude);
        var longitudeDelta = Radians(toLongitude - fromLongitude);
        var latitude1 = Radians(fromLatitude);
        var latitude2 = Radians(toLatitude);
        var a = Math.Pow(Math.Sin(latitudeDelta / 2d), 2d)
            + Math.Cos(latitude1) * Math.Cos(latitude2) * Math.Pow(Math.Sin(longitudeDelta / 2d), 2d);
        var distance = earthRadiusKm * 2d * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1d - a));
        Preferences.Default.Set(key, distance);
        return distance;
    }
}

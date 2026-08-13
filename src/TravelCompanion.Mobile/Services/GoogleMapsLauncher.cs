using System.Globalization;

namespace TravelCompanion.Mobile.Services;

public static class GoogleMapsLauncher
{
    public static Task<bool> OpenAsync(decimal latitude, decimal longitude)
    {
        var coordinates = string.Create(
            CultureInfo.InvariantCulture,
            $"{latitude},{longitude}");

        return OpenAsync(coordinates);
    }

    public static Task<bool> OpenAsync(string query)
    {
        var url = $"https://www.google.com/maps/search/?api=1&query={Uri.EscapeDataString(query)}";

        return Launcher.Default.TryOpenAsync(new Uri(url));
    }
}

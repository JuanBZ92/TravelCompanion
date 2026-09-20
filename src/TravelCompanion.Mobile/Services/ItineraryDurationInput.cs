using System.Globalization;

namespace TravelCompanion.Mobile.Services;

public static class ItineraryDurationInput
{
    public static bool TryParse(string? input, out int? minutes)
    {
        minutes = null;
        if (string.IsNullOrWhiteSpace(input)) return true;
        if (!int.TryParse(input, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            || value is < 15 or > 1440) return false;
        minutes = value;
        return true;
    }
}

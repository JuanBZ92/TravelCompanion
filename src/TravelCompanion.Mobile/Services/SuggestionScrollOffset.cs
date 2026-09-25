namespace TravelCompanion.Mobile.Services;

internal static class SuggestionScrollOffset
{
    public static double Calculate(double current, double viewport, double top, double height)
    {
        // Reserve up to two short suggestion rows, keeping the input visible on small screens.
        var preview = Math.Min(160, Math.Max(0, viewport - height - 24));
        return Math.Max(current, top + height + preview + 12 - viewport);
    }
}

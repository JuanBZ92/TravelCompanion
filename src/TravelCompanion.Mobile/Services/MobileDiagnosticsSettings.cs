using System.Reflection;

namespace TravelCompanion.Mobile.Services;

internal static class MobileDiagnosticsSettings
{
    private const string MetadataKey = "TravelCompanionDiagnosticsEnabled";

    public static bool IsEnabled => typeof(MobileDiagnosticsSettings).Assembly
        .GetCustomAttributes<AssemblyMetadataAttribute>()
        .FirstOrDefault(attribute => attribute.Key == MetadataKey)
        ?.Value is { } value
        && bool.TryParse(value, out var enabled)
        && enabled;
}

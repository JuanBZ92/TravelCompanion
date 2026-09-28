using IMap = Microsoft.Maui.Maps.IMap;
using Microsoft.Maui.Maps.Handlers;
using TravelCompanion.Mobile.Controls;

namespace TravelCompanion.Mobile.Platforms.Android;

internal static class MapUpdateBatching
{
    private static bool configured;
    public static void Configure()
    {
        if (configured) return;
        configured = true;
        // MAUI 10.0.60 rebuilds every native marker on each Pins collection change.
        // Preserve the original mappings, but run them once after a logical update.
        foreach (var property in new[] { nameof(IMap.Pins), nameof(IMap.Elements) })
            MapHandler.Mapper.ModifyMapping(property, (handler, map, original) =>
            {
                if (map is BatchedMap batched && batched.DeferUpdate(property)) return;
                original?.Invoke(handler, map);
            });
    }
}

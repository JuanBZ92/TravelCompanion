#if !WINDOWS
using Microsoft.Maui.Maps;
using TravelCompanion.Mobile.Services;

namespace TravelCompanion.Mobile.Controls;

public sealed class BatchedMap : Microsoft.Maui.Controls.Maps.Map
{
    private readonly MapUpdateBatch updates;
    public BatchedMap() => updates = new(property => Handler?.UpdateValue(property));
    public BatchedMap(MapSpan region) : base(region) => updates = new(property => Handler?.UpdateValue(property));
    public IDisposable BeginUpdate() => updates.Begin();
    internal bool DeferUpdate(string property) => updates.Defer(property);
}
#endif

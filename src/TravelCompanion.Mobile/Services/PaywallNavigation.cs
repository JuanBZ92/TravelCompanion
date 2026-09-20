using TravelCompanion.Shared.Dtos;
using TravelCompanion.Mobile.Pages;

namespace TravelCompanion.Mobile.Services;

public static class PaywallNavigation
{
    public static Task OpenAsync(PaywallEntryPoint entryPoint) => Shell.Current.GoToAsync(nameof(PaywallPage),
        new Dictionary<string, object> { ["EntryPoint"] = entryPoint.ToString() });
}

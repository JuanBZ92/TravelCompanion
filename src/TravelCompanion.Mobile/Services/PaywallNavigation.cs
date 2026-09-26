using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Mobile.Services;

public static class PaywallNavigation
{
    public static async Task OpenAsync(PaywallEntryPoint entryPoint, bool limitReached = false)
    {
        if (limitReached)
            await MauiProgram.Services.GetRequiredService<ProductAnalyticsTracker>().TrackAsync("limit_reached", entryPoint.ToString());
        await Shell.Current.GoToAsync("//main/pass", new ShellNavigationQueryParameters { ["EntryPoint"] = entryPoint.ToString() });
    }
}

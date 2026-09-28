using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Mobile.Services;

public static class PaywallNavigation
{
    public static async Task OpenAsync(PaywallEntryPoint entryPoint, bool limitReached = false)
    {
        if (limitReached)
            await MauiProgram.Services.GetRequiredService<ProductAnalyticsTracker>().TrackAsync("limit_reached", entryPoint.ToString());
        var route = MauiProgram.Services.GetRequiredService<AuthSessionService>().IsFreeMapPreview
            ? "//main/pass" : nameof(TravelCompanion.Mobile.Pages.PaywallPage);
        await Shell.Current.GoToAsync(route, new ShellNavigationQueryParameters { ["EntryPoint"] = entryPoint.ToString() });
    }
}

using TravelCompanion.Mobile.Pages;

namespace TravelCompanion.Mobile.Services;

public static class BuilderSetupNavigation
{
    public static Task OpenAsync()
    {
        var navigation = Shell.Current.Navigation;
        if (navigation.ModalStack.OfType<BuilderSetupPage>().Any()) return Task.CompletedTask;
        return navigation.PushModalAsync(MauiProgram.Services.GetRequiredService<BuilderSetupPage>());
    }

    public static async Task CloseAsync()
    {
        if (Shell.Current.Navigation.ModalStack.LastOrDefault() is BuilderSetupPage)
            await Shell.Current.Navigation.PopModalAsync();
    }
}

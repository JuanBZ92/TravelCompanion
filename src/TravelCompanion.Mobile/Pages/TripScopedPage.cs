using TravelCompanion.Mobile.Services;

namespace TravelCompanion.Mobile.Pages;

// A modal can outlive Shell navigation; hide personal content immediately on a scope change.
public class TripScopedPage : ContentPage
{
    private readonly AuthSessionService sessions = MauiProgram.Services.GetRequiredService<AuthSessionService>();
    private readonly Guid? user;
    private readonly Guid? trip;
    private readonly long version;
    public TripScopedPage() { user = sessions.CurrentUserId; trip = sessions.CurrentTripId; version = sessions.ContextVersion; }
    protected override void OnAppearing()
    {
        base.OnAppearing(); sessions.StateChanged += SessionChanged; SessionChanged(this, EventArgs.Empty);
    }
    protected override void OnDisappearing() { sessions.StateChanged -= SessionChanged; base.OnDisappearing(); }
    private void SessionChanged(object? sender, EventArgs args)
    {
        if (sessions.HasSession && sessions.CurrentUserId == user && sessions.CurrentTripId == trip && sessions.ContextVersion == version) return;
        Dispatcher.Dispatch(async () =>
        {
            Content = null;
            if (Navigation.ModalStack.LastOrDefault() == this) await Navigation.PopModalAsync(false);
        });
    }
}

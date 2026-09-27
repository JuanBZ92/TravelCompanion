using TravelCompanion.Mobile.Services;

namespace TravelCompanion.Mobile.Pages;

// A modal can survive a Shell navigation. Never leave a previous account's memory visible.
public abstract class JournalScopedPage : ContentPage
{
    private readonly AuthSessionService sessions = MauiProgram.Services.GetRequiredService<AuthSessionService>();
    private readonly JournalScope pageScope = MauiProgram.Services.GetRequiredService<JournalStore>().Scope();
    protected override void OnAppearing()
    {
        base.OnAppearing();
        sessions.StateChanged += SessionChanged;
        SessionChanged(this, EventArgs.Empty);
    }
    protected override void OnDisappearing()
    {
        sessions.StateChanged -= SessionChanged;
        base.OnDisappearing();
    }
    private void SessionChanged(object? sender, EventArgs e)
    {
        if (sessions.HasSession && sessions.ContextVersion == pageScope.Version
            && sessions.CurrentUserId == pageScope.UserId && sessions.CurrentTripId == pageScope.TripId) return;
        Dispatcher.Dispatch(async () =>
        {
            Content = null;
            if (Navigation.ModalStack.LastOrDefault() == this) await Navigation.PopModalAsync(false);
        });
    }
}

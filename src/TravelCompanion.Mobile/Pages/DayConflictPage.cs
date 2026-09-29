using TravelCompanion.Mobile.Services;
using TravelCompanion.Shared;
using TravelCompanion.Shared.Dtos;
using static TravelCompanion.Mobile.Pages.ExpenseUi;

namespace TravelCompanion.Mobile.Pages;

public sealed class DayConflictPage : ContentPage, IQueryAttributable
{
    private readonly AuthSessionService sessions = MauiProgram.Services.GetRequiredService<AuthSessionService>();
    private readonly MobileBootstrapStore bootstrap = MauiProgram.Services.GetRequiredService<MobileBootstrapStore>();
    private readonly TravelCompanionApiClient api = MauiProgram.Services.GetRequiredService<TravelCompanionApiClient>();
    private readonly VerticalStackLayout body = new() { Spacing = 14, Padding = 24 };
    private readonly long contextVersion;
    private readonly Guid? tripId;
    private CancellationTokenSource? loading;
    private DateOnly date;
    private string? issueKey;
    private int index;
    private TripScheduleDto? schedule;
    private IReadOnlyList<DayReviewIssueDto> issues = [];
    private bool cached = true;
    private bool navigating;
    private bool Current => sessions.HasSession && sessions.ContextVersion == contextVersion && sessions.CurrentTripId == tripId;

    public DayConflictPage()
    {
        contextVersion = sessions.ContextVersion; tripId = sessions.CurrentTripId;
        Title = T("Revisar este día", "Review this day"); BackgroundColor = Paper; SafeAreaEdges = SafeAreaEdges.All;
        Shell.SetBackButtonBehavior(this, new BackButtonBehavior { Command = new Command(async () => await BackAsync()) });
        Content = new ScrollView { Content = body };
    }
    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("ReviewDate", out var value) && value is DateOnly selected) date = selected;
        if (query.TryGetValue("IssueKey", out var key) && key is string text) issueKey = text;
    }
    protected override async void OnAppearing()
    {
        base.OnAppearing(); sessions.StateChanged += SessionChanged;
        await RefreshAsync();
    }
    protected override void OnDisappearing()
    {
        sessions.StateChanged -= SessionChanged; loading?.Cancel(); base.OnDisappearing();
    }
    private void SessionChanged(object? sender, EventArgs args)
    {
        if (Current) return;
        loading?.Cancel(); Dispatcher.Dispatch(() => { body.Clear(); schedule = null; issues = []; });
    }
    private async Task RefreshAsync()
    {
        loading?.Cancel(); loading?.Dispose(); loading = new(); var ct = loading.Token;
        cached = true;
        if (!Current || date == default) { body.Clear(); return; }
        body.Clear(); body.Add(Text(T("Comprobando el día…", "Checking this day…")));
        try
        {
            var saved = await bootstrap.GetCachedAsync(cancellationToken: ct);
            if (ct.IsCancellationRequested || !Current) return;
            if (saved?.Value.Schedule is { } local && local.TripId == tripId) Apply(local, true);
            if (Connectivity.Current.NetworkAccess != NetworkAccess.Internet) return;
            var token = await sessions.GetTokenAsync(); if (token is null || !Current) return;
            var fresh = await api.GetScheduleAsync(token, ct);
            if (ct.IsCancellationRequested || !Current) return;
            if (fresh is not null && fresh.TripId == tripId) Apply(fresh, false);
        }
        catch (OperationCanceledException) { }
        catch (Exception error)
        {
            ClientDiagnostics.Record("day_conflicts_load_failed", exception: error);
            if (Current && !ct.IsCancellationRequested)
            { if (schedule is not null) Render(); else body.Clear(); body.Add(Text(T("No pudimos actualizar. Volvé a intentar.", "Could not refresh. Try again."))); }
        }
        finally
        {
            if (Current && !ct.IsCancellationRequested && schedule is null)
            { body.Clear(); body.Add(Text(T("No hay un itinerario guardado para revisar. Conectate y volvé a intentar.", "No saved itinerary to review. Connect and try again."))); body.Add(Button(T("Reintentar", "Retry"), RefreshAsync)); }
        }
    }
    private void Apply(TripScheduleDto value, bool fromCache)
    {
        schedule = value; cached = fromCache;
        issues = ScheduleReviewAnalyzer.Analyze(value.Items, date, date).Single().Issues;
        index = DayConflictCursor.Select(issues, issueKey, index);
        issueKey = issues.Count > 0 ? DayConflictCursor.Key(issues[index]) : null;
        Render();
    }
    private void Render()
    {
        if (!Current || schedule is null) return;
        body.Clear(); body.Add(Text(date.ToString("dddd · d MMMM"), 13));
        if (cached) body.Add(Text(T("Itinerario guardado · pendiente de comprobar en línea", "Saved itinerary · online verification pending"), 12));
        if (issues.Count == 0)
        {
            body.Add(Text(cached ? T("Sin conflictos en los datos guardados", "No conflicts in saved data") : T("No quedan conflictos detectados", "No detected conflicts remain"), 26, true));
            body.Add(Button(T("Volver al itinerario", "Back to itinerary"), ReturnAsync));
            if (cached) body.Add(Button(T("Volver a comprobar", "Check again"), RefreshAsync));
            return;
        }
        var issue = issues[index];
        body.Add(Text(T($"{index + 1} de {issues.Count} pendientes", $"{index + 1} of {issues.Count} pending"), 12));
        body.Add(Text(issue.Title, 26, true)); body.Add(Text(issue.Message, 15));
        body.Add(Text(T("Elegí qué plan revisar. Cambiar la hora no cambia su franja.", "Choose a plan to review. Changing its time keeps its period."), 13));
        foreach (var item in schedule.Items.Where(x => issue.ItemIds.Contains(x.Id)))
        {
            var editable = item.IsTravelerOwned && sessions.CanEditItinerary
                && (!sessions.IsTrial || FreePlanningPolicy.CanPlanDate(schedule.StartsOn, item.Date));
            var content = new VerticalStackLayout { Spacing = 6 };
            content.Add(Text(item.Title, 19, true));
            var time = item.HasExactTime ? $"{item.StartsAt:HH:mm}" + (item.EndsAt.HasValue ? $" – {(item.EndsOn ?? item.Date):d/M} {item.EndsAt:HH:mm}" : "") : T("Sin hora fija", "No fixed time");
            content.Add(Text($"{item.Date:d/M} · {time}", 13));
            if (item.PeriodKey is { } period) content.Add(Text(period switch { "morning" => T("Mañana", "Morning"), "midday" => T("Mediodía", "Midday"), "afternoon" => T("Tarde", "Afternoon"), "night" => T("Noche", "Night"), _ => period }, 12));
            content.Add(Text(item.TimeZoneId ?? schedule.TimeZoneId, 12));
            if (!editable) content.Add(Text(T("Este plan es de consulta; no podés modificarlo desde esta cuenta.", "This account can view this plan but cannot edit it."), 12));
            var row = new Grid { ColumnDefinitions = [new(GridLength.Star), new(GridLength.Auto)], ColumnSpacing = 8 };
            row.Add(content);
            row.Add(IconButton(editable && !cached ? "action_edit.svg" : "action_info.svg", editable && !cached ? T("Editar plan", "Edit plan") : T("Ver detalle", "View details"), async () =>
            {
                if (navigating || !Current) return;
                navigating = true;
                try { await Shell.Current.GoToAsync(editable && !cached ? nameof(ItineraryItemEditorPage) : nameof(ScheduleItemDetailPage), new ShellNavigationQueryParameters { ["ScheduleItem"] = item, ["ReturnToDayReview"] = true }); }
                finally { navigating = false; }
            }), 1);
            body.Add(Card(row));
        }
        var controls = new Grid { ColumnDefinitions = [new(GridLength.Star), new(GridLength.Auto), new(GridLength.Auto)], ColumnSpacing = 8 };
        controls.Add(IconButton("tab_trip.svg", T("Volver al itinerario", "Back to itinerary"), ReturnAsync));
        var previous = Button("‹", () => Move(-1)); previous.MinimumWidthRequest = 48; previous.IsEnabled = index > 0; SemanticProperties.SetDescription(previous, T("Conflicto anterior", "Previous conflict")); controls.Add(previous, 1);
        var next = Button("›", () => Move(1)); next.MinimumWidthRequest = 48; next.IsEnabled = index + 1 < issues.Count; SemanticProperties.SetDescription(next, T("Conflicto siguiente", "Next conflict")); controls.Add(next, 2);
        body.Add(controls);
        if (!cached)
        {
            body.Add(Text(T("Si estos horarios te sirven, confirmá el día. El aviso volverá si cambian los planes.", "If these times work for you, confirm the day. Changes to plans will show the warning again."), 12));
            body.Add(Button(T("Confirmar día", "Confirm day"), async () =>
            {
                if (!Current || schedule is null || sessions.CurrentUserId is not { } user || tripId is not { } trip) return;
                Preferences.Default.Set(DayReviewConfirmation.Key(user, trip, date), DayReviewConfirmation.Fingerprint(schedule.Items, date));
                await ReturnAsync();
            }, true));
        }
        if (cached) body.Add(Button(T("Volver a comprobar", "Check again"), RefreshAsync));
    }
    private Task Move(int step) { index = Math.Clamp(index + step, 0, issues.Count - 1); issueKey = DayConflictCursor.Key(issues[index]); Render(); return Task.CompletedTask; }
    protected override bool OnBackButtonPressed() { _ = BackAsync(); return true; }
    private Task BackAsync() => Navigation.NavigationStack.Count > 1
        && Navigation.NavigationStack[^2] is ImproveDayPage
            ? Shell.Current.GoToAsync("..") : ReturnAsync();
    private async Task ReturnAsync()
    {
        if (!Current || navigating) return;
        navigating = true;
        try { await Shell.Current.GoToAsync("//main/schedule", new ShellNavigationQueryParameters { ["InitialDate"] = date }); }
        catch (Exception error) { ClientDiagnostics.Record("day_conflicts_return_failed", exception: error); }
        finally { navigating = false; }
    }
}

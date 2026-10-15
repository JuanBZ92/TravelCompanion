using Microsoft.Maui.Layouts;
using TravelCompanion.Mobile.Services;
using TravelCompanion.Mobile.ViewModels;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Mobile.Pages;

public sealed class RoutePlannerPage : TripScopedPage
{
    private readonly AuthSessionService sessions;
    private readonly Func<bool> current;
    private readonly RoutePlannerViewModel model;
    private bool visible;

    public RoutePlannerPage(ScheduleItemDto destination, TodayHotelBaseDto? hotel, IReadOnlyList<ScheduleItemDto> items,
        string zoneId, AuthSessionService sessions, TravelCompanionApiClient api, ILocationService location,
        Func<bool> parentCurrent, string initialMode = "WALK", bool fromCurrent = false)
    {
        this.sessions = sessions;
        var user = sessions.CurrentUserId; var trip = sessions.CurrentTripId; var version = sessions.ContextVersion;
        current = () => visible && parentCurrent() && sessions.HasSession && sessions.CurrentUserId == user
            && sessions.CurrentTripId == trip && sessions.ContextVersion == version;
        model = new(destination, hotel, items, zoneId, current,
            () => sessions.CanCalculateRoutes && sessions.HasKnownValidAccess,
            location.GetCurrentLocationAsync, async (request, ct) => {
                var token = await sessions.GetTokenAsync();
                if (!current() || !sessions.CanCalculateRoutes || !sessions.HasKnownValidAccess || string.IsNullOrWhiteSpace(token)) return null;
                return await api.GetItineraryRouteAsync(token, destination.Id, request, ct);
            }, initialMode, fromCurrent);
        BindingContext = model;
        BackgroundColor = EditorialUi.Paper; SafeAreaEdges = SafeAreaEdges.All;
        var header = new Grid { ColumnDefinitions = [new(GridLength.Star), new(new GridLength(48))], Padding = new Thickness(24, 12) };
        header.Add(EditorialUi.Heading(Text("Title")));
        header.Add(EditorialUi.Icon("action_close.svg", EditorialUi.TextResource("UxClose"), () => Navigation.PopModalAsync()), 1);
        var body = new VerticalStackLayout { Padding = new Thickness(24, 8, 24, 24), Spacing = 16 };
        var destinationContent = new VerticalStackLayout { Spacing = 8 };
        destinationContent.Add(EditorialUi.Text(Text("Destination"), 13));
        destinationContent.Add(EditorialUi.Heading(destination.Title, 24, SemanticHeadingLevel.Level2));
        destinationContent.Add(EditorialUi.Text(model.Timing, 13));
        if (!string.IsNullOrWhiteSpace(destination.LocationName) && !string.Equals(destination.LocationName, destination.Title, StringComparison.OrdinalIgnoreCase))
            destinationContent.Add(EditorialUi.Text(destination.LocationName, 14));
        destinationContent.Add(EditorialUi.Button(Text("Reservation"), async () => {
            if (!current()) return;
            var detail = MauiProgram.Services.GetRequiredService<ScheduleItemDetailViewModel>();
            detail.ApplyQueryAttributes(new Dictionary<string, object> { ["ScheduleItem"] = destination });
            await Navigation.PushModalAsync(new ScheduleItemDetailPage(detail));
        }));
        body.Add(EditorialUi.Card(destinationContent));
        body.Add(EditorialUi.Text(Text("Origin"), 13));
        var origin = new Picker { ItemsSource = model.Origins.ToList(), MinimumHeightRequest = 48, TextColor = EditorialUi.Ink, Title = Text("Origin") };
        origin.SetBinding(Picker.SelectedItemProperty, nameof(model.SelectedOrigin));
        SemanticProperties.SetDescription(origin, Text("Origin"));
        body.Add(origin);
        body.Add(EditorialUi.Text(Text("Mode"), 13));
        var modes = new FlexLayout { Direction = FlexDirection.Row, Wrap = FlexWrap.Wrap, JustifyContent = FlexJustify.Start };
        foreach (var (value, key, icon) in new[] { ("WALK", "UXAuditRouteWalk", "route_walk.svg"), ("TRANSIT", "UXAuditRouteTransit", "route_bus.svg"), ("DRIVE", "UXAuditRouteDrive", "route_car.svg") })
        {
            var button = EditorialUi.Button(EditorialUi.TextResource(key), () => { model.Mode = value; return Task.CompletedTask; });
            button.ImageSource = icon; button.Margin = new Thickness(0, 0, 8, 8); button.BorderWidth = 1; button.BorderColor = EditorialUi.Line;
            void SetSelected()
            {
                var selected = model.Mode == value;
                button.BackgroundColor = selected ? EditorialUi.Paper : EditorialUi.Surface;
                button.BorderColor = selected ? EditorialUi.Gold : EditorialUi.Line;
                button.TextColor = EditorialUi.Ink;
                button.Text = (selected ? "✓ " : "") + EditorialUi.TextResource(key);
            }
            SetSelected(); model.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(model.Mode)) SetSelected(); };
            modes.Add(button);
        }
        body.Add(modes);
        body.Add(EditorialUi.Text(Text("Margin"), 13));
        var margin = new Picker { ItemsSource = model.Margins.Select(value => $"{value} min").ToList(), SelectedIndex = model.Margins.ToList().IndexOf(model.Margin), MinimumHeightRequest = 48, TextColor = EditorialUi.Ink, Title = Text("Margin") };
        SemanticProperties.SetDescription(margin, Text("Margin"));
        margin.SelectedIndexChanged += (_, _) => { if (margin.SelectedIndex >= 0) model.Margin = model.Margins[margin.SelectedIndex]; };
        body.Add(margin);
        var result = new VerticalStackLayout { Spacing = 8 };
        result.SetBinding(IsVisibleProperty, nameof(model.HasResult));
        var originSummary = EditorialUi.Text("", 14); originSummary.SetBinding(Label.TextProperty, nameof(model.OriginSummary)); result.Add(originSummary);
        var duration = EditorialUi.Heading("", 24, SemanticHeadingLevel.Level2); duration.SetBinding(Label.TextProperty, nameof(model.Duration)); result.Add(duration);
        var departure = EditorialUi.Text("", 18); departure.TextColor = EditorialUi.Ink; departure.SetBinding(Label.TextProperty, nameof(model.Departure)); result.Add(departure);
        var marginSummary = EditorialUi.Text("", 13); marginSummary.SetBinding(Label.TextProperty, nameof(model.MarginSummary)); result.Add(marginSummary);
        var resultCard = EditorialUi.Card(result);
        resultCard.SetBinding(IsVisibleProperty, nameof(model.HasResult)); body.Add(resultCard);
        var progress = new ActivityIndicator { Color = EditorialUi.Accent };
        progress.SetBinding(ActivityIndicator.IsRunningProperty, nameof(model.Busy)); progress.SetBinding(IsVisibleProperty, nameof(model.Busy)); body.Add(progress);
        var status = EditorialUi.Text("", 14); status.SetBinding(Label.TextProperty, nameof(model.Status)); body.Add(status);
        body.Add(EditorialUi.Text(Text("Disclaimer"), 13));
        var actions = new VerticalStackLayout { Padding = new Thickness(24, 12, 24, 16), Spacing = 8, BackgroundColor = EditorialUi.Surface };
        var calculate = EditorialUi.Button(Text("Calculate"), model.EstimateAsync, primary: true);
        calculate.SetBinding(IsEnabledProperty, nameof(model.CanEstimate)); actions.Add(calculate);
        actions.Add(EditorialUi.Button(Text("Maps"), async () => {
            if (!current()) return;
            if (!await Launcher.Default.TryOpenAsync(model.MapsUri) && current())
                await DisplayAlertAsync("YUKU", Text("MapsFailed"), EditorialUi.TextResource("UxOk"));
        }));
        var root = new Grid { RowDefinitions = [new(GridLength.Auto), new(GridLength.Star), new(GridLength.Auto)] };
        root.Add(header); root.Add(new ScrollView { Content = body }, 0, 1); root.Add(actions, 0, 2); Content = root;
    }

    protected override void OnAppearing() { visible = true; base.OnAppearing(); sessions.StateChanged += SessionChanged; model.RefreshAccess(); }
    protected override void OnDisappearing() { visible = false; sessions.StateChanged -= SessionChanged; model.Cancel(); base.OnDisappearing(); }
    private void SessionChanged(object? sender, EventArgs e) => Dispatcher.Dispatch(model.RefreshAccess);
    private static string Text(string suffix) => EditorialUi.TextResource("RoutePlanner" + suffix);
}

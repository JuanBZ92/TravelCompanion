using TravelCompanion.Mobile.Services;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Mobile.Pages;

public sealed class TomorrowOverviewPage(TripScheduleDto schedule, DateOnly date, TodayHotelBaseDto? hotel) : TripScopedPage
{
    private readonly JournalStore journal = MauiProgram.Services.GetRequiredService<JournalStore>();
    private readonly AuthSessionService sessions = MauiProgram.Services.GetRequiredService<AuthSessionService>();
    private readonly JournalScope scope = MauiProgram.Services.GetRequiredService<JournalStore>().Scope();
    private readonly TripDayMetadataLoader metadata = new();
    private CancellationTokenSource? cancellation;

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        cancellation?.Cancel(); cancellation?.Dispose(); cancellation = new(); var ct = cancellation.Token;
        if (!Current() || scope.TripId != schedule.TripId) return;
        BackgroundColor = EditorialUi.Paper; SafeAreaEdges = SafeAreaEdges.All;
        var content = new VerticalStackLayout { Padding = 24, Spacing = 16 };
        var header = new Grid { ColumnDefinitions = [new(GridLength.Star), new(new GridLength(48))] };
        header.Add(EditorialUi.Heading(EditorialUi.TextResource("TomorrowTitle")));
        header.Add(EditorialUi.Icon("action_close.svg", EditorialUi.TextResource("UxClose"), () => Navigation.PopModalAsync()), 1);
        content.Add(header); content.Add(EditorialUi.Text(date.ToString("dddd d MMMM", LocalizationResourceManager.Instance.CurrentCulture)));
        var first = TripDayOverview.FirstPlan(schedule, date);
        if (first is null) content.Add(EditorialUi.Text(EditorialUi.TextResource("TomorrowNoBooking")));
        else
        {
            var booking = new VerticalStackLayout { Spacing = 12 };
            var bookingHeader = new Grid { ColumnDefinitions = [new(GridLength.Star), new(GridLength.Auto)], ColumnSpacing = 12 };
            bookingHeader.Add(EditorialUi.Text(EditorialUi.TextResource("TomorrowFirstPlan"), 13));
            var time = EditorialUi.Text(TripDayOverview.PlanTimeLabel(first, date), 13);
            time.TextColor = EditorialUi.Ink;
            bookingHeader.Add(time, 1);
            booking.Add(bookingHeader);
            booking.Add(EditorialUi.Heading(first.Title, 24, SemanticHeadingLevel.Level2));
            var place = string.IsNullOrWhiteSpace(first.LocationName) ? first.City : first.LocationName;
            if (!string.IsNullOrWhiteSpace(place) && SameText(place, first.Title)) place = first.City;
            if (!string.IsNullOrWhiteSpace(place) && !SameText(place, first.Title))
                booking.Add(EditorialUi.Text(place, 14));
            var openActivity = ActionButton(EditorialUi.TextResource("JournalActivity"), async () => {
                if (!Current()) return;
                var vm = MauiProgram.Services.GetRequiredService<ViewModels.ScheduleItemDetailViewModel>();
                vm.ApplyQueryAttributes(new Dictionary<string, object> { ["ScheduleItem"] = first });
                await Navigation.PushModalAsync(new ScheduleItemDetailPage(vm));
            });
            SemanticProperties.SetHint(openActivity, EditorialUi.TextResource("UXAuditOpenActivity"));
            booking.Add(openActivity);
            content.Add(EditorialUi.Card(booking));
        }
        var stay = TripDayOverview.Hotel(schedule, date);
        var hotelName = hotel?.Name ?? stay?.LocationName ?? stay?.Title;
        if (!string.IsNullOrWhiteSpace(hotelName))
        {
            var accommodation = new VerticalStackLayout { Spacing = 12 };
            var hotelHeader = new Grid { ColumnDefinitions = [new(new GridLength(32)), new(GridLength.Star)], ColumnSpacing = 12 };
            var hotelIcon = new Image { Source = "today_hotel.svg", WidthRequest = 24, HeightRequest = 24, VerticalOptions = LayoutOptions.Start };
            AutomationProperties.SetIsInAccessibleTree(hotelIcon, false);
            hotelHeader.Add(hotelIcon);
            var hotelDetails = new VerticalStackLayout { Spacing = 4 };
            hotelDetails.Add(EditorialUi.Text(EditorialUi.TextResource("DocumentCategory_Accommodation"), 13));
            hotelDetails.Add(EditorialUi.Heading(hotelName, 20, SemanticHeadingLevel.Level2));
            var hotelPlace = hotel?.Address ?? stay?.Address;
            if (string.IsNullOrWhiteSpace(hotelPlace) || SameText(hotelPlace, hotelName)) hotelPlace = stay?.City;
            if (!string.IsNullOrWhiteSpace(hotelPlace) && !SameText(hotelPlace, hotelName))
                hotelDetails.Add(EditorialUi.Text(hotelPlace, 13));
            hotelHeader.Add(hotelDetails, 1);
            accommodation.Add(hotelHeader);
            var openHotel = ActionButton(EditorialUi.TextResource("UxViewMap"), async () => {
                if (Current()) await GoogleMapsLauncher.OpenAsync($"{hotelName}, {hotel?.Address ?? stay?.Address}",
                    hotel?.ProviderPlaceId ?? stay?.ProviderPlaceId);
            }, image: "tab_map.svg");
            SemanticProperties.SetHint(openHotel, EditorialUi.TextResource("UxOpenMaps"));
            accommodation.Add(openHotel);
            content.Add(EditorialUi.Card(accommodation));
        }
        var documentsContainer = new VerticalStackLayout { Spacing = 12, IsVisible = false };
        documentsContainer.Add(EditorialUi.Heading(EditorialUi.TextResource("TomorrowDocuments"), 24, SemanticHeadingLevel.Level2));
        var documentSection = new VerticalStackLayout { Spacing = 8 };
        documentsContainer.Add(documentSection);
        var offlineStatus = EditorialUi.Text("", 13); offlineStatus.IsVisible = false;
        var failure = EditorialUi.Text(EditorialUi.TextResource("TomorrowMetadataFailed"), 13); failure.IsVisible = false;
        var retry = EditorialUi.Button(EditorialUi.TextResource("UxRetry"), LoadAsync); retry.IsVisible = false;
        var loading = new ActivityIndicator { IsVisible = true, IsRunning = true, Color = EditorialUi.Accent };
        content.Add(documentsContainer); content.Add(offlineStatus); content.Add(loading); content.Add(failure); content.Add(retry);
        var openDay = ActionButton(EditorialUi.TextResource("TomorrowOpenDay"), async () => {
            if (!Current()) return;
            await Navigation.PopModalAsync();
            if (!Current()) return;
            await MauiProgram.Services.GetRequiredService<ViewModels.ScheduleViewModel>().SelectInitialDateAsync(date);
        }, primary: true);
        SemanticProperties.SetHint(openDay, EditorialUi.TextResource("TomorrowOpenDayHint"));
        var dayAction = new VerticalStackLayout { Spacing = 8, Margin = new Thickness(0, 8, 0, 0) };
        dayAction.Add(openDay);
        dayAction.Add(EditorialUi.Text(EditorialUi.TextResource("TomorrowOpenDayHint"), 13));
        content.Add(dayAction);
        Content = new ScrollView { Content = content };
        var store = MauiProgram.Services.GetRequiredService<TripDocumentStore>();
        void RenderMetadata()
        {
            documentSection.Clear();
            documentsContainer.IsVisible = false;
            if (metadata.Documents is { } documents)
            {
                var visible = documents.Where(document => TripDayOverview.CanOpenDocument(document.Link,
                    document.Local, sessions.HasCuratedDocs && sessions.HasKnownValidAccess)).ToArray();
                foreach (var document in visible)
                {
                    var documentTitle = string.IsNullOrWhiteSpace(document.Link.Title) ? document.Local?.Title : document.Link.Title;
                    if (string.IsNullOrWhiteSpace(documentTitle)) documentTitle = EditorialUi.TextResource("UxDocument");
                    documentSection.Add(ActionButton(documentTitle, async () => {
                        if (!Current() || !TripDayOverview.CanOpenDocument(document.Link, document.Local,
                            sessions.HasCuratedDocs && sessions.HasKnownValidAccess)) return;
                        try { await store.OpenLinkedAsync(document.Link); }
                        catch (OperationCanceledException) { }
                        catch (Exception exception)
                        {
                            ClientDiagnostics.Record("tomorrow_document_open_failed", exception: exception);
                            if (Current()) await DisplayAlertAsync("YUKU", EditorialUi.TextResource("TripSearchOpenFailed"), EditorialUi.TextResource("UxOk"));
                        }
                    }, image: "doc_open.svg"));
                }
                documentsContainer.IsVisible = visible.Length > 0;
            }
            offlineStatus.Text = metadata.OfflineReady is { } ready
                ? EditorialUi.TextResource(ready ? "TomorrowOfflineReady" : "TomorrowOfflineMissing") : "";
            offlineStatus.IsVisible = !string.IsNullOrWhiteSpace(offlineStatus.Text);
        }
        RenderMetadata();
        try
        {
            var incomplete = await metadata.RefreshAsync(async token => {
                var links = MauiProgram.Services.GetRequiredService<ReservationDocumentLinkStore>();
                var documents = new List<TripDayDocument>();
                var files = await store.ListAsync(token).WaitAsync(token);
                Check(token);
                foreach (var reservation in TripDayOverview.DocumentReservations(schedule, date))
                {
                    var link = await links.GetAsync(reservation, token).WaitAsync(token); Check(token);
                    if (link is null) continue;
                    var local = link.LocalDocumentId is { } id ? files.FirstOrDefault(file => file.Id == id) : null;
                    if (!TripDayOverview.CanOpenDocument(link, local, sessions.HasCuratedDocs && sessions.HasKnownValidAccess)) continue;
                    if (link.LocalDocumentId is { } localId && !await store.ExistsAsync(localId, token).WaitAsync(token)) continue;
                    Check(token);
                    documents.Add(new(link, local));
                }
                return TripDayOverview.DistinctDocuments(documents);
            }, async token => {
                var manifest = await MauiProgram.Services.GetRequiredService<OfflineTripPreparationService>().GetAsync().WaitAsync(token);
                Check(token);
                return manifest is { IsComplete: true } && manifest.TripId == schedule.TripId && manifest.Revision == schedule.Revision;
            }, Current, ct, RenderMetadata,
                (source, exception) => ClientDiagnostics.Record("tomorrow_" + source + "_metadata_failed", exception: exception));
            Check(ct); failure.IsVisible = retry.IsVisible = incomplete;
        }
        catch (OperationCanceledException) { }
        finally { if (!ct.IsCancellationRequested && Current()) loading.IsRunning = loading.IsVisible = false; }
    }

    private static bool SameText(string left, string right) =>
        string.Equals(left.Trim(), right.Trim(), StringComparison.OrdinalIgnoreCase);

    private static Button ActionButton(string text, Func<Task> action, bool primary = false, string? image = null)
    {
        var button = EditorialUi.Button(text, action, primary);
        button.ImageSource = image ?? (primary ? "today_arrow.svg" : "today_arrow_dark.svg");
        button.ContentLayout = new Button.ButtonContentLayout(Button.ButtonContentLayout.ImagePosition.Right, 12);
        button.LineBreakMode = LineBreakMode.WordWrap;
        if (!primary)
        {
            button.BackgroundColor = EditorialUi.Paper;
            button.BorderColor = EditorialUi.Line;
            button.BorderWidth = 1;
        }
        return button;
    }

    private bool Current() => journal.IsCurrent(scope);
    private void Check(CancellationToken ct) { ct.ThrowIfCancellationRequested(); if (!Current()) throw new OperationCanceledException(); }
    protected override void OnDisappearing() { cancellation?.Cancel(); base.OnDisappearing(); }
}

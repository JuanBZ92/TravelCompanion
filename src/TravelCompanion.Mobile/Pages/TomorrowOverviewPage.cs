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
        content.Add(header); content.Add(EditorialUi.Text(date.ToString("dddd d MMMM")));
        var first = TripDayOverview.FirstBooking(schedule, date);
        if (first is null) content.Add(EditorialUi.Text(EditorialUi.TextResource("TomorrowNoBooking")));
        else
        {
            var booking = new VerticalStackLayout { Spacing = 8 };
            booking.Add(EditorialUi.Text(first.StartsAt.ToString("HH:mm"), 13));
            booking.Add(EditorialUi.Heading(first.Title, 24, SemanticHeadingLevel.Level2));
            booking.Add(EditorialUi.Text(string.IsNullOrWhiteSpace(first.LocationName) ? first.City : first.LocationName));
            booking.Add(EditorialUi.Button(ExpenseUi.T("Ver reserva", "View booking"), async () => {
                if (!Current()) return;
                var vm = MauiProgram.Services.GetRequiredService<ViewModels.ScheduleItemDetailViewModel>();
                vm.ApplyQueryAttributes(new Dictionary<string, object> { ["ScheduleItem"] = first });
                await Navigation.PushModalAsync(new ScheduleItemDetailPage(vm));
            }));
            content.Add(EditorialUi.Card(booking));
        }
        var stay = TripDayOverview.Hotel(schedule, date);
        var hotelName = hotel?.Name ?? stay?.LocationName ?? stay?.Title;
        if (!string.IsNullOrWhiteSpace(hotelName))
            content.Add(EditorialUi.Button(hotelName, async () => {
                if (Current()) await GoogleMapsLauncher.OpenAsync($"{hotelName}, {hotel?.Address ?? stay?.Address}",
                    hotel?.ProviderPlaceId ?? stay?.ProviderPlaceId);
            }));
        content.Add(EditorialUi.Heading(EditorialUi.TextResource("TomorrowDocuments"), 24, SemanticHeadingLevel.Level2));
        var documentSection = new VerticalStackLayout { Spacing = 8 };
        var offlineStatus = EditorialUi.Text("", 13);
        var failure = EditorialUi.Text(EditorialUi.TextResource("TomorrowMetadataFailed"), 13); failure.IsVisible = false;
        var retry = EditorialUi.Button(EditorialUi.TextResource("UxRetry"), LoadAsync); retry.IsVisible = false;
        var loading = new ActivityIndicator { IsVisible = true, IsRunning = true, Color = EditorialUi.Accent };
        content.Add(documentSection); content.Add(offlineStatus); content.Add(loading); content.Add(failure); content.Add(retry);
        content.Add(EditorialUi.Button(EditorialUi.TextResource("TomorrowOpenDay"), async () => {
            if (!Current()) return;
            await Navigation.PopModalAsync();
            if (!Current()) return;
            await MauiProgram.Services.GetRequiredService<ViewModels.ScheduleViewModel>().SelectInitialDateAsync(date);
        }, primary: true));
        Content = new ScrollView { Content = content };
        var store = MauiProgram.Services.GetRequiredService<TripDocumentStore>();
        void RenderMetadata()
        {
            documentSection.Clear();
            if (metadata.Documents is { } documents)
            {
                var visible = documents.Where(document => TripDayOverview.CanOpenDocument(document.Link,
                    document.Local, sessions.HasCuratedDocs && sessions.HasKnownValidAccess)).ToArray();
                foreach (var document in visible)
                    documentSection.Add(EditorialUi.Button(document.Link.Title, async () => {
                        if (!Current() || !TripDayOverview.CanOpenDocument(document.Link, document.Local,
                            sessions.HasCuratedDocs && sessions.HasKnownValidAccess)) return;
                        try { await store.OpenLinkedAsync(document.Link); }
                        catch (OperationCanceledException) { }
                        catch (Exception exception)
                        {
                            ClientDiagnostics.Record("tomorrow_document_open_failed", exception: exception);
                            if (Current()) await DisplayAlertAsync("YUKU", EditorialUi.TextResource("TripSearchOpenFailed"), EditorialUi.TextResource("UxOk"));
                        }
                    }));
                if (visible.Length == 0) documentSection.Add(EditorialUi.Text(EditorialUi.TextResource("TomorrowNoDocuments"), 13));
            }
            offlineStatus.Text = metadata.OfflineReady is { } ready
                ? EditorialUi.TextResource(ready ? "TomorrowOfflineReady" : "TomorrowOfflineMissing") : "";
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

    private bool Current() => journal.IsCurrent(scope);
    private void Check(CancellationToken ct) { ct.ThrowIfCancellationRequested(); if (!Current()) throw new OperationCanceledException(); }
    protected override void OnDisappearing() { cancellation?.Cancel(); base.OnDisappearing(); }
}

using System.Collections.ObjectModel;
using TravelCompanion.Mobile.Services;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Mobile.Pages;

public sealed class TripSearchPage : TripScopedPage
{
    private readonly AuthSessionService sessions = MauiProgram.Services.GetRequiredService<AuthSessionService>();
    private readonly JournalStore journal = MauiProgram.Services.GetRequiredService<JournalStore>();
    private readonly TripDocumentStore documents = MauiProgram.Services.GetRequiredService<TripDocumentStore>();
    private readonly ObservableCollection<TripSearchResult> results = [];
    private readonly SearchBar search = new() { Placeholder = EditorialUi.TextResource("TripSearchPlaceholder"), BackgroundColor = Colors.Transparent };
    private readonly Label status = EditorialUi.Text("", 13);
    private readonly ActivityIndicator loading = new() { Color = EditorialUi.Accent, IsVisible = false };
    private readonly Button retry;
    private readonly JournalScope scope;
    private TripSearchIndex index = new([]);
    private readonly TripSearchSourceLoader sources = new();
    private IReadOnlyList<ScheduleItemDto> activities = [];
    private CancellationTokenSource? cancellation;
    private CancellationTokenSource? debounce;
    private TripSearchKind? filter;
    private bool opening;
    private bool incomplete;

    public TripSearchPage(TripSearchKind? initialFilter = null)
    {
        scope = journal.Scope(); filter = initialFilter;
        BackgroundColor = EditorialUi.Paper; SafeAreaEdges = SafeAreaEdges.All;
        var header = new Grid { ColumnDefinitions = [new(GridLength.Star), new(new GridLength(48))] };
        var heading = EditorialUi.Heading(EditorialUi.TextResource("TripSearchTitle"));
        header.Add(heading);
        header.Add(EditorialUi.Icon("action_close.svg", EditorialUi.TextResource("UxClose"), () => Navigation.PopModalAsync()), 1);
        search.TextChanged += SearchChanged;
        var filters = new HorizontalStackLayout { Spacing = 4 };
        foreach (var choice in new TripSearchKind?[] { null, TripSearchKind.Reservation, TripSearchKind.Document, TripSearchKind.Memory })
        {
            var label = choice is null ? ExpenseUi.T("Todo", "All") : EditorialUi.TextResource(choice switch {
                TripSearchKind.Reservation => "TripSearchReservations", TripSearchKind.Document => "TripSearchDocuments", _ => "TripSearchMemories" });
            var button = EditorialUi.Button(label, () => { filter = choice; UpdateResults(); return Task.CompletedTask; });
            button.FontSize = 13;
            button.Clicked += (_, _) => { foreach (var sibling in filters.Children.OfType<Button>()) sibling.BackgroundColor = Colors.Transparent; button.BackgroundColor = EditorialUi.Line; };
            if (choice == filter) button.BackgroundColor = EditorialUi.Line;
            filters.Add(button);
        }
        retry = EditorialUi.Button(EditorialUi.TextResource("UxRetry"), LoadAsync); retry.IsVisible = false;
        var top = new VerticalStackLayout { Spacing = 8, Padding = new Thickness(24, 8, 24, 8) };
        top.Add(header); top.Add(search);
        var filterScroll = new ScrollView { Orientation = ScrollOrientation.Horizontal, HorizontalScrollBarVisibility = ScrollBarVisibility.Never, Content = filters };
        var hint = EditorialUi.Text(EditorialUi.TextResource("TripSearchHint"), 13);
        top.Add(filterScroll); top.Add(hint); top.Add(status); top.Add(loading); top.Add(retry);
        // Keep the close action and query visible while the keyboard uses the screen.
        // Filters retain their selection and return when typing finishes.
        search.Focused += (_, _) => { heading.IsVisible = hint.IsVisible = filterScroll.IsVisible = false; };
        search.Unfocused += (_, _) => { heading.IsVisible = hint.IsVisible = filterScroll.IsVisible = true; };
        search.SearchButtonPressed += (_, _) => search.Unfocus();
        var list = new CollectionView { ItemsSource = results, SelectionMode = SelectionMode.None, ItemsUpdatingScrollMode = ItemsUpdatingScrollMode.KeepScrollOffset,
            ItemTemplate = new DataTemplate(() => {
                var title = EditorialUi.Heading("", 20, SemanticHeadingLevel.Level2); title.SetBinding(Label.TextProperty, nameof(TripSearchResult.Title));
                var context = EditorialUi.Text("", 13); context.SetBinding(Label.TextProperty, nameof(TripSearchResult.Context));
                var kind = EditorialUi.Text("", 13); kind.SetBinding(Label.TextProperty, nameof(TripSearchResult.KindLabel));
                var content = new VerticalStackLayout { Spacing = 4 }; content.Add(kind); content.Add(title); content.Add(context);
                var card = EditorialUi.Card(content); card.Margin = new Thickness(24, 0, 24, 12);
                var tap = new TapGestureRecognizer(); tap.Tapped += async (_, _) => { if (card.BindingContext is TripSearchResult result) await OpenAsync(result); };
                card.GestureRecognizers.Add(tap);
                // A real button exposes the result as an accessible action with a 48 dp target.
                var open = EditorialUi.Button(ExpenseUi.T("Abrir", "Open"), async () => { if (card.BindingContext is TripSearchResult result) await OpenAsync(result); });
                open.HorizontalOptions = LayoutOptions.Start; content.Add(open); return card;
            }) };
        var layout = new Grid { RowDefinitions = [new(GridLength.Auto), new(GridLength.Star)] }; layout.Add(top); layout.Add(list, 0, 1); Content = layout;
    }
    protected override async void OnAppearing() { base.OnAppearing(); sessions.StateChanged += PermissionsChanged; if (journal.IsCurrent(scope)) await LoadAsync(); }
    protected override void OnDisappearing() { sessions.StateChanged -= PermissionsChanged; cancellation?.Cancel(); debounce?.Cancel(); base.OnDisappearing(); }
    private void PermissionsChanged(object? sender, EventArgs e) => Dispatcher.Dispatch(UpdateResults);
    private void Check(CancellationToken ct) { ct.ThrowIfCancellationRequested(); if (!journal.IsCurrent(scope)) throw new OperationCanceledException(); }
    private async Task LoadAsync()
    {
        cancellation?.Cancel(); cancellation?.Dispose(); cancellation = new(); var ct = cancellation.Token;
        loading.IsVisible = loading.IsRunning = true; retry.IsVisible = false;
        try
        {
            IReadOnlyList<LocalTripDocument> files = [];
            var loaded = await sources.LoadAsync([
                new("reservations", async token => {
                    var cached = await MauiProgram.Services.GetRequiredService<MobileBootstrapStore>().GetCachedAsync(cancellationToken: token); Check(token);
                    activities = cached?.Value.Schedule?.TripId == scope.TripId ? cached.Value.Schedule.Items : [];
                    return activities.Select(x => new TripSearchResult("activity-" + x.Id, TripSearchKind.Reservation,
                        x.Title, $"{x.Date:d MMM yyyy} · {x.City} · {x.LocationName}", x.Date, x)).ToArray();
                }),
                new("personal_documents", async token => {
                    files = await documents.ListAsync(token); Check(token);
                    return files.Where(x => x.SourceUrl is null || sessions.HasCuratedDocs && sessions.HasKnownValidAccess)
                        .Select(x => new TripSearchResult("file-" + x.Id, TripSearchKind.Document, x.Title,
                            JournalText.Get("DocumentCategory_" + (x.Category ?? LocalDocumentCategory.Other)), null, x)).ToArray();
                }),
                new("curated_documents", async token => {
                    if (!sessions.HasCuratedDocs || !sessions.HasKnownValidAccess) return [];
                    var docs = await MauiProgram.Services.GetRequiredService<OfflineCacheService>().GetAsync<TravelDocsDto>(
                        $"mobile-docs-{scope.TripId}-{scope.UserId}", cancellationToken: token); Check(token);
                    return docs?.Value.TripId == scope.TripId && sessions.HasCuratedDocs && sessions.HasKnownValidAccess
                        ? docs.Value.HotelDocuments.Concat(docs.Value.OtherDocuments).Where(x => !files.Any(file => file.SourceUrl == x.FileUrl))
                            .Select(x => new TripSearchResult("curated-" + x.Id, TripSearchKind.Document, x.Title, x.Subtitle, null, x)).ToArray()
                        : [];
                }),
                new("memories", async token => {
                    var memories = await journal.ReadConfirmedLocalAsync(scope, token); Check(token);
                    return memories.Select(x => new TripSearchResult(x.Key, TripSearchKind.Memory, JournalText.DisplayTitle(x),
                        $"{x.Date:d MMM yyyy} · {x.City}", x.Date, x)).ToArray();
                })], () => journal.IsCurrent(scope), ct,
                snapshot => { index = snapshot; UpdateResults(); },
                (source, exception) => ClientDiagnostics.Record("trip_search_" + source + "_failed", exception: exception));
            Check(ct); index = loaded.Index; incomplete = loaded.HasIncompleteSources; retry.IsVisible = incomplete; UpdateResults();
        }
        catch (OperationCanceledException) { }
        catch (Exception exception) { if (journal.IsCurrent(scope) && !ct.IsCancellationRequested) { status.Text = EditorialUi.TextResource("TripSearchFailed"); retry.IsVisible = true; ClientDiagnostics.Record("trip_search_failed", exception: exception); } }
        finally { if (!ct.IsCancellationRequested && journal.IsCurrent(scope)) loading.IsVisible = loading.IsRunning = false; }
    }
    private async void SearchChanged(object? sender, TextChangedEventArgs e)
    {
        debounce?.Cancel(); debounce?.Dispose(); debounce = new(); var ct = debounce.Token;
        try { await Task.Delay(200, ct); Check(ct); UpdateResults(); } catch (OperationCanceledException) { }
    }
    private void UpdateResults()
    {
        if (!journal.IsCurrent(scope)) return;
        var permitted = index.Find(search.Text ?? "", filter).Where(x => x.Item switch {
            TravelDocumentDto => sessions.HasCuratedDocs && sessions.HasKnownValidAccess,
            LocalTripDocument file when file.SourceUrl is not null => sessions.HasCuratedDocs && sessions.HasKnownValidAccess,
            _ => true }).ToArray();
        JournalEntries.ReconcileRows(results, permitted, x => $"{x.Kind}:{x.Key}");
        status.Text = incomplete ? EditorialUi.TextResource("TripSearchPartial")
            : results.Count == 0 ? EditorialUi.TextResource("TripSearchEmpty") : "";
    }
    private async Task OpenAsync(TripSearchResult result)
    {
        if (opening || !journal.IsCurrent(scope)) return; opening = true;
        try
        {
            switch (result.Item)
            {
                case ScheduleItemDto activity:
                    var viewModel = MauiProgram.Services.GetRequiredService<ViewModels.ScheduleItemDetailViewModel>();
                    viewModel.ApplyQueryAttributes(new Dictionary<string, object> { ["ScheduleItem"] = activity });
                    await Navigation.PushModalAsync(new ScheduleItemDetailPage(viewModel)); break;
                case LocalTripDocument file:
                    if (file.SourceUrl is not null && (!sessions.HasCuratedDocs || !sessions.HasKnownValidAccess)) return;
                    await documents.OpenAsync(file.Id); break;
                case TravelDocumentDto file:
                    if (!sessions.HasCuratedDocs || !sessions.HasKnownValidAccess) return;
                    await documents.OpenLinkedAsync(new(Guid.Empty, null, file.FileUrl, file.Title)); break;
                case JournalMemory memory:
                    await Navigation.PushModalAsync(new JournalReadingPage(scope, memory, memory.IsFree ? null : activities.FirstOrDefault(x => x.Id == memory.Id))); break;
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception exception) { ClientDiagnostics.Record("trip_search_open_failed", exception: exception); if (journal.IsCurrent(scope)) await DisplayAlertAsync("YUKU", EditorialUi.TextResource("TripSearchOpenFailed"), EditorialUi.TextResource("UxOk")); }
        finally { opening = false; }
    }
}

using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text;
using TravelCompanion.Mobile.Services;
using TravelCompanion.Shared;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Mobile.Pages;

/// <summary>Explicit calendar exchange. Preview is local; only confirmation changes the itinerary.</summary>
public sealed class TripCalendarPage : TripScopedPage
{
    private readonly AuthSessionService sessions = MauiProgram.Services.GetRequiredService<AuthSessionService>();
    private readonly TravelCompanionApiClient api = MauiProgram.Services.GetRequiredService<TravelCompanionApiClient>();
    private readonly MobileBootstrapStore bootstrap = MauiProgram.Services.GetRequiredService<MobileBootstrapStore>();
    private readonly Guid? user;
    private readonly Guid? trip;
    private readonly long version;
    private readonly ObservableCollection<CalendarRow> rows = [];
    private readonly Label status = EditorialUi.Text("", 13);
    private readonly Label zone = EditorialUi.Text("", 13);
    private readonly Label explanation = EditorialUi.Text("", 13);
    private readonly ActivityIndicator loading = new() { Color = EditorialUi.Accent };
    private readonly Button confirm;
    private readonly Button choose;
    private readonly Button retry;
    private readonly Button exportMode;
    private readonly Button importMode;
    private readonly CollectionView list;
    private readonly CancellationTokenSource lifetime = new();
    private TripScheduleDto? schedule;
    private IReadOnlyDictionary<Guid, RecommendationDto> recommendations = new Dictionary<Guid, RecommendationDto>();
    private UserEntitlementsDto? entitlements;
    private bool importing;
    private bool busy;
    private bool initialized;
    private string? exportedPath;

    public TripCalendarPage()
    {
        user = sessions.CurrentUserId; trip = sessions.CurrentTripId; version = sessions.ContextVersion;
        BackgroundColor = EditorialUi.Paper; SafeAreaEdges = SafeAreaEdges.All;
        var header = new Grid { ColumnDefinitions = [new(GridLength.Star), new(new GridLength(48))] };
        header.Add(EditorialUi.Heading(T("CalendarTitle")));
        header.Add(EditorialUi.Icon("action_close.svg", T("UxClose"), () => Navigation.PopModalAsync()), 1);
        var modes = new Grid { ColumnDefinitions = [new(GridLength.Star), new(GridLength.Star)], ColumnSpacing = 8 };
        exportMode = EditorialUi.Button(T("CalendarExport"), () => SetModeAsync(false));
        importMode = EditorialUi.Button(T("CalendarImport"), () => SetModeAsync(true));
        modes.Add(exportMode); modes.Add(importMode, 1);
        choose = EditorialUi.Button(T("CalendarChooseFile"), ChooseAsync);
        choose.IsVisible = false;
        retry = EditorialUi.Button(T("UxRetry"), LoadAsync); retry.IsVisible = false;
        var top = new VerticalStackLayout { Padding = new Thickness(24, 12, 24, 8), Spacing = 8 };
        top.Add(header); top.Add(modes); top.Add(zone); top.Add(explanation); top.Add(choose); top.Add(loading); top.Add(retry);
        list = new CollectionView { ItemsSource = rows, SelectionMode = SelectionMode.None,
            ItemTemplate = new DataTemplate(() =>
            {
                var check = new CheckBox { MinimumHeightRequest = 48, MinimumWidthRequest = 48, Color = EditorialUi.Accent, VerticalOptions = LayoutOptions.Start };
                check.SetBinding(CheckBox.IsCheckedProperty, nameof(CalendarRow.Selected), BindingMode.TwoWay);
                check.SetBinding(IsEnabledProperty, nameof(CalendarRow.Selectable));
                check.SetBinding(SemanticProperties.DescriptionProperty, nameof(CalendarRow.Title));
                var title = EditorialUi.Heading("", 20, SemanticHeadingLevel.Level2); title.SetBinding(Label.TextProperty, nameof(CalendarRow.Title));
                var subtitle = EditorialUi.Text("", 13); subtitle.SetBinding(Label.TextProperty, nameof(CalendarRow.Subtitle));
                var note = EditorialUi.Text("", 13); note.SetBinding(Label.TextProperty, nameof(CalendarRow.Note));
                var content = new VerticalStackLayout { Spacing = 4 }; content.Add(title); content.Add(subtitle); content.Add(note);
                var row = new Grid { ColumnDefinitions = [new(new GridLength(48)), new(GridLength.Star)], ColumnSpacing = 8 };
                row.Add(check); row.Add(content, 1);
                var card = EditorialUi.Card(row); card.Margin = new Thickness(24, 0, 24, 12);
                var tap = new TapGestureRecognizer(); tap.Tapped += (_, _) => { if (!busy && card.BindingContext is CalendarRow { Selectable: true } item) item.Selected = !item.Selected; };
                title.GestureRecognizers.Add(tap);
                return card;
            }), EmptyView = EditorialUi.Text(T("CalendarEmpty")) };
        confirm = EditorialUi.Button(T("CalendarShareSelection"), ConfirmAsync, primary: true);
        var footer = new VerticalStackLayout { Padding = new Thickness(24, 8, 24, 16), Spacing = 8 };
        footer.Add(status); footer.Add(confirm);
        var layout = new Grid { RowDefinitions = [new(GridLength.Auto), new(GridLength.Star), new(GridLength.Auto)] };
        layout.Add(top); layout.Add(list, 0, 1); layout.Add(footer, 0, 2); Content = layout;
        UpdateState();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        Connectivity.Current.ConnectivityChanged += ConnectivityChanged;
        sessions.StateChanged += SessionChanged;
        if (!initialized) { initialized = true; await LoadAsync(); }
        else UpdateState();
    }
    protected override void OnDisappearing()
    {
        Connectivity.Current.ConnectivityChanged -= ConnectivityChanged;
        sessions.StateChanged -= SessionChanged;
        // Native file/share pickers can temporarily cover this page. Scope checks remain active on return.
        if (!Navigation.ModalStack.Contains(this)) lifetime.Cancel();
        base.OnDisappearing();
    }
    private void ConnectivityChanged(object? sender, ConnectivityChangedEventArgs args) => Dispatcher.Dispatch(UpdateState);
    private void SessionChanged(object? sender, EventArgs args) => Dispatcher.Dispatch(() => { if (!Current()) lifetime.Cancel(); UpdateState(); });
    private bool Current() => sessions.HasSession && user.HasValue && trip.HasValue && sessions.CurrentUserId == user
        && sessions.CurrentTripId == trip && sessions.ContextVersion == version;
    private void Check() { lifetime.Token.ThrowIfCancellationRequested(); if (!Current()) throw new OperationCanceledException(); }
    private bool CanImport => Current() && sessions.CanEditItinerary && sessions.HasKnownValidAccess
        && !sessions.IsFreeMapPreview && !sessions.RequiresTripSetup;
    private static string T(string key) => EditorialUi.TextResource(key);

    private async Task LoadAsync()
    {
        if (busy || !Current()) return;
        SetBusy(true); retry.IsVisible = false;
        try
        {
            var cached = await bootstrap.GetCachedAsync(cancellationToken: lifetime.Token); Check();
            schedule = cached?.Value.Schedule is { } cachedSchedule && cachedSchedule.TripId == trip ? cachedSchedule : null;
            if (cached?.Value is { } cachedBootstrap && cachedBootstrap.Entitlements.UserId == user)
            {
                recommendations = cachedBootstrap.Recommendations.DistinctBy(item => item.Id).ToDictionary(item => item.Id);
                entitlements = cachedBootstrap.Entitlements;
            }
            if (schedule is null && Connectivity.Current.NetworkAccess == NetworkAccess.Internet)
            {
                var token = await sessions.GetTokenAsync(); Check();
                if (token is not null) schedule = await api.GetScheduleAsync(token, lifetime.Token);
                Check();
            }
            if (schedule is null || schedule.TripId != trip) { schedule = null; status.Text = T("CalendarLoadFailed"); retry.IsVisible = true; return; }
            zone.Text = string.Format(T("CalendarTimeZone"), schedule.TimeZoneId);
            ShowExportRows(); status.Text = "";
        }
        catch (OperationCanceledException) { }
        catch (Exception exception) { Fail("CalendarLoadFailed", exception); retry.IsVisible = true; }
        finally { SetBusy(false); }
    }

    private Task SetModeAsync(bool import)
    {
        if (busy || schedule is null || !Current()) return Task.CompletedTask;
        importing = import; rows.Clear(); status.Text = "";
        if (!import) ShowExportRows();
        UpdateState(); return Task.CompletedTask;
    }

    private void ShowExportRows()
    {
        rows.Clear();
        if (schedule is null) return;
        foreach (var item in schedule.Items.Where(CanExport).OrderBy(x => x.Date).ThenBy(x => x.StartsAt))
            AddRow(new(item.Title, $"{item.Date:d MMM yyyy} · {(item.HasExactTime ? item.StartsAt.ToString("HH:mm") : T("CalendarAllDay"))} · {item.TimeZoneId ?? schedule.TimeZoneId}", item.LocationName, item.Id));
    }

    private async Task ChooseAsync()
    {
        if (busy || schedule is null || !Current()) return;
        SetBusy(true);
        try
        {
            var file = await FilePicker.Default.PickAsync(new PickOptions
            {
                PickerTitle = T("CalendarChooseFile"),
                FileTypes = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
                {
                    [DevicePlatform.Android] = ["text/calendar", "application/ics", "application/octet-stream"],
                    [DevicePlatform.iOS] = ["com.apple.ical.ics", "public.calendar-event"],
                    [DevicePlatform.MacCatalyst] = ["com.apple.ical.ics", "public.calendar-event"],
                    [DevicePlatform.WinUI] = [".ics"]
                })
            });
            Check(); if (file is null) return;
            if (!Path.GetExtension(file.FileName).Equals(".ics", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("CalendarInvalidFile");
            await using var input = await file.OpenReadAsync(); Check();
            using var output = new MemoryStream(); var buffer = new byte[8192]; int read;
            while ((read = await input.ReadAsync(buffer, lifetime.Token)) > 0)
            {
                Check(); if (output.Length + read > CalendarExchange.MaximumBytes) throw new ArgumentException("CalendarFileTooLarge");
                output.Write(buffer, 0, read);
            }
            var text = new UTF8Encoding(false, true).GetString(output.ToArray());
            var preview = await Task.Run(() => CalendarExchange.Parse(text, schedule.TimeZoneId, schedule.StartsOn, schedule.EndsOn), lifetime.Token); Check();
            rows.Clear();
            foreach (var item in preview.Events)
            {
                var time = item.StartsAt.HasValue ? item.StartsAt.Value.ToString("HH:mm") + (item.EndsAt.HasValue ? "–" + item.EndsAt.Value.ToString("HH:mm") : "") : T("CalendarAllDay");
                var detail = $"{item.Date:d MMM yyyy} · {time} · {item.TimeZoneId}";
                var source = item.UsesTripTimeZone ? T("CalendarFloatingTime") : string.Format(T("CalendarOriginalZone"), item.SourceTimeZoneId);
                AddRow(new(item.Title, detail, string.Join("\n", new[] { item.Location, source, item.Notes }.Where(s => s.Length > 0)), item));
            }
            foreach (var issue in preview.Issues) AddRow(new(issue.Title.Length > 0 ? issue.Title : T("CalendarUnsupportedEvent"), T(issue.Reason)));
            status.Text = string.Format(T("CalendarPreviewSummary"), preview.Events.Count, preview.Issues.Count);
        }
        catch (OperationCanceledException) { }
        catch (DecoderFallbackException) { if (Current()) status.Text = T("CalendarInvalidFile"); }
        catch (ArgumentException exception) { if (Current()) status.Text = T(exception.Message.StartsWith("Calendar", StringComparison.Ordinal) ? exception.Message : "CalendarInvalidFile"); }
        catch (Exception exception) { Fail("CalendarReadFailed", exception); }
        finally { SetBusy(false); }
    }

    private async Task ConfirmAsync()
    {
        if (busy || schedule is null || !Current()) return;
        var selected = rows.Where(row => row.Selected && row.Selectable).ToArray();
        if (selected.Length == 0) return;
        if (importing && (!CanImport || Connectivity.Current.NetworkAccess != NetworkAccess.Internet)) { UpdateState(); return; }
        var snapshot = schedule;
        SetBusy(true);
        try
        {
            var message = string.Format(T(importing ? "CalendarImportConfirm" : "CalendarExportConfirm"), selected.Length);
            if (!await DisplayAlertAsync(T("CalendarTitle"), message, T(importing ? "CalendarImportSelection" : "CalendarShareSelection"), T("CommonCancel"))) return;
            Check();
            if (importing) await ImportAsync(selected);
            else
            {
                CheckExport(snapshot, selected);
                var content = await Task.Run(() => CalendarExchange.Export(snapshot, selected.Select(row => row.ItemId!.Value).ToArray(), DateTimeOffset.UtcNow), lifetime.Token);
                CheckExport(snapshot, selected);
                var directory = Path.Combine(FileSystem.CacheDirectory, "document-preview"); Directory.CreateDirectory(directory);
                exportedPath = Path.Combine(directory, $"Yuku-calendar-{Guid.NewGuid():N}.ics");
                await File.WriteAllTextAsync(exportedPath, content, new UTF8Encoding(false), lifetime.Token);
                CheckExport(snapshot, selected);
                await Share.Default.RequestAsync(new ShareFileRequest(T("CalendarTitle"), new ShareFile(exportedPath, "text/calendar"))); Check();
                status.Text = T("CalendarExported");
                // The existing preview cleanup removes this explicit export on logout, trip/account deletion and next startup.
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception exception) { Fail(importing ? "CalendarImportFailed" : "CalendarExportFailed", exception); }
        finally
        {
            if (!importing && (lifetime.IsCancellationRequested || !Current()
                || selected.Any(row => snapshot.Items.FirstOrDefault(item => item.Id == row.ItemId) is not { } item || !CanExport(item))))
            {
                try { if (exportedPath is not null && File.Exists(exportedPath)) File.Delete(exportedPath); }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { ClientDiagnostics.Record("calendar_export_cleanup_failed", exception: exception); }
            }
            SetBusy(false);
        }
    }

    private bool CanExport(ScheduleItemDto item)
    {
        var recommendation = item.RecommendationId is { } id ? recommendations.GetValueOrDefault(id) : null;
        return CalendarExchange.CanExportItem(item, sessions.HasKnownValidAccess, sessions.AccessMode, recommendation, entitlements);
    }
    private void CheckExport(TripScheduleDto snapshot, CalendarRow[] selected)
    {
        Check();
        if (snapshot.TripId != trip || selected.Any(row => row.ItemId is not { } id
            || snapshot.Items.FirstOrDefault(item => item.Id == id) is not { } item || !CanExport(item)))
        {
            if (exportedPath is not null && File.Exists(exportedPath)) File.Delete(exportedPath);
            throw new UnauthorizedAccessException();
        }
    }

    private async Task ImportAsync(CalendarRow[] selected)
    {
        var token = await sessions.GetTokenAsync(); Check(); if (token is null || !CanImport) throw new UnauthorizedAccessException();
        var current = await api.GetScheduleAsync(token, lifetime.Token); Check();
        if (current is null || current.TripId != trip) throw new OperationCanceledException();
        var snapshot = current;
        schedule = snapshot; var saved = 0; var failed = 0; var interrupted = false;
        foreach (var row in selected)
        {
            Check();
            if (!CanImport || Connectivity.Current.NetworkAccess != NetworkAccess.Internet) { interrupted = true; break; }
            var item = row.Event!;
            if (item.Date < snapshot.StartsOn || item.Date > snapshot.EndsOn) { row.Note = T("CalendarOutsideTrip"); failed++; continue; }
            var request = CalendarExchange.Mutation(item, snapshot.Revision);
            ItineraryItemMutationResponse? result;
            try
            {
                result = await api.CreateItineraryItemAsync(token, request, lifetime.Token, notifyChanged: false); Check();
                if (result?.HasOverlap == true)
                {
                    if (!await DisplayAlertAsync(T("CalendarOverlapTitle"), string.Format(T("CalendarOverlapMessage"), item.Title), T("CalendarImportSelection"), T("CommonCancel"))) { failed++; continue; }
                    Check();
                    if (!CanImport || Connectivity.Current.NetworkAccess != NetworkAccess.Internet) { interrupted = true; break; }
                    result = await api.CreateItineraryItemAsync(token, request with { ConfirmOverlap = true }, lifetime.Token, notifyChanged: false); Check();
                }
            }
            catch (OperationCanceledException) when (lifetime.IsCancellationRequested || !Current()) { throw; }
            catch (Exception exception) when (exception is HttpRequestException or TimeoutException or OperationCanceledException or System.Text.Json.JsonException)
            {
                ClientDiagnostics.Record("calendar_import_batch_paused", exception: exception);
                row.Note = T("CalendarImportUnconfirmed");
                interrupted = true; break;
            }
            catch (Exception exception) { ClientDiagnostics.Record("calendar_import_event_failed", exception: exception); row.Note = T("CalendarImportRetry"); failed++; continue; }
            if (result?.Success != true || result.Item is null)
            {
                if (result is not null && result.Revision > snapshot.Revision) snapshot = snapshot with { Revision = result.Revision };
                // A denial or an unavailable backend can return a JSON error body.
                // Pause instead of repeating that failed request for the remaining rows.
                row.Note = T("CalendarImportRetry"); interrupted = true; break;
            }
            snapshot = snapshot with { Revision = result.Revision, Items = snapshot.Items.Where(x => x.Id != result.Item.Id).Append(result.Item).ToArray() };
            schedule = snapshot;
            row.Selected = false; row.Note = T("CalendarSaved"); row.Selectable = false; saved++;
            await bootstrap.ReplaceScheduleAsync(snapshot, lifetime.Token); Check();
            status.Text = string.Format(T("CalendarProgress"), saved, selected.Length);
        }
        await MauiProgram.Services.GetRequiredService<BuilderTripStore>().UpdateRevisionAsync(snapshot.Revision, lifetime.Token); Check();
        if (saved > 0) await api.NotifyItineraryChangedAsync(); Check();
        status.Text = interrupted
            ? string.Format(T("CalendarImportPaused"), saved, selected.Length - saved)
            : string.Format(T("CalendarImported"), saved, failed);
    }

    private void AddRow(CalendarRow row) { row.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(CalendarRow.Selected)) UpdateState(); }; rows.Add(row); }
    private void SetBusy(bool value) { busy = value; loading.IsVisible = loading.IsRunning = value; UpdateState(); }
    private void UpdateState()
    {
        if (!importing && schedule is not null)
        {
            var allowed = schedule.Items.Where(CanExport).Select(item => item.Id).ToHashSet();
            foreach (var row in rows.Where(row => row.ItemId is { } id && !allowed.Contains(id)).ToArray()) rows.Remove(row);
        }
        list.IsEnabled = !busy;
        exportMode.IsEnabled = importMode.IsEnabled = !busy && schedule is not null;
        exportMode.BackgroundColor = importing ? Colors.Transparent : EditorialUi.Line;
        importMode.BackgroundColor = importing ? EditorialUi.Line : Colors.Transparent;
        confirm.Text = T(importing ? "CalendarImportSelection" : "CalendarShareSelection");
        choose.IsVisible = importing;
        choose.IsEnabled = !busy && schedule is not null && Current();
        confirm.IsEnabled = !busy && Current() && rows.Any(row => row.Selected && row.Selectable)
            && (!importing || CanImport && Connectivity.Current.NetworkAccess == NetworkAccess.Internet);
        explanation.Text = importing ? !CanImport ? T("CalendarPermissionRequired")
            : Connectivity.Current.NetworkAccess != NetworkAccess.Internet ? T("CalendarOfflineImport") : T("CalendarImportHint")
            : schedule is not null && schedule.Items.Any(item => !CanExport(item)) ? T("CalendarExportLimited") : T("CalendarExportHint");
    }
    private void Fail(string key, Exception exception)
    {
        ClientDiagnostics.Record("calendar_exchange_failed", exception: exception);
        if (Current()) status.Text = T(key);
    }

    private sealed class CalendarRow : INotifyPropertyChanged
    {
        public string Title { get; }
        public string Subtitle { get; }
        public Guid? ItemId { get; }
        public CalendarEvent? Event { get; }
        private bool selected;
        private bool selectable;
        private string note;
        public bool Selected { get => selected; set { selected = value; Changed(); } }
        public bool Selectable { get => selectable; set { selectable = value; Changed(); } }
        public string Note { get => note; set { note = value; Changed(); } }
        public CalendarRow(string title, string subtitle, string note, Guid itemId) { Title = title; Subtitle = subtitle; this.note = note; ItemId = itemId; selectable = true; }
        public CalendarRow(string title, string subtitle, string note, CalendarEvent item) { Title = title; Subtitle = subtitle; this.note = note; Event = item; selectable = true; }
        public CalendarRow(string title, string reason) { Title = title; Subtitle = reason; note = ""; }
        public event PropertyChangedEventHandler? PropertyChanged;
        private void Changed([CallerMemberName] string? property = null) => PropertyChanged?.Invoke(this, new(property));
    }
}

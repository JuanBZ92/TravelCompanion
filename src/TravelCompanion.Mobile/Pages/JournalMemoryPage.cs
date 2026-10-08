using TravelCompanion.Mobile.Services;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Mobile.Pages;

internal static class JournalUi
{
    public static Color Ink => EditorialUi.Ink;
    public static Color Muted => EditorialUi.Muted;
    public static Color Paper => EditorialUi.Paper;
    public static Label Text(string text, double size = 15, bool heading = false) => new()
    { Text = text, FontSize = size, FontFamily = heading ? "serif" : null,
        TextColor = heading ? Ink : Muted, LineHeight = 1.2 };
    public static Button Action(string key, Func<Task> action, bool primary = false)
    {
        var button = new Button { Text = JournalText.Get(key), MinimumHeightRequest = 48, CornerRadius = 8,
            BackgroundColor = primary ? Ink : Colors.Transparent, TextColor = primary ? Colors.White : Ink };
        button.Clicked += async (_, _) => { if (!button.IsEnabled) return; button.IsEnabled = false;
            try { await action(); } catch (OperationCanceledException) { }
            catch (Exception) { await Shell.Current.DisplayAlertAsync(JournalText.Get("JournalHeading"), JournalText.Get("JournalFailure"), JournalText.Get("JournalOk")); }
            finally { button.IsEnabled = true; } };
        return button;
    }
    public static ImageButton Icon(string source, string label, Func<Task> action)
    {
        var button = new ImageButton { Source = source, Padding = 12, WidthRequest = 48, HeightRequest = 48, BackgroundColor = Colors.Transparent };
        SemanticProperties.SetDescription(button, label);
        button.Clicked += async (_, _) => { if (!button.IsEnabled) return; button.IsEnabled = false;
            try { await action(); } catch (OperationCanceledException) { }
            catch (Exception) { await Shell.Current.DisplayAlertAsync(JournalText.Get("JournalHeading"), JournalText.Get("JournalFailure"), JournalText.Get("JournalOk")); }
            finally { button.IsEnabled = true; } };
        return button;
    }
    public static VerticalStackLayout Tool(string source, string key, Func<Task> action, bool primary = false)
    {
        var label = JournalText.Get(key);
        var icon = Icon(source, label, action);
        icon.WidthRequest = 52;
        icon.HeightRequest = 52;
        icon.CornerRadius = 14;
        icon.HorizontalOptions = LayoutOptions.Center;
        if (primary) icon.BackgroundColor = Ink;
        return new VerticalStackLayout { Spacing = 3, HorizontalOptions = LayoutOptions.Fill,
            MinimumWidthRequest = 72, Children = { icon, new Label { Text = label, FontSize = 11,
                LineBreakMode = LineBreakMode.WordWrap, HorizontalTextAlignment = TextAlignment.Center,
                TextColor = primary ? Ink : Muted } } };
    }
}

public sealed class JournalActivityPickerPage : JournalScopedPage
{
    public JournalActivityPickerPage(IReadOnlyList<ScheduleItemDto> activities, IReadOnlyList<JournalMemory> memories,
        DateOnly? selectedDate = null, Func<ScheduleItemDto, Task>? onSelected = null)
    {
        BackgroundColor = JournalUi.Paper; SafeAreaEdges = SafeAreaEdges.All;
        var store = MauiProgram.Services.GetRequiredService<JournalStore>(); var scope = store.Scope();
        var search = new SearchBar { Placeholder = JournalText.Get("JournalSearchActivity"), MinimumHeightRequest = 48 };
        SemanticProperties.SetDescription(search, JournalText.Get("JournalSearchActivity"));
        var list = new CollectionView { SelectionMode = SelectionMode.Single,
            ItemsSource = ScheduleActivityLookup.SearchJournalActivities(activities, selectedDate),
            EmptyView = JournalUi.Text(JournalText.Get("JournalNoMatchingActivities")) };
        list.ItemTemplate = new DataTemplate(() => {
            var title = JournalUi.Text("", 19, true); title.SetBinding(Label.TextProperty, "Title");
            var date = JournalUi.Text("", 12); date.SetBinding(Label.TextProperty, new Binding("Date", stringFormat: "{0:d MMMM}"));
            return new VerticalStackLayout { Padding = new Thickness(0, 12), Spacing = 6, MinimumHeightRequest = 48, Children = { date, title } }; });
        search.TextChanged += (_, e) => list.ItemsSource = ScheduleActivityLookup.SearchJournalActivities(activities, selectedDate, e.NewTextValue);
        var opening = false;
        list.SelectionChanged += async (_, e) => {
            if (opening || e.CurrentSelection.FirstOrDefault() is not ScheduleItemDto item) return;
            list.SelectedItem = null; if (!store.IsCurrent(scope)) return;
            opening = true;
            try {
                if (onSelected is not null)
                {
                    await onSelected(item);
                    if (store.IsCurrent(scope)) await Navigation.PopModalAsync();
                    return;
                }
                var memory = memories.FirstOrDefault(x => !x.IsFree && x.Id == item.Id)
                    ?? new JournalMemory(new(item.Id, scope.TripId, item.Title, item.City, item.Date, "", 0, DateTimeOffset.UtcNow), IsDraft: true);
                await Navigation.PushModalAsync(new JournalMemoryPage(scope, memory, item));
            } finally { opening = false; }
        };
        var header = new Grid { ColumnDefinitions = [new(GridLength.Star), new(GridLength.Auto)] };
        header.Add(JournalUi.Text(selectedDate is { } day
            ? JournalText.Format("JournalActivitiesOnDate", day.ToString("d MMMM yyyy"))
            : JournalText.Get("JournalChooseActivity"), 26, true));
        header.Add(JournalUi.Icon("action_close.svg", JournalText.Get("JournalClose"), () => Navigation.PopModalAsync()), 1);
        var grid = new Grid { Padding = 24, RowSpacing = 16, RowDefinitions = [new(GridLength.Auto), new(GridLength.Auto), new(GridLength.Star)] };
        grid.Add(header); grid.Add(search, 0, 1); grid.Add(list, 0, 2); Content = grid;
    }
}

public sealed class JournalMemoryPage : JournalScopedPage
{
    private readonly JournalStore store = MauiProgram.Services.GetRequiredService<JournalStore>();
    private readonly JournalScope scope;
    private JournalMemory memory;
    private readonly Editor editor;
    private readonly Entry title;
    private readonly Entry place;
    private readonly DatePicker date;
    private readonly VerticalStackLayout photos = new() { Spacing = 12 };
    private readonly Label status = JournalUi.Text("", 12);
    private readonly Label counter = JournalUi.Text("", 12);
    private readonly Button retrySynchronization;
    private readonly Button conflictAction;
    private readonly List<JournalMemory> confirmations = [];
    private CancellationTokenSource? debounce;
    private Task autosave = Task.CompletedTask;
    private readonly SemaphoreSlim persistGate = new(1, 1);
    private long editVersion;
    private bool busy, closing, initialized, dirty, suppress;
    private bool visible, hasDraftChanges, deferredSynchronizationRefresh;
    private long synchronizationRefresh;
    private bool pickPhotos;
    private Window? observedWindow;
    private readonly JournalDraft? initialDraft;
    public JournalMemoryPage(JournalScope scope, JournalMemory memory, ScheduleItemDto? item, bool startWithPhotos = false, JournalDraft? draft = null)
    {
        this.scope = scope; this.memory = memory; initialDraft = draft; pickPhotos = startWithPhotos;
        if (!memory.IsDraft && !memory.HasConflict && memory.DeletePending is null
            && (memory.Pending is not null || memory.FreePending is not null))
            confirmations.Add(memory); // The pending payload is already visible to this editor.
        BackgroundColor = JournalUi.Paper; SafeAreaEdges = SafeAreaEdges.All;
        title = new Entry { Text = memory.Title, Placeholder = JournalText.Get("JournalTitleOptional"), MaxLength = 120, FontFamily = "serif", FontSize = 26, TextColor = JournalUi.Ink, IsVisible = memory.IsFree };
        place = new Entry { Text = memory.City, Placeholder = JournalText.Get("JournalPlaceOptional"), MaxLength = 200, TextColor = JournalUi.Ink, IsVisible = memory.IsFree };
        date = new DatePicker { MinimumDate = new DateTime(1900, 1, 1), MaximumDate = new DateTime(2100, 12, 31),
            Date = memory.Date.ToDateTime(TimeOnly.MinValue), Format = "d MMMM yyyy", IsEnabled = memory.IsFree, MinimumHeightRequest = 48, TextColor = JournalUi.Ink };
        editor = new Editor { Text = draft?.Text ?? memory.Text, Placeholder = JournalText.Get("JournalPrompt"), MaxLength = 2000,
            AutoSize = EditorAutoSizeOption.TextChanges, MinimumHeightRequest = 200, FontSize = 18, TextColor = JournalUi.Ink, BackgroundColor = Colors.Transparent };
#if ANDROID
        // The native underline cuts across the paper cards on Android.
        foreach (var field in new Microsoft.Maui.Controls.View[] { title, place, editor })
            field.HandlerChanged += (_, _) => {
                if (field.Handler?.PlatformView is Android.Widget.EditText native)
                    native.BackgroundTintList = Android.Content.Res.ColorStateList.ValueOf(Android.Graphics.Color.Transparent);
            };
#endif
        SemanticProperties.SetDescription(date, JournalText.Get("JournalDate"));
        var header = new Grid { Padding = new Thickness(22, 8), ColumnDefinitions = [new(GridLength.Star), new(GridLength.Auto), new(GridLength.Auto)] };
        var heading = JournalUi.Text(JournalText.Get("JournalWrite"), 22, true);
        heading.VerticalOptions = LayoutOptions.Center;
        header.Add(heading);
        header.Add(JournalUi.Icon("action_delete.svg", JournalText.Get("JournalDiscardDraft"), DiscardAsync), 1);
        header.Add(JournalUi.Icon("action_close.svg", JournalText.Get("JournalClose"), CloseAsync), 2);
        var body = new VerticalStackLayout { Padding = new Thickness(22, 18, 22, 28), Spacing = 18 };
        var dateField = new VerticalStackLayout { Spacing = 4, Children = { JournalUi.Text(JournalText.Get("JournalDate"), 12), date } };
        body.Add(dateField);
        if (memory.IsFree)
        {
            body.Add(new Border { BackgroundColor = Color.FromArgb("#FFFCF8"), Stroke = Color.FromArgb("#E5DDD3"),
                StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = new CornerRadius(12) },
                Padding = new Thickness(14, 6), Content = new VerticalStackLayout { Spacing = 2, Children = { title, place } } });
            body.Add(JournalUi.Text(JournalText.Get("JournalActivityDraftNotice"), 12));
        }
        if (!memory.IsFree) body.Add(JournalUi.Text(JournalText.Title(memory), 28, true));
        body.Add(new Border { BackgroundColor = Color.FromArgb("#FFFCF8"), Stroke = Color.FromArgb("#E5DDD3"),
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = new CornerRadius(12) },
            Padding = new Thickness(18, 12), Content = editor });
        body.Add(counter); body.Add(photos);
        body.Add(JournalUi.Text(JournalText.Get("JournalStorageNotice"), 12));
        var tools = new Grid { ColumnSpacing = 8,
            ColumnDefinitions = memory.IsFree
                ? [new(GridLength.Star), new(GridLength.Star), new(GridLength.Star)]
                : [new(GridLength.Star), new(GridLength.Star)] };
        tools.Add(JournalUi.Tool("journal_photo.svg", "JournalAddPhotos", AddPhotosAsync), 0);
        if (memory.IsFree) tools.Add(JournalUi.Tool("journal_search.svg", "JournalFindDayActivity", FindDayActivityAsync), 1);
        tools.Add(JournalUi.Tool("action_saved.svg", "JournalSave", SaveAsync, true), memory.IsFree ? 2 : 1);
        retrySynchronization = JournalUi.Action("JournalSyncRetry", RetrySynchronizationAsync);
        retrySynchronization.IsVisible = false;
        retrySynchronization.LineBreakMode = LineBreakMode.WordWrap;
        SemanticProperties.SetDescription(retrySynchronization, JournalText.Get("JournalSyncRetry"));
        conflictAction = JournalUi.Action("JournalCompare", ResolveAsync);
        var footer = new VerticalStackLayout { Padding = new Thickness(22, 8, 22, 16), Spacing = 8,
            Children = { status, retrySynchronization, tools } };
        footer.BackgroundColor = Color.FromArgb("#FFFCF8");
        var grid = new Grid { RowDefinitions = [new(GridLength.Auto), new(GridLength.Star), new(GridLength.Auto)] };
        grid.Add(header); grid.Add(new ScrollView { Content = body }, 0, 1); grid.Add(footer, 0, 2); Content = grid;
        editor.TextChanged += (_, _) => Changed(); title.TextChanged += (_, _) => Changed(); place.TextChanged += (_, _) => Changed();
        date.DateSelected += (_, _) => Changed(); UpdateCounter();
    }
    private void UpdateCounter() => counter.Text = JournalText.Format("JournalCharacterCount", editor.Text?.Length ?? 0);
    private async Task FindDayActivityAsync()
    {
        if (busy || !store.IsCurrent(scope)) return;
        busy = true; date.IsEnabled = false;
        try
        {
            debounce?.Cancel(); await autosave; await PersistAsync();
            var selectedDate = DateOnly.FromDateTime(date.Date ?? DateTime.Today);
            var bootstrap = MauiProgram.Services.GetRequiredService<MobileBootstrapStore>();
            var cached = await bootstrap.GetCachedAsync();
            if (!store.IsCurrent(scope)) return;
            var activities = cached?.Value.Schedule is { } schedule && schedule.TripId == scope.TripId
                ? schedule.Items : [];
            if (!store.IsCurrent(scope)) return;
            await Navigation.PushModalAsync(new JournalActivityPickerPage(activities, [], selectedDate, item =>
            {
                if (!store.IsCurrent(scope)) return Task.CompletedTask;
                place.Text = item.Title;
                status.Text = JournalText.Get("JournalActivitySelected");
                return Task.CompletedTask;
            }));
        }
        finally { FinishBusy(); date.IsEnabled = memory.IsFree; }
    }
    private void Changed()
    {
        UpdateCounter(); if (suppress) return; dirty = hasDraftChanges = true; editVersion++;
        retrySynchronization.IsVisible = false;
        debounce?.Cancel(); debounce = new(); autosave = AutoSaveAsync(debounce.Token);
    }
    private JournalMemory Snapshot()
    {
        if (!memory.IsFree) return memory;
        var selected = DateOnly.FromDateTime(date.Date ?? DateTime.Today);
        // Metadata edits must retain the revision that the writer actually saw.
        return memory with { FreeEntry = memory.FreeEntry! with { Title = title.Text ?? "", Place = place.Text ?? "", Date = selected },
            FreePending = memory.FreePending is { } p ? p with { Title = title.Text ?? "", Place = place.Text ?? "", Date = selected } : null };
    }
    private async Task AutoSaveAsync(CancellationToken ct)
    {
        try { await Task.Delay(650, ct); await PersistAsync(ct); }
        catch (OperationCanceledException) { }
        catch (Exception) { status.Text = JournalText.Get("JournalDraftFailed"); }
    }
    private async Task PersistAsync(CancellationToken ct = default)
    {
        if (!dirty || !store.IsCurrent(scope)) return;
        await persistGate.WaitAsync(ct);
        try
        {
            if (!dirty || !store.IsCurrent(scope)) return;
            hasDraftChanges = true;
            retrySynchronization.IsVisible = false;
            status.Text = JournalText.Get("JournalSaving");
            var version = editVersion; var snapshot = Snapshot(); var text = editor.Text ?? "";
            await store.SaveDraftAsync(scope, snapshot, text, ct);
            if (version == editVersion)
            {
                dirty = false;
                status.Text = JournalText.Get("JournalDraftSaved");
            }
        }
        finally { persistGate.Release(); }
    }
    protected override async void OnAppearing()
    {
        base.OnAppearing();
        visible = true;
        store.SynchronizationChanged -= SynchronizationChanged;
        store.SynchronizationChanged += SynchronizationChanged;
        observedWindow = Window;
        if (observedWindow is not null) observedWindow.Stopped += AppStopped;
        try {
            if (!initialized) {
                var draft = initialDraft ?? (await store.DraftsAsync(scope)).FirstOrDefault(x => x.Memory.Key == memory.Key);
                if (!visible || !store.IsCurrent(scope)) return;
                if (draft is not null) {
                    hasDraftChanges = true;
                    if (!draft.Memory.HasConflict && draft.Memory.DeletePending is null
                        && (draft.Memory.Pending is not null || draft.Memory.FreePending is not null))
                        confirmations.Add(draft.Memory); // Draft.Text itself is never treated as confirmed.
                    suppress = true; memory = draft.Memory; editor.Text = draft.Text; title.Text = memory.Title; place.Text = memory.City;
                    date.Date = memory.Date.ToDateTime(TimeOnly.MinValue); suppress = false; status.Text = JournalText.Get("JournalDraftSaved");
                } else status.Text = memory.Status;
                initialized = true;
            }
            await RefreshSynchronizationAsync();
            if (!visible || !store.IsCurrent(scope)) return;
            await RenderPhotosAsync();
            if (pickPhotos) { pickPhotos = false; await AddPhotosAsync(); }
        } catch (OperationCanceledException) { } catch (Exception) { status.Text = JournalText.Get("JournalFailure"); }
    }
    protected override async void OnDisappearing()
    {
        visible = false; synchronizationRefresh++; deferredSynchronizationRefresh = false;
        store.SynchronizationChanged -= SynchronizationChanged;
        if (observedWindow is not null) observedWindow.Stopped -= AppStopped;
        observedWindow = null;
        debounce?.Cancel();
        try { await autosave; if (!closing) await PersistAsync(); } catch (Exception) { status.Text = JournalText.Get("JournalDraftFailed"); }
        base.OnDisappearing();
    }
    private async void AppStopped(object? sender, EventArgs e)
    {
        if (busy || closing) return;
        debounce?.Cancel();
        try { await autosave; await PersistAsync(); }
        catch (Exception) { status.Text = JournalText.Get("JournalDraftFailed"); }
    }
    private async Task RenderPhotosAsync()
    {
        photos.Clear();
        if (memory.HasConflict) photos.Add(conflictAction);
        photos.Add(JournalUi.Text(JournalText.Format("JournalPhotosLimit", memory.Images.Length), 12));
        var gallery = new FlexLayout { Wrap = Microsoft.Maui.Layouts.FlexWrap.Wrap,
            AlignItems = Microsoft.Maui.Layouts.FlexAlignItems.Start,
            AlignContent = Microsoft.Maui.Layouts.FlexAlignContent.Start };
        for (var i = 0; i < memory.Images.Length; i++) {
            var index = i; var photo = memory.Images[i]; var bytes = await store.PhotoAsync(scope, photo.Id, true);
            if (!store.IsCurrent(scope)) return;
            var tile = new Grid { WidthRequest = 100, HeightRequest = 156, RowSpacing = 8,
                RowDefinitions = [new(new GridLength(100)), new(new GridLength(48))],
                Margin = new Thickness(0, 0, 8, 8), VerticalOptions = LayoutOptions.Start };
            FlexLayout.SetAlignSelf(tile, Microsoft.Maui.Layouts.FlexAlignSelf.Start);
            var image = JournalUi.Icon("journal_photo.svg", JournalText.Format("JournalPhotoNumber", i + 1, memory.Images.Length),
                () => Navigation.PushModalAsync(new JournalPhotoPage(scope, memory, index)));
            image.WidthRequest = 100; image.HeightRequest = 100; image.Padding = 0; image.Aspect = Aspect.AspectFill;
            if (bytes is not null) image.Source = ImageSource.FromStream(() => new MemoryStream(bytes));
            tile.Add(new Border { StrokeThickness = 0, Padding = 0,
                StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = new CornerRadius(10) },
                Content = image });
            var controls = new Grid { ColumnDefinitions = [new(new GridLength(48)), new(new GridLength(48))], ColumnSpacing = 4 };
            var coverAction = JournalUi.Icon(photo.Id == memory.CoverId ? "expense_check.svg" : "journal_cover.svg",
                JournalText.Get(photo.Id == memory.CoverId ? "JournalCover" : "JournalSetCover"), async () => {
                if (busy || !store.IsCurrent(scope)) return;
                busy = true;
                try { memory = Snapshot() with { CoverId = photo.Id }; dirty = true; await PersistAsync(); await RenderPhotosAsync(); }
                finally { FinishBusy(); } });
            coverAction.BackgroundColor = photo.Id == memory.CoverId ? JournalUi.Ink : Colors.Transparent;
            coverAction.CornerRadius = 12;
            controls.Add(coverAction);
            controls.Add(JournalUi.Icon("action_delete.svg", JournalText.Get("JournalRemovePhoto"), async () => {
                if (busy || !store.IsCurrent(scope)) return;
                busy = true;
                try {
                    if (!await DisplayAlertAsync(JournalText.Get("JournalRemovePhoto"), JournalText.Get("JournalOriginalKept"), JournalText.Get("JournalRemove"), JournalText.Get("JournalCancel")) || !store.IsCurrent(scope)) return;
                    var remaining = memory.Images.Where(x => x.Id != photo.Id).ToArray();
                    memory = Snapshot() with { Photos = remaining, CoverId = memory.CoverId == photo.Id ? remaining.FirstOrDefault()?.Id : memory.CoverId };
                    dirty = true; await PersistAsync(); await RenderPhotosAsync();
                } finally { FinishBusy(); }
            }), 1);
            tile.Add(controls, 0, 1);
            gallery.Children.Add(tile);
        }
        photos.Add(gallery);
    }
    private async Task AddPhotosAsync()
    {
        if (busy || memory.Images.Length >= 10) return; busy = true;
        try {
            debounce?.Cancel(); await autosave; await PersistAsync();
            var files = await MediaPicker.Default.PickPhotosAsync(JournalMedia.PickerOptions(10 - memory.Images.Length));
            if (files.Count == 0 || !store.IsCurrent(scope)) return;
            memory = await store.AddDraftPhotosAsync(scope, Snapshot(), editor.Text ?? "", files);
            dirty = true; await PersistAsync(); await RenderPhotosAsync();
        } finally { FinishBusy(); }
    }
    private async Task SaveAsync()
    {
        if (busy || closing || !store.IsCurrent(scope)) return;
        busy = true; retrySynchronization.IsEnabled = false;
        editor.IsEnabled = false; title.IsEnabled = false; place.IsEnabled = false; date.IsEnabled = false;
        var committed = false;
        try
        {
            debounce?.Cancel(); await autosave;
            if (memory.HasConflict) { status.Text = JournalText.Get("JournalCompare"); return; }
            if (string.IsNullOrWhiteSpace(editor.Text) && memory.Images.Length == 0)
            { status.Text = JournalText.Get("JournalEmptyValidation"); return; }
            await PersistAsync(); status.Text = JournalText.Get("JournalSaving");
            var snapshot = Snapshot(); var text = editor.Text ?? "";
            var confirmed = await store.SaveConfirmedAsync(scope, snapshot, text, knownConfirmations: confirmations.ToArray());
            committed = true;
            memory = ConfirmedSnapshot(snapshot, confirmed);
            confirmations.Add(memory);
            dirty = hasDraftChanges = false;
            var saved = (await store.LoadAsync(scope, [], false, default)).FirstOrDefault(x => x.Key == memory.Key);
            if (!store.IsCurrent(scope)) return;
            if (saved is not null) MergeSynchronization(saved);
            await store.DiscardDraftAsync(scope, memory);
        }
        catch (OperationCanceledException) { }
        catch (Exception exception)
        {
            ClientDiagnostics.Record(committed ? "journal_editor_post_save_failed" : "journal_editor_save_failed", exception: exception);
            if (visible && store.IsCurrent(scope) && !committed) status.Text = JournalText.Get("JournalDraftFailed");
        }
        finally
        {
            // The local confirmation remains valid even when draft cleanup fails.
            if (committed && store.IsCurrent(scope)) RequestSynchronization();
            editor.IsEnabled = true; title.IsEnabled = memory.IsFree;
            place.IsEnabled = memory.IsFree; date.IsEnabled = memory.IsFree;
            FinishBusy(refresh: !committed);
            if (committed) UpdateSynchronizationStatus();
            if (visible && store.IsCurrent(scope) && committed) await RefreshSynchronizationAsync();
        }
    }
    private static JournalMemory ConfirmedSnapshot(JournalMemory snapshot, JournalMemory confirmed) => snapshot with
    {
        IsDraft = false, Pending = confirmed.Pending, FreePending = confirmed.FreePending,
        Conflict = confirmed.Conflict, FreeConflict = confirmed.FreeConflict,
        Acknowledgement = confirmed.Acknowledgement
    };

    private static int ExpectedRevision(JournalMemory entry) =>
        entry.FreePending?.ExpectedRevision ?? entry.Pending?.ExpectedRevision ?? entry.Revision;

    private static bool SamePayload(JournalMemory left, JournalMemory right) => left.Key == right.Key
        && string.Equals(left.Text.Trim(), right.Text.Trim(), StringComparison.Ordinal)
        && (!left.IsFree || left.Date == right.Date && string.Equals(left.Title.Trim(), right.Title.Trim(), StringComparison.Ordinal)
            && string.Equals(left.City.Trim(), right.City.Trim(), StringComparison.Ordinal));

    private static bool Acknowledges(JournalMemory local, JournalMemory saved)
    {
        if (local.Key != saved.Key || local.HasConflict || local.Deleted) return false;
        var mutation = saved.FreePending?.MutationId ?? saved.Pending?.MutationId;
        var ownAcknowledgement = local.Acknowledgement is { } acknowledgement
            ? mutation is { } id && id != Guid.Empty && acknowledgement.MutationId == id
                && acknowledgement.Revision == local.Revision
            : (long)ExpectedRevision(saved) + 1 == local.Revision;
        if (!ownAcknowledgement) return false;
        var remote = local with { Pending = null, FreePending = null };
        return SamePayload(saved, remote);
    }

    private void MergeSynchronization(JournalMemory local)
    {
        if (local.Key != memory.Key || local.IsDraft) return;
        if (local.HasConflict)
        {
            memory = memory with { Conflict = local.Conflict, FreeConflict = local.FreeConflict };
            return;
        }
        if (memory.HasConflict) return;
        var ownedPending = JournalText.HasPendingChanges(local)
            ? confirmations.LastOrDefault(saved => SamePayload(saved, local)) : null;
        var acknowledged = confirmations.LastOrDefault(saved => Acknowledges(local, saved));
        if (acknowledged is not null)
        {
            // Advance only over an acknowledged payload this editor already knew.
            // Visible fields and current draft photos are deliberately never replaced.
            var clearPending = !JournalText.HasPendingChanges(local)
                || (memory.FreePending?.MutationId ?? memory.Pending?.MutationId)
                    == (acknowledged.FreePending?.MutationId ?? acknowledged.Pending?.MutationId);
            memory = memory with { Acknowledgement = local.Acknowledgement };
            memory = memory.IsFree
                ? memory with { FreeEntry = memory.FreeEntry! with { Revision = local.Revision,
                    UpdatedAt = local.FreeEntry!.UpdatedAt, Title = acknowledged.Title.Trim(), Place = acknowledged.City.Trim(),
                    Date = acknowledged.Date, Notes = acknowledged.Text.Trim() },
                    FreePending = clearPending ? null : memory.FreePending }
                : memory with { Note = memory.Note with { Revision = local.Revision, UpdatedAt = local.Note.UpdatedAt,
                    Notes = acknowledged.Text.Trim() }, Pending = clearPending ? null : memory.Pending };
            confirmations.RemoveAll(saved => ExpectedRevision(saved) < local.Revision);
        }
        if (ownedPending is not null && (ExpectedRevision(ownedPending) == ExpectedRevision(local)
            || acknowledged is not null && ExpectedRevision(local) == local.Revision))
        {
            memory = memory with { Pending = local.Pending, FreePending = local.FreePending };
            confirmations.RemoveAll(saved => ReferenceEquals(saved, ownedPending));
            confirmations.Add(ownedPending with { Pending = local.Pending, FreePending = local.FreePending });
        }
    }

    private void UpdateSynchronizationStatus()
    {
        if (!visible || !store.IsCurrent(scope)) return;
        var state = store.GetSynchronizationState(scope);
        if (memory.HasConflict || !hasDraftChanges && !dirty && !busy)
            status.Text = JournalText.SynchronizationStatus(memory, state);
        retrySynchronization.IsVisible = !hasDraftChanges && JournalText.ShouldRetrySynchronization(memory, state);
        retrySynchronization.IsEnabled = !busy && !closing;
        if (memory.HasConflict && !photos.Children.Contains(conflictAction)) photos.Children.Insert(0, conflictAction);
        if (!memory.HasConflict) photos.Children.Remove(conflictAction);
    }

    private void SynchronizationChanged(object? sender, JournalScope changedScope)
    {
        if (changedScope != scope) return;
        Dispatcher.Dispatch(() => {
            if (visible && initialized && !closing && store.IsCurrent(scope)) _ = RefreshSynchronizationAsync();
        });
    }

    private async Task RefreshSynchronizationAsync()
    {
        if (!visible || !initialized || closing || !store.IsCurrent(scope)) return;
        if (busy) { deferredSynchronizationRefresh = true; return; }
        var refresh = ++synchronizationRefresh;
        try
        {
            var local = (await store.LoadAsync(scope, [], false, default)).FirstOrDefault(x => x.Key == memory.Key);
            if (refresh != synchronizationRefresh || !visible || closing || !store.IsCurrent(scope)) return;
            if (busy) { deferredSynchronizationRefresh = true; return; }
            if (local is not null) MergeSynchronization(local);
            UpdateSynchronizationStatus();
        }
        catch (OperationCanceledException) { }
        catch (Exception exception)
        {
            ClientDiagnostics.Record("journal_editor_sync_reload_failed", exception: exception);
        }
    }

    private void RequestSynchronization()
    {
        if (memory.IsDraft || !store.IsCurrent(scope)) return;
        try { store.RequestSynchronization(scope); }
        catch (OperationCanceledException) { }
        catch (Exception exception) { ClientDiagnostics.Record("journal_editor_sync_request_failed", exception: exception); }
    }

    private void FinishBusy(bool refresh = true)
    {
        busy = false;
        retrySynchronization.IsEnabled = !closing && visible && store.IsCurrent(scope);
        var deferred = deferredSynchronizationRefresh;
        deferredSynchronizationRefresh = false;
        if (refresh && deferred && visible && !closing && store.IsCurrent(scope)) _ = RefreshSynchronizationAsync();
    }

    private async Task RetrySynchronizationAsync()
    {
        if (busy || closing || !visible || hasDraftChanges || !store.IsCurrent(scope)
            || !JournalText.ShouldRetrySynchronization(memory, store.GetSynchronizationState(scope))) return;
        busy = true;
        try
        {
            var local = (await store.LoadAsync(scope, [], false, default)).FirstOrDefault(x => x.Key == memory.Key);
            if (!visible || !store.IsCurrent(scope) || local is null) return;
            MergeSynchronization(local);
            if (JournalText.ShouldRetrySynchronization(local, store.GetSynchronizationState(scope))) RequestSynchronization();
        }
        finally { FinishBusy(); UpdateSynchronizationStatus(); }
    }

    private async Task ResolveAsync()
    {
        if (busy || !store.IsCurrent(scope)) return;
        busy = true;
        editor.IsEnabled = false; title.IsEnabled = false; place.IsEnabled = false; date.IsEnabled = false;
        try
        {
            var useLocal = false;
            if (memory.FreeConflict?.Deleted == true)
            {
                if (!await DisplayAlertAsync(JournalText.Get("JournalDeleted"), JournalText.Get("JournalDeletedConflict"),
                    JournalText.Get("JournalAcceptDeletion"), JournalText.Get("JournalCancel"))) return;
            }
            else
            {
                var keep = JournalText.Get(memory.DeletePending is null ? "JournalKeepMine" : "JournalDelete");
                var remote = JournalText.Get("JournalUseRemote");
                var choice = await DisplayActionSheetAsync($"{memory.Text}\n\n{memory.FreeConflict?.Notes ?? memory.Conflict?.Notes}",
                    JournalText.Get("JournalCancel"), null, keep, remote);
                if (choice != keep && choice != remote) return;
                useLocal = choice == keep;
            }
            if (!store.IsCurrent(scope)) return;
            await store.ResolveAsync(scope, memory, useLocal);
            var entries = await store.LoadAsync(scope, [], false, default);
            if (!store.IsCurrent(scope)) return;
            var resolved = entries.FirstOrDefault(x => x.Key == memory.Key);
            if (resolved is null)
            {
                if (useLocal) RequestSynchronization();
                debounce?.Cancel(); await autosave; await store.DiscardDraftAsync(scope, memory);
                if (!store.IsCurrent(scope)) return;
                dirty = false; closing = true; await Navigation.PopModalAsync(); return;
            }
            var photoDraft = !memory.Images.Select(photo => photo.Id).SequenceEqual(resolved.Images.Select(photo => photo.Id))
                || memory.CoverId != resolved.CoverId;
            memory = resolved with { Photos = memory.Photos, CoverId = memory.CoverId };
            confirmations.Clear();
            if (!memory.IsDraft && (memory.Pending is not null || memory.FreePending is not null))
                confirmations.Add(memory);
            suppress = true;
            try
            {
                editor.Text = memory.Text; title.Text = memory.Title; place.Text = memory.City;
                date.Date = memory.Date.ToDateTime(TimeOnly.MinValue);
            }
            finally { suppress = false; }
            if (JournalText.HasPendingChanges(memory)) RequestSynchronization();
            dirty = true; await PersistAsync();
            hasDraftChanges = photoDraft; // Text is explicitly resolved; local photo edits remain a draft.
            await RenderPhotosAsync();
        }
        finally
        {
            editor.IsEnabled = true; title.IsEnabled = memory.IsFree;
            place.IsEnabled = memory.IsFree; date.IsEnabled = memory.IsFree;
            FinishBusy(refresh: false);
            UpdateSynchronizationStatus();
            if (visible && store.IsCurrent(scope)) await RefreshSynchronizationAsync();
        }
    }
    private async Task DiscardAsync()
    {
        if (busy || !await DisplayAlertAsync(JournalText.Get("JournalDiscardDraft"),
            JournalText.Get(memory.IsDraft ? "JournalDiscardNewQuestion" : "JournalDiscardQuestion"),
            JournalText.Get("JournalDiscard"), JournalText.Get("JournalCancel"))) return;
        debounce?.Cancel(); await autosave; await store.DiscardDraftAsync(scope, memory); dirty = false; closing = true; await Navigation.PopModalAsync();
    }
    private async Task CloseAsync()
    {
        if (closing || busy) return;
        try { debounce?.Cancel(); await autosave; await PersistAsync(); closing = true; await Navigation.PopModalAsync(); }
        catch (Exception) { closing = false; status.Text = JournalText.Get("JournalDraftFailed"); }
    }
    protected override bool OnBackButtonPressed() { _ = CloseAsync(); return true; }
}

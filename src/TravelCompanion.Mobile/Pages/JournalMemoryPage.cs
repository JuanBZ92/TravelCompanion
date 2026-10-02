using TravelCompanion.Mobile.Services;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Mobile.Pages;

internal static class JournalUi
{
    public static readonly Color Ink = Color.FromArgb("#302920");
    public static readonly Color Muted = Color.FromArgb("#71675D");
    public static readonly Color Paper = Color.FromArgb("#F8F3ED");
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
        if (primary) icon.BackgroundColor = Ink;
        return new VerticalStackLayout { Spacing = 3, HorizontalOptions = LayoutOptions.Center,
            MinimumWidthRequest = 72, Children = { icon, new Label { Text = label, FontSize = 11,
                MaxLines = 2, HorizontalTextAlignment = TextAlignment.Center, TextColor = primary ? Ink : Muted } } };
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
    private CancellationTokenSource? debounce;
    private Task autosave = Task.CompletedTask;
    private readonly SemaphoreSlim persistGate = new(1, 1);
    private long editVersion;
    private bool busy, closing, initialized, dirty, suppress;
    private bool pickPhotos;
    private Window? observedWindow;
    private readonly JournalDraft? initialDraft;
    public JournalMemoryPage(JournalScope scope, JournalMemory memory, ScheduleItemDto? item, bool startWithPhotos = false, JournalDraft? draft = null)
    {
        this.scope = scope; this.memory = memory; initialDraft = draft; pickPhotos = startWithPhotos;
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
        var tools = new HorizontalStackLayout { Spacing = 22, HorizontalOptions = LayoutOptions.Center };
        tools.Add(JournalUi.Tool("journal_photo.svg", "JournalAddPhotos", AddPhotosAsync));
        if (memory.IsFree) tools.Add(JournalUi.Tool("journal_search.svg", "JournalFindDayActivity", FindDayActivityAsync));
        tools.Add(JournalUi.Tool("action_saved.svg", "JournalSave", SaveAsync, true));
        var footer = new VerticalStackLayout { Padding = new Thickness(22, 8, 22, 16), Spacing = 8,
            Children = { status, tools } };
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
        finally { busy = false; date.IsEnabled = memory.IsFree; }
    }
    private void Changed()
    {
        UpdateCounter(); if (suppress) return; dirty = true; editVersion++;
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
        observedWindow = Window;
        if (observedWindow is not null) observedWindow.Stopped += AppStopped;
        try {
            if (!initialized) {
                initialized = true;
                var draft = initialDraft ?? (await store.DraftsAsync(scope)).FirstOrDefault(x => x.Memory.Key == memory.Key);
                if (draft is not null) {
                    suppress = true; memory = draft.Memory; editor.Text = draft.Text; title.Text = memory.Title; place.Text = memory.City;
                    date.Date = memory.Date.ToDateTime(TimeOnly.MinValue); suppress = false; status.Text = JournalText.Get("JournalDraftSaved");
                } else status.Text = memory.Status;
            }
            await RenderPhotosAsync();
            if (pickPhotos) { pickPhotos = false; await AddPhotosAsync(); }
        } catch (OperationCanceledException) { } catch (Exception) { status.Text = JournalText.Get("JournalFailure"); }
    }
    protected override async void OnDisappearing()
    {
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
        if (memory.HasConflict) photos.Add(JournalUi.Action("JournalCompare", ResolveAsync));
        photos.Add(JournalUi.Text(JournalText.Format("JournalPhotosLimit", memory.Images.Length), 12));
        var gallery = new FlexLayout { Wrap = Microsoft.Maui.Layouts.FlexWrap.Wrap };
        for (var i = 0; i < memory.Images.Length; i++) {
            var index = i; var photo = memory.Images[i]; var bytes = await store.PhotoAsync(scope, photo.Id, true);
            var tile = new VerticalStackLayout { WidthRequest = 100, Margin = new Thickness(0, 0, 8, 8) };
            var image = JournalUi.Icon("journal_photo.svg", JournalText.Format("JournalPhotoNumber", i + 1, memory.Images.Length),
                () => Navigation.PushModalAsync(new JournalPhotoPage(scope, memory, index)));
            image.WidthRequest = 100; image.HeightRequest = 100; image.Padding = 0; image.Aspect = Aspect.AspectFill;
            if (bytes is not null) image.Source = ImageSource.FromStream(() => new MemoryStream(bytes));
            tile.Add(image);
            tile.Add(JournalUi.Icon(photo.Id == memory.CoverId ? "expense_check.svg" : "journal_cover.svg",
                JournalText.Get(photo.Id == memory.CoverId ? "JournalCover" : "JournalSetCover"), async () => {
                if (busy) return; memory = Snapshot() with { CoverId = photo.Id }; dirty = true; await PersistAsync(); await RenderPhotosAsync(); }));
            tile.Add(JournalUi.Icon("action_delete.svg", JournalText.Get("JournalRemovePhoto"), async () => {
                if (busy || !await DisplayAlertAsync(JournalText.Get("JournalRemovePhoto"), JournalText.Get("JournalOriginalKept"), JournalText.Get("JournalRemove"), JournalText.Get("JournalCancel"))) return;
                var remaining = memory.Images.Where(x => x.Id != photo.Id).ToArray();
                memory = Snapshot() with { Photos = remaining, CoverId = memory.CoverId == photo.Id ? remaining.FirstOrDefault()?.Id : memory.CoverId };
                dirty = true; await PersistAsync(); await RenderPhotosAsync();
            }));
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
        } finally { busy = false; }
    }
    private async Task SaveAsync()
    {
        if (busy) return; busy = true;
        editor.IsEnabled = false; title.IsEnabled = false; place.IsEnabled = false; date.IsEnabled = false;
        var committed = false;
        try {
            debounce?.Cancel(); await autosave;
            if (memory.HasConflict) { status.Text = JournalText.Get("JournalCompare"); return; }
            if (string.IsNullOrWhiteSpace(editor.Text) && memory.Images.Length == 0) { status.Text = JournalText.Get("JournalEmptyValidation"); return; }
            await PersistAsync(); status.Text = JournalText.Get("JournalSaving");
            await store.SaveAsync(scope, Snapshot(), editor.Text ?? "");
            committed = true;
            await store.DiscardDraftAsync(scope, memory); dirty = false;
            memory = Snapshot() with { IsDraft = false };
            status.Text = JournalText.Get("JournalPending");
        } catch (OperationCanceledException) { }
        catch (Exception) { status.Text = JournalText.Get(committed ? "JournalPending" : "JournalDraftFailed"); }
        finally
        {
            editor.IsEnabled = true; title.IsEnabled = memory.IsFree;
            place.IsEnabled = memory.IsFree; date.IsEnabled = memory.IsFree;
            busy = false;
        }
    }
    private async Task ResolveAsync()
    {
        var keep = JournalText.Get(memory.DeletePending is null ? "JournalKeepMine" : "JournalDelete"); var remote = JournalText.Get("JournalUseRemote");
        var choice = await DisplayActionSheetAsync($"{memory.Text}\n\n{memory.FreeConflict?.Notes ?? memory.Conflict?.Notes}", JournalText.Get("JournalCancel"), null, keep, remote);
        if (choice != keep && choice != remote) return;
        await store.ResolveAsync(scope, memory, choice == keep);
        var entries = await store.LoadAsync(scope, [], false, default);
        var resolved = entries.FirstOrDefault(x => x.Key == memory.Key);
        if (resolved is null)
        {
            debounce?.Cancel(); await autosave; await store.DiscardDraftAsync(scope, memory);
            dirty = false; closing = true; await Navigation.PopModalAsync(); return;
        }
        memory = resolved;
        suppress = true; editor.Text = memory.Text; title.Text = memory.Title; place.Text = memory.City; date.Date = memory.Date.ToDateTime(TimeOnly.MinValue); suppress = false;
        dirty = true; await PersistAsync(); await RenderPhotosAsync();
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

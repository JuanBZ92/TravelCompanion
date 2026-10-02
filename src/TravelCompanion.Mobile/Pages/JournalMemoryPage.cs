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
}

public sealed class JournalActivityPickerPage : JournalScopedPage
{
    public JournalActivityPickerPage(IReadOnlyList<ScheduleItemDto> activities, IReadOnlyList<JournalMemory> memories)
    {
        BackgroundColor = JournalUi.Paper;
        var store = MauiProgram.Services.GetRequiredService<JournalStore>(); var scope = store.Scope();
        var search = new SearchBar { Placeholder = JournalText.Get("JournalSearchActivity") };
        var list = new CollectionView { SelectionMode = SelectionMode.Single, ItemsSource = activities.OrderBy(x => x.Date).ThenBy(x => x.StartsAt).ToArray() };
        list.ItemTemplate = new DataTemplate(() => {
            var title = JournalUi.Text("", 19, true); title.SetBinding(Label.TextProperty, "Title");
            var date = JournalUi.Text("", 12); date.SetBinding(Label.TextProperty, new Binding("Date", stringFormat: "{0:d MMMM}"));
            return new VerticalStackLayout { Padding = new Thickness(0, 12), Spacing = 6, MinimumHeightRequest = 48, Children = { date, title } }; });
        search.TextChanged += (_, e) => list.ItemsSource = activities.Where(x => $"{x.Title} {x.City}".Contains(e.NewTextValue ?? "", StringComparison.CurrentCultureIgnoreCase)).OrderBy(x => x.Date).ToArray();
        var opening = false;
        list.SelectionChanged += async (_, e) => {
            if (opening || e.CurrentSelection.FirstOrDefault() is not ScheduleItemDto item) return;
            list.SelectedItem = null; if (!store.IsCurrent(scope)) return;
            opening = true;
            try {
                var memory = memories.FirstOrDefault(x => !x.IsFree && x.Id == item.Id)
                    ?? new JournalMemory(new(item.Id, scope.TripId, item.Title, item.City, item.Date, "", 0, DateTimeOffset.UtcNow), IsDraft: true);
                await Navigation.PushModalAsync(new JournalMemoryPage(scope, memory, item));
            } finally { opening = false; }
        };
        var header = new Grid { ColumnDefinitions = [new(GridLength.Star), new(GridLength.Auto)] };
        header.Add(JournalUi.Text(JournalText.Get("JournalChooseActivity"), 26, true));
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
        SemanticProperties.SetDescription(title, JournalText.Get("JournalTitleOptional"));
        SemanticProperties.SetDescription(place, JournalText.Get("JournalPlaceOptional"));
        SemanticProperties.SetDescription(date, JournalText.Get("JournalDate"));
        SemanticProperties.SetDescription(editor, JournalText.Get("JournalYourText"));
        var header = new Grid { Padding = new Thickness(22, 8), ColumnDefinitions = [new(GridLength.Star), new(GridLength.Auto)] };
        header.Add(JournalUi.Text(JournalText.Get("JournalWrite"), 22, true));
        header.Add(JournalUi.Icon("action_close.svg", JournalText.Get("JournalClose"), CloseAsync), 1);
        var body = new VerticalStackLayout { Padding = 22, Spacing = 16, Children = { date, title, place } };
        if (!memory.IsFree) body.Add(JournalUi.Text(JournalText.Title(memory), 28, true));
        body.Add(new Border { BackgroundColor = Color.FromArgb("#FFFCF8"), Stroke = Color.FromArgb("#E5DDD3"), Padding = 16, Content = editor });
        body.Add(counter); body.Add(photos);
        body.Add(JournalUi.Text(JournalText.Get("JournalStorageNotice"), 12));
        body.Add(JournalUi.Action("JournalDiscardDraft", DiscardAsync));
        var footer = new VerticalStackLayout { Padding = new Thickness(22, 8, 22, 16), Spacing = 6,
            Children = { status, JournalUi.Action("JournalSave", SaveAsync, true) } };
        var grid = new Grid { RowDefinitions = [new(GridLength.Auto), new(GridLength.Star), new(GridLength.Auto)] };
        grid.Add(header); grid.Add(new ScrollView { Content = body }, 0, 1); grid.Add(footer, 0, 2); Content = grid;
        editor.TextChanged += (_, _) => Changed(); title.TextChanged += (_, _) => Changed(); place.TextChanged += (_, _) => Changed();
        date.DateSelected += (_, _) => Changed(); UpdateCounter();
    }
    private void UpdateCounter() => counter.Text = JournalText.Format("JournalCharacterCount", editor.Text?.Length ?? 0);
    private void Changed()
    {
        UpdateCounter(); if (suppress) return; dirty = true;
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
        status.Text = JournalText.Get("JournalSaving");
        var snapshot = Snapshot(); var text = editor.Text ?? "";
        await store.SaveDraftAsync(scope, snapshot, text, ct);
        ct.ThrowIfCancellationRequested();
        status.Text = JournalText.Get("JournalDraftSaved");
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
                    date.Date = memory.Date.ToDateTime(TimeOnly.MinValue); suppress = false; dirty = true; status.Text = JournalText.Get("JournalDraftSaved");
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
        photos.Add(JournalUi.Action("JournalAddPhotos", AddPhotosAsync));
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
        var committed = false;
        try {
            debounce?.Cancel(); await autosave;
            if (memory.HasConflict) { status.Text = JournalText.Get("JournalCompare"); return; }
            if (string.IsNullOrWhiteSpace(editor.Text) && memory.Images.Length == 0) { status.Text = JournalText.Get("JournalEmptyValidation"); return; }
            await PersistAsync(); status.Text = JournalText.Get("JournalSaving");
            await store.SaveAsync(scope, Snapshot(), editor.Text ?? "");
            committed = true;
            await store.DiscardDraftAsync(scope, memory); dirty = false;
            var entries = await store.LoadAsync(scope, [], Connectivity.Current.NetworkAccess == NetworkAccess.Internet, default);
            memory = entries.FirstOrDefault(x => x.Key == memory.Key) ?? memory;
            status.Text = memory.Status.Length > 0 ? memory.Status : JournalText.Get("JournalSaved");
            await RenderPhotosAsync();
        } catch (OperationCanceledException) { }
        catch (Exception) { status.Text = JournalText.Get(committed ? "JournalPending" : "JournalDraftFailed"); }
        finally { busy = false; }
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
        if (busy || !await DisplayAlertAsync(JournalText.Get("JournalDiscardDraft"), JournalText.Get("JournalDiscardQuestion"), JournalText.Get("JournalDiscard"), JournalText.Get("JournalCancel"))) return;
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

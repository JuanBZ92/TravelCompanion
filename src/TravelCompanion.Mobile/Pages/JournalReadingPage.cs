using TravelCompanion.Mobile.Services;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Mobile.Pages;

public sealed class JournalReadingPage(JournalScope scope, JournalMemory original, ScheduleItemDto? item) : JournalScopedPage
{
    private readonly JournalStore store = MauiProgram.Services.GetRequiredService<JournalStore>();
    private JournalMemory memory = original;
    private Label photoStatus = JournalUi.Text("", 12);
    private Grid? actions;
    private int loadVersion;
    private bool visible;
    private bool addingPhotos;
    private bool opening;

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        visible = true;
        await LoadAsync();
    }

    protected override void OnDisappearing()
    {
        visible = false;
        loadVersion++;
        base.OnDisappearing();
    }

    private async Task LoadAsync()
    {
        var version = ++loadVersion;
        BackgroundColor = JournalUi.Paper;
        SafeAreaEdges = SafeAreaEdges.All;
        try
        {
            var memories = await store.LoadAsync(scope, [], false, default);
            if (!CanDisplay(version)) return;
            var current = memories.FirstOrDefault(x => x.Key == memory.Key);
            if (current is null && memory.IsFree && !memory.IsDraft)
            {
                if (Navigation.ModalStack.LastOrDefault() == this) await Navigation.PopModalAsync();
                return;
            }
            memory = current ?? memory;
            if (!CanDisplay(version)) return;

            var header = new Grid { Padding = new Thickness(22, 8, 18, 8),
                ColumnDefinitions = [new(GridLength.Star), new(GridLength.Auto), new(GridLength.Auto)] };
            var heading = new VerticalStackLayout { Spacing = 2 };
            var eyebrow = JournalUi.Text(JournalText.Get("JournalMemoryLabel"), 12);
            eyebrow.TextColor = Color.FromArgb("#765831");
            heading.Add(eyebrow);
            heading.Add(JournalUi.Text(memory.Date.ToString("d MMMM yyyy"), 14));
            header.Add(heading);
            if (memory.IsFree) header.Add(JournalUi.Icon("action_delete.svg", JournalText.Get("JournalDelete"), DeleteAsync), 1);
            header.Add(JournalUi.Icon("action_close.svg", JournalText.Get("JournalClose"),
                () => Navigation.PopModalAsync()), 2);

            var body = new VerticalStackLayout { Padding = new Thickness(22, 10, 22, 28), Spacing = 18 };
            var title = JournalUi.Text(JournalText.DisplayTitle(memory), 31, true);
            SemanticProperties.SetHeadingLevel(title, SemanticHeadingLevel.Level1);
            body.Add(title);
            if (!string.IsNullOrWhiteSpace(memory.Title) && !string.IsNullOrWhiteSpace(memory.City)
                && !string.Equals(memory.Title.Trim(), memory.City.Trim(), StringComparison.CurrentCultureIgnoreCase))
            {
                var place = JournalUi.Text(memory.City, 15);
                place.TextColor = Color.FromArgb("#765831");
                body.Add(new Border { BackgroundColor = Color.FromArgb("#F0E8DE"),
                    StrokeThickness = 0, StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = new CornerRadius(12) },
                    Padding = new Thickness(12, 7), HorizontalOptions = LayoutOptions.Start, Content = place });
            }

            if (!string.IsNullOrWhiteSpace(memory.Text))
            {
                var prose = JournalUi.Text(memory.Text, 18);
                prose.TextColor = JournalUi.Ink;
                prose.LineHeight = 1.45;
                var paper = new Grid { ColumnDefinitions = [new(new GridLength(3)), new(GridLength.Star)],
                    ColumnSpacing = 17 };
                paper.Add(new BoxView { Color = Color.FromArgb("#D6C5AE"), WidthRequest = 3 });
                paper.Add(prose, 1);
                body.Add(new Border { BackgroundColor = Color.FromArgb("#FFFCF8"),
                    Stroke = Color.FromArgb("#E5DDD3"),
                    StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = new CornerRadius(12) },
                    Padding = 20, Content = paper });
            }
            if (memory.Images.Length > 0)
            {
                body.Add(JournalUi.Text(JournalText.Format("JournalPhotosLimit", memory.Images.Length), 12));
                var indices = JournalEntries.PhotoPreviewIndices(memory);
                var gallery = new FlexLayout { Wrap = Microsoft.Maui.Layouts.FlexWrap.Wrap,
                    AlignItems = Microsoft.Maui.Layouts.FlexAlignItems.Start,
                    AlignContent = Microsoft.Maui.Layouts.FlexAlignContent.Start };
                foreach (var index in indices)
                {
                    var bytes = await store.PhotoAsync(scope, memory.Images[index].Id, true);
                    if (!CanDisplay(version)) return;
                    var image = JournalUi.Icon("journal_photo.svg",
                        $"{JournalText.Get("JournalOpenPhotos")} · {JournalText.Format("JournalPhotoNumber", index + 1, memory.Images.Length)}",
                        () => OpenPhotosAsync(index));
                    image.WidthRequest = 68; image.HeightRequest = 68; image.Padding = 0; image.Aspect = Aspect.AspectFill;
                    if (bytes is not null) image.Source = ImageSource.FromStream(() => new MemoryStream(bytes));
                    var tile = new Border { WidthRequest = 68, HeightRequest = 68, Padding = 0,
                        Margin = new Thickness(0, 0, 8, 8), StrokeThickness = 0,
                        StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = new CornerRadius(10) },
                        Content = image };
                    FlexLayout.SetAlignSelf(tile, Microsoft.Maui.Layouts.FlexAlignSelf.Start);
                    gallery.Children.Add(tile);
                }
                var remaining = memory.Images.Length - indices.Count;
                if (remaining > 0)
                {
                    var firstHiddenIndex = JournalEntries.PhotoPreviewIndices(memory, 4)[3];
                    var more = EditorialUi.Button($"+{remaining}", () => OpenPhotosAsync(firstHiddenIndex));
                    more.WidthRequest = 68; more.HeightRequest = 68; more.Padding = 0; more.FontSize = 20;
                    more.CornerRadius = 10; more.BackgroundColor = Color.FromArgb("#F0E8DE");
                    more.Margin = new Thickness(0, 0, 8, 8);
                    SemanticProperties.SetDescription(more, JournalText.Format(remaining == 1 ? "JournalMorePhoto" : "JournalMorePhotos", remaining));
                    FlexLayout.SetAlignSelf(more, Microsoft.Maui.Layouts.FlexAlignSelf.Start);
                    gallery.Children.Add(more);
                }
                body.Add(gallery);
            }
            if (memory.Status.Length > 0)
            {
                var status = JournalUi.Text(memory.Status, 13);
                status.TextColor = Color.FromArgb("#765831");
                body.Add(status);
            }
            var notice = JournalUi.Text(JournalText.Get("JournalStorageNotice"), 12);
            notice.LineHeight = 1.3;
            body.Add(notice);

            actions = new Grid { Padding = new Thickness(22, 10, 22, 16), IsEnabled = !addingPhotos,
                ColumnDefinitions = item is null
                    ? [new(GridLength.Star), new(GridLength.Star)]
                    : [new(GridLength.Star), new(GridLength.Star), new(GridLength.Star)] };
            actions.Add(JournalUi.Tool("action_edit.svg", "JournalEdit",
                EditAsync), 0);
            actions.Add(JournalUi.Tool("journal_photo.svg", "JournalAddPhotos",
                AddPhotosAsync), 1);
            if (item is not null) actions.Add(JournalUi.Tool("tab_trip.svg", "JournalActivity", OpenActivityAsync), 2);

            var layout = new Grid { RowDefinitions = [new(GridLength.Auto),
                new(GridLength.Star), new(GridLength.Auto)] };
            layout.Add(header);
            layout.Add(new ScrollView { Content = body }, 0, 1);
            photoStatus = JournalUi.Text(photoStatus.Text ?? "", 12);
            photoStatus.Margin = new Thickness(22, 0);
            photoStatus.IsVisible = photoStatus.Text.Length > 0;
            layout.Add(new VerticalStackLayout { Children = { photoStatus, actions } }, 0, 2);
            if (!CanDisplay(version)) return;
            Content = layout;
        }
        catch (OperationCanceledException) { }
        catch (Exception exception)
        {
            ClientDiagnostics.Record("journal_read_failed", exception: exception);
            if (!CanDisplay(version)) return;
            var header = new Grid { ColumnDefinitions = [new(GridLength.Star), new(GridLength.Auto)] };
            header.Add(EditorialUi.Heading(JournalText.Get("JournalMemoryLabel")));
            header.Add(EditorialUi.Icon("action_close.svg", JournalText.Get("JournalClose"),
                () => Navigation.PopModalAsync()), 1);
            Content = new ScrollView { Content = new VerticalStackLayout
            {
                Padding = 24, Spacing = 18, Children =
                {
                    header, EditorialUi.Text(EditorialUi.TextResource("UxMemoryRetry")),
                    EditorialUi.Button(EditorialUi.TextResource("UxRetry"), LoadAsync, true)
                }
            } };
        }
    }

    private bool CanDisplay(int version) => visible && version == loadVersion && store.IsCurrent(scope);

    private void SetPhotoStatus(string text)
    {
        photoStatus.Text = text;
        photoStatus.IsVisible = text.Length > 0;
    }

    private async Task OpenPhotosAsync(int index)
    {
        if (opening || addingPhotos || !visible || !store.IsCurrent(scope)) return;
        opening = true;
        try { await Navigation.PushModalAsync(new JournalPhotoPage(scope, memory, index)); }
        finally { opening = false; }
    }

    private async Task EditAsync()
    {
        if (opening || addingPhotos || !visible || !store.IsCurrent(scope)) return;
        opening = true;
        try { await Navigation.PushModalAsync(new JournalMemoryPage(scope, memory, item)); }
        finally { opening = false; }
    }

    private async Task AddPhotosAsync()
    {
        if (addingPhotos || opening || !visible || !store.IsCurrent(scope)) return;
        if (memory.Images.Length >= 10)
        {
            SetPhotoStatus(JournalText.Format("JournalPhotosLimit", memory.Images.Length));
            return;
        }
        addingPhotos = true;
        if (actions is not null) actions.IsEnabled = false;
        SetPhotoStatus("");
        try
        {
            var draft = (await store.DraftsAsync(scope)).FirstOrDefault(x => x.Memory.Key == memory.Key);
            if (!store.IsCurrent(scope)) return;
            var remaining = 10 - memory.Images.Length;
            if (draft is not null)
            {
                remaining = Math.Min(remaining, 10 - draft.Memory.Images.Length);
                if (remaining <= 0)
                {
                    SetPhotoStatus(JournalText.Get("JournalDraftPhotoLimit"));
                    return;
                }
            }
            var files = await MediaPicker.Default.PickPhotosAsync(JournalMedia.PickerOptions(remaining));
            if (files.Count == 0 || !store.IsCurrent(scope)) return;
            SetPhotoStatus(JournalText.Get("JournalAddingPhotos"));
            memory = await store.AddConfirmedPhotosAsync(scope, memory, files);
            if (!store.IsCurrent(scope)) return;
            SetPhotoStatus("");
            if (visible) await LoadAsync();
        }
        catch (OperationCanceledException) { }
        catch (Exception exception)
        {
            ClientDiagnostics.Record("journal_add_photos_failed", exception: exception);
            if (visible && store.IsCurrent(scope)) SetPhotoStatus(JournalText.Get("JournalFailure"));
        }
        finally
        {
            addingPhotos = false;
            if (actions is not null) actions.IsEnabled = true;
        }
    }

    private async Task OpenActivityAsync()
    {
        if (!store.IsCurrent(scope) || item is null) return;
        await Navigation.PopModalAsync();
        await Shell.Current.GoToAsync(nameof(ScheduleItemDetailPage),
            new Dictionary<string, object> { ["ScheduleItem"] = item });
    }

    private async Task DeleteAsync()
    {
        if (!memory.IsFree || !store.IsCurrent(scope)
            || !await DisplayAlertAsync(JournalText.Get("JournalDelete"), JournalText.Get("JournalDeleteQuestion"),
                JournalText.Get("JournalRemove"), JournalText.Get("JournalCancel"))) return;
        await store.DeleteFreeLocalAsync(scope, memory);
        await store.DiscardDraftAsync(scope, memory);
        await Navigation.PopModalAsync();
    }
}

using TravelCompanion.Mobile.Services;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Mobile.Pages;

public sealed class JournalReadingPage(JournalScope scope, JournalMemory original, ScheduleItemDto? item) : JournalScopedPage
{
    private readonly JournalStore store = MauiProgram.Services.GetRequiredService<JournalStore>();
    private JournalMemory memory = original;

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        BackgroundColor = JournalUi.Paper;
        SafeAreaEdges = SafeAreaEdges.All;
        try
        {
            var memories = await store.LoadAsync(scope, [], false, default);
            var current = memories.FirstOrDefault(x => x.Key == memory.Key);
            if (current is null && memory.IsFree && !memory.IsDraft)
            {
                await Navigation.PopModalAsync();
                return;
            }
            memory = current ?? memory;
            if (!store.IsCurrent(scope)) return;

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

            if (memory.Images.Length > 0)
            {
                var index = Math.Max(0, Array.FindIndex(memory.Images, x => x.Id == memory.CoverId));
                var bytes = await store.PhotoAsync(scope, memory.Images[index].Id, true);
                if (!store.IsCurrent(scope)) return;
                var cover = JournalUi.Icon("journal_photo.svg", JournalText.Get("JournalOpenPhotos"),
                    () => Navigation.PushModalAsync(new JournalPhotoPage(scope, memory, index)));
                cover.WidthRequest = -1; cover.HeightRequest = 240; cover.Padding = 0; cover.Aspect = Aspect.AspectFill;
                if (bytes is not null) cover.Source = ImageSource.FromStream(() => new MemoryStream(bytes));
                body.Add(new Border { Stroke = Color.FromArgb("#E5DDD3"),
                    StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = new CornerRadius(12) },
                    Padding = 0, Content = cover });
                body.Add(JournalUi.Text(JournalText.Format("JournalPhotoCount", memory.Images.Length), 12));
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
            if (memory.Status.Length > 0)
            {
                var status = JournalUi.Text(memory.Status, 13);
                status.TextColor = Color.FromArgb("#765831");
                body.Add(status);
            }
            var notice = JournalUi.Text(JournalText.Get("JournalStorageNotice"), 12);
            notice.LineHeight = 1.3;
            body.Add(notice);

            var actions = new Grid { Padding = new Thickness(22, 10, 22, 16),
                ColumnDefinitions = item is null
                    ? [new(GridLength.Star), new(GridLength.Star)]
                    : [new(GridLength.Star), new(GridLength.Star), new(GridLength.Star)] };
            actions.Add(JournalUi.Tool("action_edit.svg", "JournalEdit",
                () => Navigation.PushModalAsync(new JournalMemoryPage(scope, memory, item))), 0);
            actions.Add(JournalUi.Tool("journal_photo.svg", "JournalAddPhotos",
                () => Navigation.PushModalAsync(new JournalMemoryPage(scope, memory, item, true))), 1);
            if (item is not null) actions.Add(JournalUi.Tool("tab_trip.svg", "JournalActivity", OpenActivityAsync), 2);

            var layout = new Grid { RowDefinitions = [new(GridLength.Auto),
                new(GridLength.Star), new(GridLength.Auto)] };
            layout.Add(header);
            layout.Add(new ScrollView { Content = body }, 0, 1);
            layout.Add(actions, 0, 2);
            Content = layout;
        }
        catch (OperationCanceledException) { }
        catch (Exception) { Content = JournalUi.Text(JournalText.Get("JournalFailure")); }
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

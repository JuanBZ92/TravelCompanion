using TravelCompanion.Mobile.Services;

namespace TravelCompanion.Mobile.Pages;

public sealed class JournalPhotoPage : JournalScopedPage
{
    private readonly JournalStore store = MauiProgram.Services.GetRequiredService<JournalStore>();
    private readonly JournalScope scope;
    private readonly JournalMemory memory;
    private readonly Image image = new() { Aspect = Aspect.AspectFit };
    private readonly Label counter = JournalUi.Text("", 14);
    private readonly Label message = JournalUi.Text("", 15);
    private readonly ImageButton previous;
    private readonly ImageButton next;
    private int position;
    private int loadVersion;
    private bool visible;
    private bool closing;

    public JournalPhotoPage(JournalScope scope, JournalMemory memory, int position)
    {
        this.scope = scope;
        this.memory = memory;
        this.position = Math.Clamp(position, 0, Math.Max(0, memory.Images.Length - 1));
        BackgroundColor = JournalUi.Paper;
        var title = JournalUi.Text(JournalText.Title(memory), 22, true);
        title.MaxLines = 2;
        title.LineBreakMode = LineBreakMode.TailTruncation;
        var header = new Grid { ColumnDefinitions = [new(GridLength.Star), new(GridLength.Auto)] };
        header.Add(title);
        header.Add(JournalUi.Icon("action_close.svg", JournalText.Get("JournalClose"), CloseAsync), 1);
        previous = JournalUi.Icon("today_arrow_dark.svg", JournalText.Get("JournalPreviousPhoto"), () => MoveAsync(-1));
        previous.Rotation = 180;
        next = JournalUi.Icon("today_arrow_dark.svg", JournalText.Get("JournalNextPhoto"), () => MoveAsync(1));
        counter.HorizontalTextAlignment = TextAlignment.Center;
        counter.VerticalOptions = LayoutOptions.Center;
        var controls = new Grid { ColumnDefinitions = [new(GridLength.Auto), new(GridLength.Star), new(GridLength.Auto)] };
        controls.Add(previous); controls.Add(counter, 1); controls.Add(next, 2);
        var stage = new Grid();
        stage.Add(image);
        message.HorizontalTextAlignment = TextAlignment.Center;
        message.VerticalOptions = LayoutOptions.Center;
        stage.Add(message);
        var left = new SwipeGestureRecognizer { Direction = SwipeDirection.Left };
        left.Swiped += async (_, _) => await MoveAsync(1);
        var right = new SwipeGestureRecognizer { Direction = SwipeDirection.Right };
        right.Swiped += async (_, _) => await MoveAsync(-1);
        stage.GestureRecognizers.Add(left); stage.GestureRecognizers.Add(right);
        var grid = new Grid { Padding = 24, RowSpacing = 16,
            RowDefinitions = [new(GridLength.Auto), new(GridLength.Star), new(GridLength.Auto)] };
        grid.Add(header); grid.Add(stage, 0, 1); grid.Add(controls, 0, 2);
        Content = grid;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        visible = true;
        await ShowAsync();
    }

    protected override void OnDisappearing()
    {
        visible = false;
        loadVersion++;
        image.Source = null;
        base.OnDisappearing();
    }

    private Task MoveAsync(int delta)
    {
        var target = position + delta;
        if (target < 0 || target >= memory.Images.Length) return Task.CompletedTask;
        position = target;
        return ShowAsync();
    }

    private async Task ShowAsync()
    {
        var version = ++loadVersion;
        image.Source = null;
        previous.IsEnabled = position > 0;
        next.IsEnabled = position + 1 < memory.Images.Length;
        counter.Text = memory.Images.Length == 0 ? JournalText.Get("JournalNoPhoto") : $"{position + 1} / {memory.Images.Length}";
        SemanticProperties.SetDescription(image, JournalText.Format("JournalPhotoNumber", position + 1, memory.Images.Length));
        message.Text = JournalText.Get("JournalLoadingPhoto");
        try
        {
            var bytes = memory.Images.Length == 0 ? null : await store.PhotoAsync(scope, memory.Images[position].Id);
            if (!visible || version != loadVersion || !store.IsCurrent(scope)) return;
            image.Source = bytes is null ? null : ImageSource.FromStream(() => new MemoryStream(bytes));
            message.Text = bytes is null ? JournalText.Get("JournalPhotoMissing") : "";
        }
        catch (Exception)
        {
            if (visible && version == loadVersion && store.IsCurrent(scope))
                message.Text = JournalText.Get("JournalPhotoFailed");
        }
    }

    private async Task CloseAsync()
    {
        if (closing) return;
        closing = true;
        try { await Navigation.PopModalAsync(); }
        finally { closing = false; }
    }
    protected override bool OnBackButtonPressed() { _ = CloseAsync(); return true; }
}

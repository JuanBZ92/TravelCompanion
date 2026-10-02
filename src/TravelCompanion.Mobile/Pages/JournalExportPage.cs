using TravelCompanion.Mobile.Services;

namespace TravelCompanion.Mobile.Pages;

public sealed class JournalExportPage : JournalScopedPage
{
    private readonly JournalStore store = MauiProgram.Services.GetRequiredService<JournalStore>();
    private readonly JournalScope scope;
    private readonly IReadOnlyList<JournalMemory> entries;
    private readonly HashSet<DateOnly> days;
    private readonly Entry title;
    private readonly Label status = JournalUi.Text("", 13);
    private readonly ProgressBar progress = new();
    private CancellationTokenSource? exporting;
    private Guid? cover;
    private bool coverChosen;

    public JournalExportPage(JournalScope scope, string tripName, IReadOnlyList<JournalMemory> entries)
    {
        this.scope = scope;
        this.entries = entries = entries.Where(x => !x.IsDraft && !x.Deleted && x.HasContent).OrderBy(x => x.Date).ToArray();
        days = entries.Select(x => x.Date).ToHashSet();
        cover = entries.Select(x => x.CoverId).FirstOrDefault(x => x.HasValue) ?? entries.SelectMany(x => x.Images).FirstOrDefault()?.Id;
        BackgroundColor = JournalUi.Paper;
        title = new Entry { Text = tripName, Placeholder = JournalText.Get("JournalAlbumTitle"), MaxLength = 100 };
        var body = new VerticalStackLayout { Padding = 24, Spacing = 16 };
        var header = new Grid { ColumnDefinitions = [new(GridLength.Star), new(GridLength.Auto)] };
        header.Add(JournalUi.Text(JournalText.Get("JournalAlbum"), 30, true));
        header.Add(JournalUi.Icon("action_close.svg", JournalText.Get("JournalClose"), async () => { exporting?.Cancel(); await Navigation.PopModalAsync(); }), 1);
        body.Add(header); body.Add(JournalUi.Text(JournalText.Get("JournalAlbumIntro"))); body.Add(title);
        foreach (var group in entries.GroupBy(x => x.Date))
        {
            var check = new CheckBox { IsChecked = true, MinimumHeightRequest = 48, MinimumWidthRequest = 48 };
            check.CheckedChanged += (_, e) => { if (e.Value) days.Add(group.Key); else days.Remove(group.Key); };
            SemanticProperties.SetDescription(check, JournalText.Format("JournalIncludeDay", group.Key.ToString("d MMMM")));
            var row = new Grid { ColumnDefinitions = [new(GridLength.Auto), new(GridLength.Star)], ColumnSpacing = 12 };
            row.Add(check); row.Add(JournalUi.Text($"{group.Key:d MMMM} · {JournalText.Memories(group.Count())}"), 1); body.Add(row);
        }
        body.Add(JournalUi.Text(JournalText.Get("JournalCover"), 22, true));
        var coverPicker = new Picker { Title = JournalText.Get("JournalCoverPhoto") };
        var choices = entries.SelectMany(x => x.Images.Select((p, i) => (p.Id, Label: JournalText.Format("JournalPhotoChoice", JournalText.Title(x), i + 1)))).ToArray();
        coverPicker.Items.Add(JournalText.Get("JournalNoPhoto"));
        foreach (var choice in choices) coverPicker.Items.Add(choice.Label);
        coverPicker.SelectedIndex = cover.HasValue ? Array.FindIndex(choices, x => x.Id == cover) + 1 : 0;
        coverPicker.SelectedIndexChanged += (_, _) =>
        {
            coverChosen = true;
            cover = coverPicker.SelectedIndex <= 0 ? null : choices[coverPicker.SelectedIndex - 1].Id;
        };
        body.Add(coverPicker);
        var preview = new Button { Text = JournalText.Get("JournalPreview"), CornerRadius = 8, MinimumHeightRequest = 48 };
        preview.Clicked += async (_, _) => await ExportAsync("preview"); body.Add(preview);
        var save = new Button { Text = JournalText.Get("JournalSavePdf"), CornerRadius = 8, MinimumHeightRequest = 48 };
        save.Clicked += async (_, _) => await ExportAsync("save"); body.Add(save);
        var share = new Button { Text = JournalText.Get("JournalShare"), MinimumHeightRequest = 48, BackgroundColor = Colors.Transparent, TextColor = JournalUi.Ink };
        share.Clicked += async (_, _) => await ExportAsync("share"); body.Add(share);
        var cancel = new Button { Text = JournalText.Get("JournalCancelExport"), MinimumHeightRequest = 48, BackgroundColor = Colors.Transparent, TextColor = JournalUi.Ink };
        cancel.Clicked += (_, _) => exporting?.Cancel(); body.Add(cancel);
        body.Add(progress); body.Add(status); Content = new ScrollView { Content = body };
    }

    private async Task ExportAsync(string action)
    {
        if (exporting is not null) return;
        var selected = entries.Where(x => days.Contains(x.Date)).ToArray();
        if (selected.Length == 0) { status.Text = JournalText.Get("JournalChooseDay"); return; }
        exporting = new CancellationTokenSource();
        try
        {
            var missing = 0;
            Guid? firstAvailable = null;
            foreach (var photo in selected.SelectMany(x => x.Images))
            {
                if (await store.PhotoAsync(scope, photo.Id, true) is null) missing++;
                else firstAvailable ??= photo.Id;
            }
            if (!coverChosen && (!cover.HasValue || await store.PhotoAsync(scope, cover.Value, true) is null)) cover = firstAvailable;
            if (missing > 0 && !await DisplayAlertAsync(JournalText.Get("JournalMissingPhotos"), JournalText.Format("JournalMissingQuestion", missing), JournalText.Get("JournalContinue"), JournalText.Get("JournalCancel"))) return;
            status.Text = JournalText.Get("JournalBuilding");
            var selectedCover = selected.SelectMany(x => x.Images).Any(x => x.Id == cover) ? cover : null;
            var file = await JournalPdfExporter.CreateAsync(store, scope, string.IsNullOrWhiteSpace(title.Text) ? JournalText.Get("JournalMyTrip") : title.Text.Trim(),
                selected, selectedCover, new Progress<double>(value => progress.Progress = value), exporting.Token);
            if (!store.IsCurrent(scope)) return;
            if (action == "preview")
            {
                if (!await Launcher.Default.OpenAsync(new OpenFileRequest(JournalText.Get("JournalHeading"), new ReadOnlyFile(file))))
                    status.Text = JournalText.Get("JournalNoViewer");
                else status.Text = JournalText.Get("JournalPreviewReady");
            }
            else if (action == "share")
            {
                await Share.Default.RequestAsync(new ShareFileRequest(JournalText.Get("JournalHeading"), new ShareFile(file))); status.Text = JournalText.Get("JournalShareReady");
            }
            else
            {
#if ANDROID
                var saved = await Platforms.Android.JournalFileSaver.SaveAsync(file);
                status.Text = saved ? JournalText.Get("JournalPdfSaved") : JournalText.Get("JournalSaveCancelled");
#endif
            }
        }
        catch (OperationCanceledException) { status.Text = JournalText.Get("JournalExportCancelled"); }
        catch (Exception) { status.Text = JournalText.Get("JournalExportFailed"); }
        finally { exporting.Dispose(); exporting = null; }
    }
    protected override bool OnBackButtonPressed()
    {
        exporting?.Cancel(); _ = Navigation.PopModalAsync(); return true;
    }
}

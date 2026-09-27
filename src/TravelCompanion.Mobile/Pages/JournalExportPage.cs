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
        this.scope = scope; this.entries = entries; days = entries.Select(x => x.Note.Date).ToHashSet();
        cover = entries.SelectMany(x => x.Images).FirstOrDefault()?.Id;
        BackgroundColor = JournalUi.Paper;
        title = new Entry { Text = tripName, Placeholder = "Título de tu álbum", MaxLength = 100 };
        var body = new VerticalStackLayout { Padding = 24, Spacing = 16 };
        var header = new Grid { ColumnDefinitions = [new(GridLength.Star), new(GridLength.Auto)] };
        header.Add(JournalUi.Text("Tu álbum de viaje", 30, true));
        header.Add(JournalUi.Icon("action_close.svg", "Cerrar exportación", async () => { exporting?.Cancel(); await Navigation.PopModalAsync(); }), 1);
        body.Add(header); body.Add(JournalUi.Text("Un PDF con tus lugares, notas y fotos. Elegí los días que querés conservar.")); body.Add(title);
        foreach (var group in entries.GroupBy(x => x.Note.Date))
        {
            var check = new CheckBox { IsChecked = true };
            check.CheckedChanged += (_, e) => { if (e.Value) days.Add(group.Key); else days.Remove(group.Key); };
            SemanticProperties.SetDescription(check, $"Incluir {group.Key:d MMMM}");
            var row = new HorizontalStackLayout { Spacing = 12 };
            row.Add(check); row.Add(JournalUi.Text($"{group.Key:d MMMM} · {group.Count()} recuerdos")); body.Add(row);
        }
        body.Add(JournalUi.Text("Portada", 22, true));
        var coverPicker = new Picker { Title = "Foto de portada" };
        var choices = entries.SelectMany(x => x.Images.Select((p, i) => (p.Id, Label: $"{x.Note.Title} · foto {i + 1}"))).ToArray();
        coverPicker.Items.Add("Sin foto");
        foreach (var choice in choices) coverPicker.Items.Add(choice.Label);
        coverPicker.SelectedIndex = choices.Length > 0 ? 1 : 0;
        coverPicker.SelectedIndexChanged += (_, _) =>
        {
            coverChosen = true;
            cover = coverPicker.SelectedIndex <= 0 ? null : choices[coverPicker.SelectedIndex - 1].Id;
        };
        body.Add(coverPicker);
        var preview = new Button { Text = "Vista previa", CornerRadius = 22 };
        preview.Clicked += async (_, _) => await ExportAsync("preview"); body.Add(preview);
        var save = new Button { Text = "Guardar PDF", CornerRadius = 22 };
        save.Clicked += async (_, _) => await ExportAsync("save"); body.Add(save);
        var share = new Button { Text = "Compartir álbum", BackgroundColor = Colors.Transparent, TextColor = JournalUi.Ink };
        share.Clicked += async (_, _) => await ExportAsync("share"); body.Add(share);
        var cancel = new Button { Text = "Cancelar exportación", BackgroundColor = Colors.Transparent, TextColor = JournalUi.Ink };
        cancel.Clicked += (_, _) => exporting?.Cancel(); body.Add(cancel);
        body.Add(progress); body.Add(status); Content = new ScrollView { Content = body };
    }

    private async Task ExportAsync(string action)
    {
        if (exporting is not null) return;
        var selected = entries.Where(x => days.Contains(x.Note.Date)).ToArray();
        if (selected.Length == 0) { status.Text = "Elegí al menos un día."; return; }
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
            if (!coverChosen) cover = firstAvailable;
            if (missing > 0 && !await DisplayAlertAsync("Fotos no disponibles", $"Faltan {missing} fotos en este dispositivo. ¿Exportar sin ellas?", "Continuar", "Cancelar")) return;
            status.Text = "Armando tu álbum…";
            var selectedCover = selected.SelectMany(x => x.Images).Any(x => x.Id == cover) ? cover : null;
            var file = await JournalPdfExporter.CreateAsync(store, scope, string.IsNullOrWhiteSpace(title.Text) ? "Mi viaje" : title.Text.Trim(),
                selected, selectedCover, new Progress<double>(value => progress.Progress = value), exporting.Token);
            if (!store.IsCurrent(scope)) return;
            if (action == "preview")
            {
                if (!await Launcher.Default.OpenAsync(new OpenFileRequest("Tu Journal", new ReadOnlyFile(file))))
                    status.Text = "No hay un visor PDF disponible. Podés guardar o compartir el archivo.";
                else status.Text = "Vista previa lista";
            }
            else if (action == "share")
            {
                await Share.Default.RequestAsync(new ShareFileRequest("Mi Journal", new ShareFile(file))); status.Text = "Álbum listo para compartir";
            }
            else
            {
#if ANDROID
                var saved = await Platforms.Android.JournalFileSaver.SaveAsync(file);
                status.Text = saved ? "PDF guardado" : "Guardado cancelado";
#endif
            }
        }
        catch (OperationCanceledException) { status.Text = "Exportación cancelada"; }
        catch (Exception) { status.Text = "No se pudo exportar. Tus notas y fotos siguen guardadas."; }
        finally { exporting.Dispose(); exporting = null; }
    }
    protected override bool OnBackButtonPressed()
    {
        exporting?.Cancel(); _ = Navigation.PopModalAsync(); return true;
    }
}

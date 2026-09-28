using TravelCompanion.Mobile.Services;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Mobile.Pages;

internal static class JournalUi
{
    public static readonly Color Ink = Color.FromArgb("#302920");
    public static readonly Color Muted = Color.FromArgb("#71675D");
    public static readonly Color Paper = Color.FromArgb("#F8F3ED");
    public static Label Text(string text, double size = 15, bool heading = false) => new()
    {
        Text = text, FontSize = size, FontFamily = heading ? "serif" : null,
        TextColor = heading ? Ink : Muted, LineHeight = 1.2
    };
    public static ImageButton Icon(string source, string label, Func<Task> action)
    {
        var button = new ImageButton { Source = source, Padding = 12, WidthRequest = 48, HeightRequest = 48,
            BackgroundColor = Colors.Transparent };
        SemanticProperties.SetDescription(button, label);
        button.Clicked += async (_, _) =>
        {
            if (!button.IsEnabled) return;
            button.IsEnabled = false;
            try { await action(); }
            catch (OperationCanceledException) { }
            catch (Exception) { await Shell.Current.DisplayAlertAsync("Journal", "No se pudo completar. Tu recuerdo sigue guardado; intentá nuevamente.", "Entendido"); }
            finally { button.IsEnabled = true; }
        };
        return button;
    }
}

public sealed class JournalActivityPickerPage : JournalScopedPage
{
    public JournalActivityPickerPage(IReadOnlyList<ScheduleItemDto> activities, IReadOnlyList<JournalMemory> memories)
    {
        BackgroundColor = JournalUi.Paper;
        var store = MauiProgram.Services.GetRequiredService<JournalStore>();
        var scope = store.Scope();
        var search = new SearchBar { Placeholder = "Buscar un lugar de tu viaje" };
        var list = new CollectionView { SelectionMode = SelectionMode.Single,
            ItemsSource = activities.OrderBy(x => x.Date).ThenBy(x => x.StartsAt).ToArray() };
        list.ItemTemplate = new DataTemplate(() =>
        {
            var title = JournalUi.Text("", 19, true); title.SetBinding(Label.TextProperty, "Title");
            var date = JournalUi.Text("", 12); date.SetBinding(Label.TextProperty, new Binding("Date", stringFormat: "{0:d MMMM}"));
            return new VerticalStackLayout { Padding = new Thickness(0, 12), Spacing = 6, Children = { date, title } };
        });
        search.TextChanged += (_, e) => list.ItemsSource = activities.Where(x =>
            $"{x.Title} {x.City}".Contains(e.NewTextValue ?? "", StringComparison.CurrentCultureIgnoreCase)).OrderBy(x => x.Date).ToArray();
        list.SelectionChanged += async (_, e) =>
        {
            if (e.CurrentSelection.FirstOrDefault() is not ScheduleItemDto item) return;
            list.SelectedItem = null;
            if (!store.IsCurrent(scope)) return;
            var memory = memories.FirstOrDefault(x => x.Note.ActivityId == item.Id)
                ?? new JournalMemory(new(item.Id, scope.TripId, item.Title, item.City, item.Date, "", 0, DateTimeOffset.UtcNow));
            await Navigation.PushModalAsync(new JournalMemoryPage(scope, memory, item));
        };
        var header = new Grid { ColumnDefinitions = [new(GridLength.Star), new(GridLength.Auto)] };
        header.Add(JournalUi.Text("Un nuevo recuerdo", 28, true));
        header.Add(JournalUi.Icon("action_close.svg", "Cerrar", () => Navigation.PopModalAsync()), 1);
        var grid = new Grid { Padding = 24, RowSpacing = 16,
            RowDefinitions = [new(GridLength.Auto), new(GridLength.Auto), new(GridLength.Star)] };
        grid.Add(header); grid.Add(search, 0, 1); grid.Add(list, 0, 2);
        list.EmptyView = JournalUi.Text("No hay actividades para mostrar en este viaje.");
        Content = grid;
    }
}

public sealed class JournalMemoryPage : JournalScopedPage
{
    private readonly JournalStore store = MauiProgram.Services.GetRequiredService<JournalStore>();
    private readonly JournalScope scope;
    private JournalMemory memory;
    private readonly Editor editor;
    private readonly VerticalStackLayout photos = new() { Spacing = 12 };
    private readonly Label status = JournalUi.Text("", 12);
    private bool busy;
    private bool closing;
    private int editRevision;
    private bool pickPhotosOnAppearing;

    public JournalMemoryPage(JournalScope scope, JournalMemory memory, ScheduleItemDto? item, bool startWithPhotos = false)
    {
        this.scope = scope; this.memory = memory;
        editRevision = memory.Note.Revision;
        pickPhotosOnAppearing = startWithPhotos;
        BackgroundColor = JournalUi.Paper;
        editor = new Editor { Text = memory.Text, Placeholder = "¿Qué te gustaría recordar de este lugar?",
            MaxLength = 2000, AutoSize = EditorAutoSizeOption.TextChanges, MinimumHeightRequest = 110,
            FontSize = 17, TextColor = JournalUi.Ink, BackgroundColor = Colors.Transparent };
        var header = new Grid { Padding = new Thickness(24, 8), ColumnDefinitions = [new(GridLength.Star), new(GridLength.Auto), new(GridLength.Auto)] };
        header.Add(JournalUi.Text("TU RECUERDO", 11));
        header.Add(JournalUi.Icon("action_close.svg", "Cerrar recuerdo", CloseAsync), 2);
        var body = new VerticalStackLayout { Padding = 24, Spacing = 18, Children =
        {
            JournalUi.Text(memory.Note.Title, 28, true),
            JournalUi.Text($"{memory.Note.Date:d MMMM} · {memory.Note.City}", 13),
            JournalUi.Text("MI NOTA", 11),
            new Border { BackgroundColor = Color.FromArgb("#FFFCF8"), Stroke = Color.FromArgb("#E5DDD3"),
                StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 16 },
                Padding = 14, Content = editor }, photos
        } };
        SemanticProperties.SetDescription(editor, "Nota personal del recuerdo");
        if (item is not null) header.Add(JournalUi.Icon("action_info.svg", "Ver actividad", async () =>
        {
            if (await ConfirmLeaveAsync())
            {
                await Navigation.PopModalAsync();
                if (Navigation.ModalStack.LastOrDefault() is JournalActivityPickerPage) await Navigation.PopModalAsync();
                await Shell.Current.GoToAsync(nameof(ScheduleItemDetailPage), new Dictionary<string, object> { ["ScheduleItem"] = item });
            }
        }), 1);
        var save = new Button { Text = "Guardar recuerdo", CornerRadius = 22, MinimumHeightRequest = 52,
            BackgroundColor = JournalUi.Ink, TextColor = Colors.White };
        save.Clicked += async (_, _) => await SaveAsync();
        status.IsVisible = false;
        status.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(Label.Text)) status.IsVisible = !string.IsNullOrWhiteSpace(status.Text);
        };
        var footer = new VerticalStackLayout { Padding = new Thickness(24, 12, 24, 20), Spacing = 8,
            Children = { status, save } };
        var grid = new Grid { RowDefinitions = [new(GridLength.Auto), new(GridLength.Star), new(GridLength.Auto)] };
        grid.Add(header); grid.Add(new ScrollView { Content = body }, 0, 1);
        grid.Add(footer, 0, 2); Content = grid;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        try
        {
            await ReloadAsync();
            if (pickPhotosOnAppearing) { pickPhotosOnAppearing = false; await AddPhotosAsync(); }
        }
        catch (OperationCanceledException) { }
        catch (Exception) { status.Text = "No pudimos abrir todas las fotos. Podés volver a agregarlas."; }
    }

    private async Task ReloadAsync()
    {
        var entries = await store.LoadAsync(scope, [], false, default);
        memory = entries.FirstOrDefault(x => x.Note.ActivityId == memory.Note.ActivityId) ?? memory;
        status.Text = memory.Status;
        photos.Clear();
        if (memory.Conflict is not null)
        {
            var resolve = new Button { Text = "Comparar versiones", BackgroundColor = Colors.Transparent, TextColor = JournalUi.Ink };
            resolve.Clicked += async (_, _) =>
            {
                var choice = await DisplayActionSheetAsync($"Tu nota:\n{memory.Text}\n\nGuardada:\n{memory.Conflict.Notes}", "Volver", null,
                    "Conservar mi versión", "Usar la guardada");
                if (choice is not ("Conservar mi versión" or "Usar la guardada")) return;
                await store.ResolveAsync(scope, memory, choice == "Conservar mi versión");
                await ReloadAsync(); editor.Text = memory.Text; editRevision = memory.Note.Revision;
            };
            photos.Add(resolve);
        }
        var photoHeader = new Grid { ColumnDefinitions = [new(GridLength.Star), new(GridLength.Auto)] };
        var photoTitle = JournalUi.Text($"FOTOS · {memory.Images.Length} / 10", 11);
        photoTitle.VerticalOptions = LayoutOptions.Center;
        photoHeader.Add(photoTitle);
        photoHeader.Add(JournalUi.Icon("journal_add.svg", "Agregar fotos", AddPhotosAsync), 1);
        photos.Add(photoHeader);
        if (memory.Images.Length == 0)
            photos.Add(JournalUi.Text("Sumá una foto para volver a este momento.", 14));
        var gallery = new FlexLayout { Wrap = Microsoft.Maui.Layouts.FlexWrap.Wrap };
        for (var i = 0; i < memory.Images.Length; i++)
        {
            var index = i;
            var photo = memory.Images[i];
            var bytes = await store.PhotoAsync(scope, photo.Id, true);
            var tile = new VerticalStackLayout { WidthRequest = 88, Margin = new Thickness(0, 0, 10, 10), Spacing = 0 };
            var open = JournalUi.Icon("journal_photo.svg", $"Abrir foto {i + 1}",
                () => Navigation.PushModalAsync(new JournalPhotoPage(scope, memory, index)));
            open.WidthRequest = 88; open.HeightRequest = 88; open.Padding = 0; open.Aspect = Aspect.AspectFill;
            if (bytes is not null) open.Source = ImageSource.FromStream(() => new MemoryStream(bytes));
            tile.Add(new Border { StrokeThickness = 0, BackgroundColor = Color.FromArgb("#E5DDD3"),
                StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 12 }, Content = open });
            var remove = JournalUi.Icon("action_delete.svg", $"Quitar foto {i + 1}", async () =>
            {
                if (!await DisplayAlertAsync("Quitar foto", "La foto original no se elimina de tu galería.", "Quitar", "Cancelar")) return;
                await store.ChangePhotoAsync(scope, memory.Note.ActivityId, photo.Id, true);
                await ReloadAsync();
            });
            remove.HorizontalOptions = LayoutOptions.Center;
            tile.Add(remove); gallery.Children.Add(tile);
        }
        photos.Add(gallery);
        photos.Add(JournalUi.Text("Fotos guardadas en este dispositivo. No se recuperan al cambiar de teléfono.", 12));
    }

    private async Task AddPhotosAsync()
    {
        if (memory.Images.Length >= 10) { await DisplayAlertAsync("Tus fotos", "Podés guardar hasta 10 fotos por recuerdo.", "Entendido"); return; }
        var files = await MediaPicker.Default.PickPhotosAsync(JournalMedia.PickerOptions(10 - memory.Images.Length));
        if (files.Count == 0) return;
        await store.AddPhotosAsync(scope, memory, files.Take(10 - memory.Images.Length));
        await ReloadAsync();
    }

    private async Task SaveAsync()
    {
        if (busy) return;
        busy = true;
        try
        {
            if (memory.Conflict is not null) { status.Text = "Compará las dos versiones antes de guardar."; return; }
            await store.SaveAsync(scope, memory with { Note = memory.Note with { Revision = editRevision } }, editor.Text ?? "");
            await store.LoadAsync(scope, [], Connectivity.Current.NetworkAccess == NetworkAccess.Internet, default);
            await ReloadAsync();
            editor.Text = memory.Text;
            editRevision = memory.Note.Revision;
            status.Text = memory.Status.Length > 0 ? memory.Status : "Recuerdo guardado";
        }
        catch (OperationCanceledException) { status.Text = "Guardado en este dispositivo. Se sincronizará al reconectar."; }
        catch (Exception) { status.Text = "No se pudo guardar. Tu texto sigue aquí; intentá nuevamente."; }
        finally { busy = false; }
    }
    private Task<bool> ConfirmLeaveAsync() => (editor.Text ?? "").Trim() == memory.Text.Trim() ? Task.FromResult(true)
        : DisplayAlertAsync("Cambios sin guardar", "¿Querés descartar los cambios de esta nota?", "Descartar", "Seguir escribiendo");
    private async Task CloseAsync()
    {
        if (closing || busy) return;
        closing = true;
        try { if (await ConfirmLeaveAsync()) await Navigation.PopModalAsync(); }
        finally { closing = false; }
    }
    protected override bool OnBackButtonPressed() { _ = CloseAsync(); return true; }
}

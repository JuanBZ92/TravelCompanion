using TravelCompanion.Mobile.Services;
using TravelCompanion.Shared;
using TravelCompanion.Shared.Dtos;
using static TravelCompanion.Mobile.Pages.ExpenseUi;

namespace TravelCompanion.Mobile.Pages;

public sealed class ExpenseEditorPage : TripScopedPage
{
    private readonly ExpenseStore store = MauiProgram.Services.GetRequiredService<ExpenseStore>();
    private readonly JournalScope scope;
    private readonly ExpenseBook book;
    private readonly ExpenseDto? original;
    private readonly Entry amount = new() { Keyboard = Keyboard.Numeric, Placeholder = "0", FontSize = 34 };
    private readonly Picker currency = new() { Title = "Moneda / Currency", ItemsSource = ExpensePolicy.Currencies };
    private readonly Entry concept = new() { MaxLength = 160 };
    private readonly Entry manual = new() { Keyboard = Keyboard.Numeric, Placeholder = "1 JPY = …" };
    private readonly DatePicker date = new() { MinimumDate = new DateTime(2000, 1, 1), MaximumDate = new DateTime(2100, 12, 31) };
    private readonly Label quote = Text("");
    private readonly Label activityLabel = Text("");
    private ExpenseCategory category;
    private ExpenseActivityDto? activity;
    private bool saving;
    private bool closed;
    private ExpenseRateDto? currentRate;
    private int rateVersion;
    private readonly List<(ExpenseCategory Category, Border View)> categoryViews = [];

    public ExpenseEditorPage(JournalScope scope, ExpenseBook book, ExpenseDto? original = null, ExpenseActivityDto? activity = null)
    {
        this.scope = scope; this.book = book.PendingSettings is { } pending && !book.SettingsConflict
            ? book with { Settings = book.Settings with { Currency = pending.Currency, Budget = pending.Budget } } : book;
        this.original = original; this.activity = activity;
        BackgroundColor = Paper; SafeAreaEdges = SafeAreaEdges.All;
        category = original?.Category ?? activity?.Category ?? ExpenseCategory.Other;
        concept.Placeholder = T("Concepto (opcional)", "Description (optional)");
        concept.Text = original?.Concept ?? activity?.Title;
        amount.Text = original?.Amount.ToString(System.Globalization.CultureInfo.CurrentCulture);
        currency.SelectedItem = original?.Currency ?? book.LastCurrency;
        var today = DateTime.UtcNow;
        try { today = TimeZoneInfo.ConvertTimeFromUtc(today, TimeZoneInfo.FindSystemTimeZoneById(book.TimeZoneId)); } catch (TimeZoneNotFoundException) { }
        date.Date = (original?.Date ?? activity?.Date ?? DateOnly.FromDateTime(today)).ToDateTime(TimeOnly.MinValue);
        if (original?.ActivityId is { } id) this.activity = new(id, original.ActivityTitle ?? "", original.Date, original.Category);
        if (original?.RateSource == "manual" && original.BaseCurrency == this.book.Settings.Currency)
            manual.Text = original.Rate?.ToString(System.Globalization.CultureInfo.CurrentCulture);
        var heading = new Grid { ColumnDefinitions = [new(GridLength.Star), new(GridLength.Auto)] };
        heading.Add(Text(original is null ? T("Agregar gasto", "Add expense") : T("Editar gasto", "Edit expense"), 28, true));
        heading.Add(IconButton("action_close.svg", T("Cerrar", "Close"), () => saving ? Task.CompletedTask : CloseAsync()), 1);
        var amountRow = new Grid { ColumnDefinitions = [new(GridLength.Star), new(new GridLength(100))], ColumnSpacing = 12 };
        amountRow.Add(amount); amountRow.Add(currency, 1);
        amount.Placeholder = T("Importe", "Amount");
        var categories = new Grid { ColumnSpacing = 6, RowSpacing = 6 };
        for (var i = 0; i < 4; i++) categories.ColumnDefinitions.Add(new(GridLength.Star));
        for (var i = 0; i < 2; i++) categories.RowDefinitions.Add(new(GridLength.Auto));
        var choices = new[] { ExpenseCategory.Food, ExpenseCategory.Drinks, ExpenseCategory.Transport, ExpenseCategory.Accommodation,
            ExpenseCategory.Flights, ExpenseCategory.Activities, ExpenseCategory.Shopping, ExpenseCategory.Other };
        for (var i = 0; i < choices.Length; i++)
        {
            var choice = choices[i];
            var button = Button(choice == ExpenseCategory.Drinks ? T("Bebidas", "Drinks") : Category(choice), () => { category = choice; SelectCategory(); return Task.CompletedTask; });
            button.FontSize = 11; button.ImageSource = Icon(choice); button.ContentLayout = new Microsoft.Maui.Controls.Button.ButtonContentLayout(Microsoft.Maui.Controls.Button.ButtonContentLayout.ImagePosition.Top, 6);
            button.Padding = 3;
            var tile = Card(button); tile.Padding = 0; categoryViews.Add((choice, tile)); categories.Add(tile, i % 4, i / 4);
        }
        SelectCategory();
        var optional = new VerticalStackLayout { Spacing = 10, IsVisible = false };
        optional.Add(Text(T("Cambio manual (opcional)", "Manual exchange rate (optional)"))); optional.Add(manual);
        optional.Add(Text(T("Unidades de tu moneda por 1 unidad del gasto. Se conserva al guardar.", "Units of your currency per 1 expense currency unit. Saved with the expense."), 12));
        var activityRow = new Grid { ColumnDefinitions = [new(GridLength.Star), new(GridLength.Auto)], ColumnSpacing = 8 };
        activityLabel.VerticalOptions = LayoutOptions.Center;
        activityRow.Add(activityLabel);
        activityRow.Add(IconButton("expense_link.svg", T("Vincular actividad", "Link activity"), ChooseActivityAsync), 1);
        var detailsRow = new Grid { ColumnDefinitions = [new(GridLength.Star), new(GridLength.Auto)], ColumnSpacing = 8 };
        detailsRow.Add(date);
        detailsRow.Add(IconButton("today_adjust.svg", T("Cambio manual", "Manual exchange rate"), () => { optional.IsVisible = !optional.IsVisible; return Task.CompletedTask; }), 1);
        quote.FontSize = 12; quote.LineBreakMode = LineBreakMode.WordWrap;
        var body = new VerticalStackLayout { Spacing = 12, Children = { amountRow, categories, concept, detailsRow,
            activityRow, optional, quote } };
        if (original?.Rate is { } rate) quote.Text = $"1 {original.Currency} ≈ {rate} {original.BaseCurrency} · {original.RateDate:d} · {original.RateSource}";
        UpdateActivity();
        currency.SelectedIndexChanged += async (_, _) => { manual.Text = ""; await RefreshRateAsync(); };
        date.DateSelected += async (_, _) => await RefreshRateAsync();
        var footer = new Grid { ColumnDefinitions = [new(GridLength.Star), new(GridLength.Auto)] };
        var save = IconButton("expense_check.svg", T("Guardar gasto", "Save expense"), SaveAsync);
        save.BackgroundColor = Ink; save.CornerRadius = 24; save.WidthRequest = 56; save.HeightRequest = 56; save.Padding = 16;
        footer.Add(save, 1);
        if (original is not null) footer.Add(IconButton("action_delete.svg", T("Eliminar gasto", "Delete expense"), DeleteAsync));
        var layout = new Grid { Padding = new Thickness(24, 16), RowDefinitions = [new(GridLength.Auto), new(GridLength.Star), new(GridLength.Auto)], RowSpacing = 12 };
        layout.Add(heading); layout.Add(new ScrollView { Content = body }, 0, 1); layout.Add(footer, 0, 2); Content = layout;
    }
    protected override void OnAppearing() { base.OnAppearing(); Dispatcher.Dispatch(() => amount.Focus()); _ = RefreshRateAsync(); }
    private async Task RefreshRateAsync()
    {
        var version = ++rateVersion;
        currentRate = null;
        try
        {
            var code = currency.SelectedItem as string ?? "JPY"; var day = DateOnly.FromDateTime(date.Date ?? DateTime.Today);
            manual.Placeholder = $"1 {code} = … {book.Settings.Currency}";
            var result = original is { Rate: not null, RateDate: not null } && original.Currency == code
                && original.Date == day && original.BaseCurrency == book.Settings.Currency
                ? new ExpenseRateDto(code, original.BaseCurrency, original.Rate.Value, original.RateDate.Value, original.RateSource ?? "cached")
                : await store.RateAsync(scope, book, code, day);
            if (closed || version != rateVersion || !store.IsCurrent(scope)) return;
            currentRate = result;
            quote.Text = result is null ? T("Conversión pendiente. Podés guardar igual.", "Conversion pending. You can still save.")
                : $"1 {code} ≈ {result.Rate} {result.BaseCurrency} · {result.Date:d} · {result.Source}";
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { ClientDiagnostics.Record("expense_rate_failed", exception: error); }
    }
    protected override bool OnBackButtonPressed() { if (!saving) _ = CloseAsync(); return true; }
    private Task CloseAsync() { if (closed) return Task.CompletedTask; closed = true; return Navigation.PopModalAsync(); }
    private void SelectCategory()
    {
        foreach (var tile in categoryViews)
        {
            tile.View.Stroke = tile.Category == category ? Gold : Colors.Transparent;
            if (tile.View.Content is View button) SemanticProperties.SetDescription(button, Category(tile.Category)
                + (tile.Category == category ? T(", seleccionada", ", selected") : ""));
        }
    }
    private void UpdateActivity() => activityLabel.Text = activity?.Title ?? T("Sin actividad vinculada", "No linked activity");
    private async Task ChooseActivityAsync()
    {
        await Navigation.PushModalAsync(new ExpenseActivityPicker(book.Activities ?? [], DateOnly.FromDateTime(date.Date ?? DateTime.Today), selected =>
        {
            activity = selected; if (selected is not null) { if (string.IsNullOrWhiteSpace(concept.Text)) concept.Text = selected.Title; category = selected.Category; SelectCategory(); }
            UpdateActivity();
        }));
    }
    private async Task SaveAsync()
    {
        if (saving || closed) return;
        if (!store.IsCurrent(scope)) { await CloseAsync(); return; }
        if (!ExpensePolicy.TryAmount(amount.Text ?? "", out var value)) throw new ArgumentException(T("Ingresá un importe válido.", "Enter a valid amount."));
        var code = currency.SelectedItem as string ?? "JPY";
        if (ExpensePolicy.Round(value, code) != value) throw new ArgumentException(T("Revisá los decimales de esta moneda.", "Check the decimal places for this currency."));
        decimal? custom = null;
        if (!string.IsNullOrWhiteSpace(manual.Text))
        { if (!ExpensePolicy.TryAmount(manual.Text, out var parsed) || parsed > 100000000m) throw new ArgumentException(T("Cambio no válido.", "Invalid exchange rate.")); custom = parsed; }
        saving = true;
        try
        {
            var day = DateOnly.FromDateTime(date.Date ?? DateTime.Today);
            // Persist immediately; conversion is resolved during sync without delaying the save.
            var quoteRate = currentRate?.Currency == code && currentRate.Date <= day ? currentRate : null;
            var preserve = original?.Rate is not null && original.Currency == code && original.Date == day
                && original.BaseCurrency == book.Settings.Currency;
            var rate = code == book.Settings.Currency ? 1m : custom ?? (preserve ? original!.Rate : quoteRate?.Rate);
            var entry = new ExpenseDto(original?.Id ?? Guid.NewGuid(), scope.TripId, value, code, day, category,
                string.IsNullOrWhiteSpace(concept.Text) ? Category(category) : concept.Text.Trim(), activity?.Id, activity?.Title,
                rate, rate.HasValue ? custom.HasValue ? day : preserve ? original!.RateDate : quoteRate?.Date ?? day : null,
                custom.HasValue ? "manual" : preserve ? original!.RateSource : quoteRate?.Source,
                book.Settings.Currency, original?.Revision ?? 0, false, Guid.Empty);
            await store.SaveAsync(scope, entry, custom, book.Settings.Revision);
            await CloseAsync();
        }
        finally { saving = false; }
    }
    private async Task DeleteAsync()
    {
        if (saving || original is null || !await DisplayAlertAsync(T("Eliminar gasto", "Delete expense"), original.Concept, T("Eliminar", "Delete"), T("Cancelar", "Cancel"))) return;
        saving = true;
        try { await store.SaveAsync(scope, original with { Deleted = true }, null, book.Settings.Revision); await CloseAsync(); }
        finally { saving = false; }
    }
}

internal sealed class ExpenseActivityPicker : TripScopedPage
{
    private bool closing;
    public ExpenseActivityPicker(IEnumerable<ExpenseActivityDto> items, DateOnly date, Action<ExpenseActivityDto?> selected)
    {
        BackgroundColor = ExpenseUi.Paper; SafeAreaEdges = SafeAreaEdges.All;
        var search = new SearchBar { Placeholder = ExpenseUi.T("Buscar una actividad", "Search activities") };
        var list = new CollectionView { SelectionMode = SelectionMode.Single, ItemTemplate = new DataTemplate(() =>
        {
            var label = ExpenseUi.Text("", 16); label.Margin = new Thickness(8, 12); label.SetBinding(Label.TextProperty, nameof(ExpenseActivityDto.Title)); return label;
        }) };
        var ordered = items.OrderBy(x => Math.Abs(x.Date.DayNumber - date.DayNumber)).ThenBy(x => x.Title).ToArray();
        list.ItemsSource = ordered;
        search.TextChanged += (_, args) => list.ItemsSource = ordered.Where(x => x.Title.Contains(args.NewTextValue ?? "", StringComparison.CurrentCultureIgnoreCase)).ToArray();
        list.SelectionChanged += async (_, args) => { if (!closing && args.CurrentSelection.FirstOrDefault() is ExpenseActivityDto item) { closing = true; selected(item); await Navigation.PopModalAsync(); } };
        var layout = new Grid { Padding = 24, RowDefinitions = [new(GridLength.Auto), new(GridLength.Auto), new(GridLength.Star), new(GridLength.Auto)] };
        layout.Add(ExpenseUi.Button(ExpenseUi.T("Cerrar", "Close"), () => Navigation.PopModalAsync())); layout.Add(search, 0, 1); layout.Add(list, 0, 2);
        layout.Add(ExpenseUi.Button(ExpenseUi.T("Sin actividad", "No activity"), async () => { selected(null); await Navigation.PopModalAsync(); }), 0, 3); Content = layout;
    }
}

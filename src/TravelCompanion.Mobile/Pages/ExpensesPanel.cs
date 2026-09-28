using System.Globalization;
using TravelCompanion.Mobile.Services;
using TravelCompanion.Shared;
using TravelCompanion.Shared.Dtos;
using static TravelCompanion.Mobile.Pages.ExpenseUi;

namespace TravelCompanion.Mobile.Pages;

public sealed class ExpensesPanel : ContentView
{
    private readonly ExpenseStore store = MauiProgram.Services.GetRequiredService<ExpenseStore>();
    private readonly AuthSessionService sessions = MauiProgram.Services.GetRequiredService<AuthSessionService>();
    private readonly TravelCompanionApiClient api = MauiProgram.Services.GetRequiredService<TravelCompanionApiClient>();
    private readonly VerticalStackLayout summary = new() { Spacing = 12, Padding = new Thickness(0, 0, 0, 18) };
    private readonly CollectionView list;
    private readonly Label status = Text("", 12);
    private ExpenseBook? book;
    private JournalScope? scope;
    private bool active;
    private bool refreshing;

    public ExpensesPanel()
    {
        list = new CollectionView { Header = summary, SelectionMode = SelectionMode.None,
            ItemTemplate = new DataTemplate(() =>
            {
                var day = Text("", 12); day.SetBinding(Label.TextProperty, nameof(ExpenseRow.Day));
                var name = Text("", 16, true); name.SetBinding(Label.TextProperty, nameof(ExpenseRow.Name));
                var detail = Text("", 12); detail.SetBinding(Label.TextProperty, nameof(ExpenseRow.Detail));
                var price = Text("", 16, true); price.SetBinding(Label.TextProperty, nameof(ExpenseRow.Price));
                price.HorizontalTextAlignment = TextAlignment.End;
                var icon = new Image { WidthRequest = 24, HeightRequest = 24 }; icon.SetBinding(Image.SourceProperty, nameof(ExpenseRow.Icon));
                var row = new Grid { ColumnDefinitions = [new(new GridLength(30)), new(GridLength.Star), new(GridLength.Auto)], ColumnSpacing = 10 };
                row.Add(icon); row.Add(new VerticalStackLayout { Spacing = 5, Children = { name, detail } }, 1); row.Add(price, 2);
                var layout = new VerticalStackLayout { Spacing = 8, Padding = new Thickness(0, 0, 0, 12), Children = { day, Card(row) } };
                var tap = new TapGestureRecognizer(); tap.Tapped += async (_, _) =>
                {
                    if (layout.BindingContext is ExpenseRow item)
                        await Act(async () => await OpenAsync(item.Entry));
                };
                layout.GestureRecognizers.Add(tap);
                SemanticProperties.SetHint(layout, T("Tocar para editar gasto", "Tap to edit expense"));
                return layout;
            }) };
        var refresh = new RefreshView { Content = list };
        refresh.Refreshing += async (_, _) => { try { await RefreshAsync(); } finally { refresh.IsRefreshing = false; } };
        var root = new Grid { Padding = new Thickness(24, 12, 24, 16), RowDefinitions = [new(GridLength.Star), new(GridLength.Auto)] };
        root.Add(refresh); root.Add(status, 0, 1); Content = root;
    }
    public async Task ActivateAsync()
    {
        if (!active) { active = true; store.Changed += OnChanged; }
        await RefreshAsync();
        var pending = MauiProgram.Services.GetRequiredService<PendingExpenseAction>();
        if (book?.HasPremium == true && scope is { } current && pending.UserId == current.UserId && pending.TripId == current.TripId)
        { var action = pending.Action; pending.Action = null; if (action is not null) await PremiumAsync(action); }
    }
    public void Deactivate() { active = false; store.Changed -= OnChanged; }
    private async void OnChanged(object? sender, EventArgs args)
    {
        if (!active || refreshing) return;
        await Act(async () => await MainThread.InvokeOnMainThreadAsync(async () =>
        {
            if (scope is { } current && store.IsCurrent(current)) { book = await store.ReadAsync(current); Render(); }
        }));
    }
    private async Task Act(Func<Task> action)
    {
        try { await action(); }
        catch (OperationCanceledException) { }
        catch (Exception error)
        {
            ClientDiagnostics.Record("expenses_ui_failed", exception: error);
            if (active) status.Text = T("No pudimos actualizar. Tus gastos guardados siguen disponibles.", "Could not refresh. Saved expenses are still available.");
        }
    }
    public async Task RefreshAsync()
    {
        if (refreshing) return;
        refreshing = true;
        try
        {
            if (!sessions.CurrentTripId.HasValue)
            {
                scope = null; book = null; status.Text = "";
                summary.Clear(); summary.Add(Text(T("Gastos", "Expenses"), 34, true));
                summary.Add(Text(T("Creá un viaje para empezar a registrar tus gastos.", "Create a trip to start tracking expenses.")));
                summary.Add(Button(T("Crear mi viaje", "Create my trip"), BuilderSetupNavigation.OpenAsync, true));
                list.ItemsSource = null; return;
            }
            scope = store.Scope(); book = await store.ReadAsync(scope.Value); Render();
            await store.SyncAsync(scope.Value);
            if (!active || !store.IsCurrent(scope.Value)) return;
            book = await store.ReadAsync(scope.Value);
            if (book.Settings.Revision == 0 && book.PendingSettings is null && book.Items.Count == 0)
            {
                var currency = "EUR";
                try { var local = new RegionInfo(CultureInfo.CurrentCulture.Name).ISOCurrencySymbol; if (ExpensePolicy.Currencies.Contains(local)) currency = local; } catch (ArgumentException) { }
                await store.SaveBudgetAsync(scope.Value, book.Settings, currency, null);
                book = await store.ReadAsync(scope.Value);
            }
            Render();
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { ClientDiagnostics.Record("expenses_load_failed", exception: error); status.Text = T("No pudimos cargar los gastos. Deslizá para reintentar.", "Could not load expenses. Pull to retry."); }
        finally { refreshing = false; }
    }
    private ExpenseBook EffectiveBook() => book!.PendingSettings is { } pending && !book.SettingsConflict
        ? book with { Settings = book.Settings with { Currency = pending.Currency, Budget = pending.Budget } } : book!;
    private void Render()
    {
        if (book is null) return;
        var current = EffectiveBook(); var entries = current.Items.Where(x => !x.Value.Deleted || x.Conflict).OrderByDescending(x => x.Value.Date).ToArray();
        summary.Clear();
        var header = new Grid { ColumnDefinitions = [new(GridLength.Star), new(GridLength.Auto)] };
        header.Add(Text(T("Gastos", "Expenses"), 34, true));
        var add = Button("+", () => OpenAsync(null)); SemanticProperties.SetDescription(add, T("Agregar gasto", "Add expense")); header.Add(add, 1); summary.Add(header);
        var total = ExpensePolicy.Total(entries.Select(x => x.Value).Where(x => x.BaseCurrency == current.Settings.Currency));
        var pendingCount = entries.Count(x => !x.Value.Deleted && (x.Value.Rate is null || x.Value.BaseCurrency != current.Settings.Currency));
        summary.Add(Text((pendingCount > 0 ? "≈ " : "") + Money(total, current.Settings.Currency), 32, true));
        if (pendingCount > 0) summary.Add(Text(T($"Total parcial · {pendingCount} sin conversión", $"Partial total · {pendingCount} awaiting conversion"), 12));
        if (current.Settings.Budget is { } budget)
        {
            summary.Add(new ProgressBar { Progress = (double)Math.Clamp(total / budget, 0, 1), ProgressColor = Gold });
            summary.Add(Text(total <= budget ? T($"Quedan {Money(budget - total, current.Settings.Currency)}", $"Remaining: {Money(budget - total, current.Settings.Currency)}")
                : T($"Superaste el presupuesto en {Money(total - budget, current.Settings.Currency)}", $"Over budget by {Money(total - budget, current.Settings.Currency)}"), 13));
        }
        summary.Add(Button(T("Moneda y presupuesto", "Currency and budget"), BudgetAsync));
        var actions = new Grid { ColumnDefinitions = [new(GridLength.Star), new(GridLength.Star)] };
        actions.Add(Button(T("Ver desglose", "Breakdown"), () => PremiumAsync("breakdown")));
        actions.Add(Button(T("Exportar CSV", "Export CSV"), () => PremiumAsync("export")), 1); summary.Add(actions);
        if (book.SettingsConflict) summary.Add(Button(T("Revisar cambios del presupuesto", "Review budget conflict"), ResolveBudgetAsync));
        if (entries.Length == 0)
        {
            var empty = new VerticalStackLayout { Spacing = 12 };
            empty.Add(Text(T("Tu viaje, también en números", "Your trip, in numbers"), 25, true));
            empty.Add(Text(T("Guardá tus gastos y descubrí cuánto llevás invertido en el viaje.", "Record expenses and see how much you have spent.")));
            empty.Add(Button(T("Agregar primer gasto", "Add first expense"), () => OpenAsync(null), true)); summary.Add(Card(empty));
        }
        DateOnly? previous = null;
        list.ItemsSource = entries.Select(x =>
        {
            var label = previous != x.Value.Date ? x.Value.Date.ToString("d MMMM") : ""; previous = x.Value.Date;
            return new ExpenseRow(x, label, x.Value.Concept, Money(x.Value.Amount, x.Value.Currency), Icon(x.Value.Category),
                x.NeedsEdit ? T("Revisá el gasto o su actividad para sincronizar", "Review expense or linked activity to sync") :
                x.Conflict ? T("Hay otra versión · revisar", "Another version exists · review") : x.Pending is not null ? T("Pendiente de sincronizar", "Pending sync") :
                x.Value.Rate is null || x.Value.BaseCurrency != current.Settings.Currency ? T("Conversión pendiente", "Conversion pending") : x.Value.Currency == current.Settings.Currency ? Category(x.Value.Category)
                : $"≈ {Money(ExpensePolicy.Converted(x.Value)!.Value, current.Settings.Currency)} · {x.Value.RateDate:d}");
        }).ToArray();
        status.Text = book.Items.Any(x => x.Pending is not null) || book.PendingSettings is not null
            ? T("Guardado en este dispositivo · pendiente de sincronizar", "Saved on this device · pending sync") : "";
    }
    private async Task OpenAsync(LocalExpense? entry)
    {
        if (scope is not { } current || book is null || !store.IsCurrent(current)) return;
        if (entry?.Conflict == true)
        {
            var local = $"{entry.Value.Concept}: {Money(entry.Value.Amount, entry.Value.Currency)}";
            var remote = entry.Server is { } server ? $"{server.Concept}: {Money(server.Amount, server.Currency)}{(server.Deleted ? " · eliminado" : "")}" : T("Sin registro remoto", "No remote record");
            var choice = await Shell.Current.DisplayActionSheetAsync($"{T("Este dispositivo", "This device")}: {local}\n{T("Servidor", "Server")}: {remote}", T("Cancelar", "Cancel"), null,
                T("Mantener mi versión", "Keep my version"), T("Usar versión del servidor", "Use server version"));
            if (choice == T("Mantener mi versión", "Keep my version") || choice == T("Usar versión del servidor", "Use server version"))
            { await store.ResolveAsync(current, entry.Value.Id, choice == T("Mantener mi versión", "Keep my version")); await RefreshAsync(); }
            return;
        }
        await Shell.Current.Navigation.PushModalAsync(new ExpenseEditorPage(current, EffectiveBook(), entry?.Value));
    }
    private async Task ResolveBudgetAsync()
    {
        if (scope is not { } current || book?.PendingSettings is not { } pending) return;
        var choice = await Shell.Current.DisplayActionSheetAsync($"{pending.Budget} {pending.Currency} / {book.Settings.Budget} {book.Settings.Currency}", T("Cancelar", "Cancel"), null,
            T("Mantener mi versión", "Keep my version"), T("Usar versión del servidor", "Use server version"));
        if (choice == T("Mantener mi versión", "Keep my version") || choice == T("Usar versión del servidor", "Use server version"))
        { await store.ResolveSettingsAsync(current, choice == T("Mantener mi versión", "Keep my version")); await RefreshAsync(); }
    }
    private async Task BudgetAsync()
    {
        if (book is null || scope is not { } current) return;
        var effective = EffectiveBook();
        await Shell.Current.Navigation.PushModalAsync(new ExpenseBudgetPage(effective.Settings, async (currency, budget) =>
        {
            await store.SaveBudgetAsync(current, book.Settings, currency, budget);
        }));
    }
    private async Task PremiumAsync(string action)
    {
        if (scope is not { } current || book is null || !store.IsCurrent(current)) return;
        if (!book.HasPremium)
        {
            var pending = MauiProgram.Services.GetRequiredService<PendingExpenseAction>(); pending.UserId = current.UserId; pending.TripId = current.TripId; pending.Action = action;
            await PaywallNavigation.OpenAsync(PaywallEntryPoint.Expenses); return;
        }
        if (book.Items.Any(x => x.Pending is not null) || book.PendingSettings is not null)
            throw new InvalidOperationException(T("Sincronizá los gastos pendientes para ver el desglose o exportar.", "Sync pending expenses before viewing a breakdown or exporting."));
        var token = await sessions.GetTokenAsync() ?? throw new InvalidOperationException("Sesión no disponible.");
        try
        {
            if (action == "export")
            {
                var bytes = await api.ExportExpensesAsync(token, current.TripId, default); if (!store.IsCurrent(current)) return;
                var path = Path.Combine(FileSystem.CacheDirectory, "yuku-gastos.csv"); await File.WriteAllBytesAsync(path, bytes);
                if (!store.IsCurrent(current)) return;
                await Share.Default.RequestAsync(new ShareFileRequest(T("Gastos del viaje", "Trip expenses"), new ShareFile(path)));
            }
            else
            {
                var data = await api.GetExpenseBreakdownAsync(token, current.TripId, default); if (!store.IsCurrent(current)) return;
                var body = new VerticalStackLayout { Spacing = 14, Padding = 24 };
                var page = new TripScopedPage { BackgroundColor = Paper, SafeAreaEdges = SafeAreaEdges.All };
                body.Add(Button(T("Cerrar", "Close"), () => page.Navigation.PopModalAsync())); body.Add(Text(T("En qué gastaste", "Spending breakdown"), 30, true));
                var max = Math.Max(1, data.Categories.Sum(x => x.Total));
                foreach (var group in data.Categories.OrderByDescending(x => x.Total))
                { body.Add(Text($"{Category(group.Category)} · {Money(group.Total, data.Currency)}{(group.Pending > 0 ? " ≈" : "")}")); body.Add(new ProgressBar { Progress = (double)(group.Total / max), ProgressColor = Gold }); }
                body.Add(Text(T("Por día", "By day"), 25, true));
                foreach (var day in data.Days) body.Add(Text($"{day.Date:d} · {Money(day.Total, data.Currency)}{(day.Pending > 0 ? " ≈" : "")}"));
                page.Content = new ScrollView { Content = body }; await Shell.Current.Navigation.PushModalAsync(page);
            }
        }
        catch (HttpRequestException error) when (error.StatusCode == System.Net.HttpStatusCode.Forbidden)
        { book = book with { HasPremium = false }; await PremiumAsync(action); }
    }
    private sealed record ExpenseRow(LocalExpense Entry, string Day, string Name, string Price, string Icon, string Detail);
}

internal sealed class ExpenseBudgetPage : TripScopedPage
{
    private bool saving;
    protected override bool OnBackButtonPressed() => saving || base.OnBackButtonPressed();
    public ExpenseBudgetPage(ExpenseSettingsDto current, Func<string, decimal?, Task> save)
    {
        BackgroundColor = Paper; SafeAreaEdges = SafeAreaEdges.All;
        var currency = new Picker { ItemsSource = ExpensePolicy.Currencies, SelectedItem = current.Currency, Title = T("Moneda del resumen", "Summary currency") };
        var budget = new Entry { Keyboard = Keyboard.Numeric, Placeholder = T("Sin presupuesto", "No budget"), Text = current.Budget?.ToString(CultureInfo.CurrentCulture) };
        Content = new ScrollView { Content = new VerticalStackLayout { Padding = 24, Spacing = 20, Children =
        {
            Button(T("Cerrar", "Close"), () => saving ? Task.CompletedTask : Navigation.PopModalAsync()), Text(T("Moneda y presupuesto", "Currency and budget"), 28, true), currency, budget,
            Text(T("Opcional. Si cambiás de moneda, confirmá el presupuesto en la nueva moneda.", "Optional. When changing currency, confirm your budget in the new currency.")),
            Button(T("Guardar", "Save"), async () =>
            {
                decimal? value = null;
                if (!string.IsNullOrWhiteSpace(budget.Text))
                { if (!ExpensePolicy.TryAmount(budget.Text, out var parsed)) throw new ArgumentException(T("Presupuesto no válido.", "Invalid budget.")); value = parsed; }
                var code = currency.SelectedItem as string ?? current.Currency;
                if (value.HasValue && ExpensePolicy.Round(value.Value, code) != value) throw new ArgumentException(T("Revisá los decimales.", "Check decimal places."));
                if (code != current.Currency && !await DisplayAlertAsync(T("Cambiar moneda", "Change currency"), T("Se recalcularán los gastos y se reemplazará el presupuesto con el importe ingresado.", "Expenses will be converted and the budget replaced with the amount entered."), T("Confirmar", "Confirm"), T("Cancelar", "Cancel"))) return;
                saving = true;
                try { await save(code, value); if (Navigation.ModalStack.LastOrDefault() == this) await Navigation.PopModalAsync(); }
                finally { saving = false; }
            }, true)
        } } };
    }
}

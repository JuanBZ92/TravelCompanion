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
    private int refreshVersion;

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
                var open = new Button { BackgroundColor = Colors.Transparent, Padding = 0, MinimumHeightRequest = 48 };
                open.SetBinding(SemanticProperties.DescriptionProperty, nameof(ExpenseRow.OpenDescription));
                open.Clicked += async (_, _) =>
                {
                    if (layout.BindingContext is ExpenseRow item)
                        await Act(async () => await OpenAsync(item.Entry));
                };
                row.Add(open, 0); Grid.SetColumnSpan(open, 3);
                return layout;
            }) };
        var refresh = new RefreshView { Content = list };
        refresh.Refreshing += async (_, _) => { try { await RefreshAsync(); } finally { refresh.IsRefreshing = false; } };
        var root = new Grid { Padding = new Thickness(24, 12, 24, 16), RowDefinitions = [new(GridLength.Star), new(GridLength.Auto)] };
        root.Add(refresh); root.Add(status, 0, 1); Content = root;
    }
    public async Task ActivateAsync()
    {
        if (!active) { active = true; store.Changed += OnChanged; sessions.StateChanged += OnSessionChanged; }
        if (scope is { } previous && !store.IsCurrent(previous)) ClearLocalContent();
        await RefreshAsync();
        if (!active) return;
        var pending = MauiProgram.Services.GetRequiredService<PendingExpenseAction>();
        if (book?.HasPremium == true && scope is { } current && pending.UserId == current.UserId && pending.TripId == current.TripId)
        { var action = pending.Action; pending.Action = null; if (action is not null) await PremiumAsync(action); }
    }
    public void Deactivate()
    {
        active = false; store.Changed -= OnChanged; sessions.StateChanged -= OnSessionChanged;
        refreshVersion++; refreshing = false;
    }
    private void ClearLocalContent()
    {
        scope = null; book = null; summary.Clear(); list.ItemsSource = null; status.Text = "";
    }
    private void OnSessionChanged(object? sender, EventArgs args)
    {
        if (!active || scope is { } current && store.IsCurrent(current)) return;
        Dispatcher.Dispatch(() =>
        {
            if (!active || scope is { } latest && store.IsCurrent(latest)) return;
            refreshVersion++; refreshing = false;
            ClearLocalContent();
            _ = Act(RefreshAsync);
        });
    }
    private async void OnChanged(object? sender, EventArgs args)
    {
        if (!active) return;
        await Act(async () => await MainThread.InvokeOnMainThreadAsync(async () =>
        {
            if (scope is not { } current || !store.IsCurrent(current)) return;
            var latest = await store.ReadAsync(current);
            if (!active || scope != current || !store.IsCurrent(current)) return;
            book = latest;
            Render();
        }));
    }
    private async Task Act(Func<Task> action)
    {
        var operationVersion = refreshVersion;
        var operationScope = scope;
        try { await action(); }
        catch (OperationCanceledException) { }
        catch (Exception error)
        {
            ClientDiagnostics.Record("expenses_ui_failed", exception: error);
            if (active && operationVersion == refreshVersion && operationScope == scope
                && (operationScope is null || store.IsCurrent(operationScope.Value)))
                status.Text = T("No pudimos actualizar. Tus gastos guardados siguen disponibles.", "Could not refresh. Saved expenses are still available.");
        }
    }
    public async Task RefreshAsync()
    {
        if (refreshing) return;
        refreshing = true;
        var version = ++refreshVersion;
        JournalScope? loadingScope = null;
        bool IsCurrentLoad() => active && version == refreshVersion && loadingScope is { } current
            && scope == current && store.IsCurrent(current);
        try
        {
            if (!sessions.HasSession || !sessions.CurrentTripId.HasValue)
            {
                scope = null; book = null; status.Text = "";
                summary.Clear(); summary.Add(Text(T("Gastos", "Expenses"), 34, true));
                summary.Add(Text(T("Creá un viaje para empezar a registrar tus gastos.", "Create a trip to start tracking expenses.")));
                summary.Add(Button(T("Crear mi viaje", "Create my trip"), BuilderSetupNavigation.OpenAsync, true));
                list.ItemsSource = null; return;
            }
            var current = store.Scope(); loadingScope = current; scope = current;
            var local = await store.ReadAsync(current);
            if (!IsCurrentLoad()) return;
            book = local; Render();
            await store.SyncAsync(current);
            if (!IsCurrentLoad()) return;
            local = await store.ReadAsync(current);
            if (!IsCurrentLoad()) return;
            book = local;
            if (book.Settings.Revision == 0 && book.PendingSettings is null && book.Items.Count == 0)
            {
                var currency = "EUR";
                try { var regionCurrency = new RegionInfo(CultureInfo.CurrentCulture.Name).ISOCurrencySymbol; if (ExpensePolicy.Currencies.Contains(regionCurrency)) currency = regionCurrency; } catch (ArgumentException) { }
                await store.SaveBudgetAsync(current, book.Settings, currency, null);
                local = await store.ReadAsync(current);
                if (!IsCurrentLoad()) return;
                book = local;
            }
            Render();
        }
        catch (OperationCanceledException) { }
        catch (Exception error)
        {
            ClientDiagnostics.Record("expenses_load_failed", exception: error);
            if (IsCurrentLoad()) status.Text = T("No pudimos cargar los gastos. Deslizá para reintentar.", "Could not load expenses. Pull to retry.");
        }
        finally { if (version == refreshVersion) refreshing = false; }
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
        header.Add(IconButton("itinerary_add.svg", T("Agregar gasto", "Add expense"), () => OpenAsync(null)), 1); summary.Add(header);
        var total = ExpensePolicy.Total(entries.Select(x => x.Value).Where(x => x.BaseCurrency == current.Settings.Currency));
        var pendingCount = entries.Count(x => !x.Value.Deleted && (x.Value.Rate is null || x.Value.BaseCurrency != current.Settings.Currency));
        var totals = new Grid { ColumnDefinitions = [new(GridLength.Star), new(GridLength.Auto)], ColumnSpacing = 8 };
        var amount = new VerticalStackLayout { Spacing = 4 };
        amount.Add(Text(T("TOTAL DEL VIAJE", "TRIP TOTAL"), 11));
        amount.Add(Text((pendingCount > 0 ? "≈ " : "") + Money(total, current.Settings.Currency), 28, true));
        totals.Add(amount);
        totals.Add(IconButton("today_adjust.svg", T("Moneda y presupuesto", "Currency and budget"), BudgetAsync), 1);
        summary.Add(Card(totals));
        if (pendingCount > 0) summary.Add(Text(T($"Total parcial · {pendingCount} sin conversión", $"Partial total · {pendingCount} awaiting conversion"), 12));
        if (current.Settings.Budget is { } budget)
        {
            summary.Add(new ProgressBar { Progress = (double)Math.Clamp(total / budget, 0, 1), ProgressColor = Gold });
            summary.Add(Text(total <= budget ? T($"Quedan {Money(budget - total, current.Settings.Currency)}", $"Remaining: {Money(budget - total, current.Settings.Currency)}")
                : T($"Superaste el presupuesto en {Money(total - budget, current.Settings.Currency)}", $"Over budget by {Money(total - budget, current.Settings.Currency)}"), 13));
        }
        if (entries.Length > 0)
        {
            var actions = new HorizontalStackLayout { HorizontalOptions = LayoutOptions.End, Spacing = 8 };
            actions.Add(IconButton("expense_chart.svg", T("Ver desglose", "Breakdown"), () => PremiumAsync("breakdown")));
            actions.Add(IconButton("doc_download.svg", T("Exportar CSV", "Export CSV"), () => PremiumAsync("export"))); summary.Add(actions);
        }
        if (book.SettingsConflict) summary.Add(Button(T("Revisar cambios del presupuesto", "Review budget conflict"), ResolveBudgetAsync));
        if (entries.Length == 0)
        {
            var empty = new VerticalStackLayout { Spacing = 10, Padding = new Thickness(4, 18) };
            empty.Add(Text(T("Cada gasto, en su lugar", "Every expense, in one place"), 23, true));
            empty.Add(Text(T("Tocá + para registrar tu primer gasto y seguir el presupuesto de tu viaje.", "Tap + to add your first expense and track your trip budget.")));
            summary.Add(empty);
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
        if (!active || scope is not { } current || book is null || !store.IsCurrent(current)) return;
        if (entry?.Conflict == true)
        {
            var local = $"{entry.Value.Concept}: {Money(entry.Value.Amount, entry.Value.Currency)}";
            var remote = entry.Server is { } server ? $"{server.Concept}: {Money(server.Amount, server.Currency)}{(server.Deleted ? T(" · eliminado", " · deleted") : "")}" : T("Sin registro remoto", "No remote record");
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
        if (!active || scope is not { } current || !store.IsCurrent(current) || book?.PendingSettings is not { } pending) return;
        var choice = await Shell.Current.DisplayActionSheetAsync($"{pending.Budget} {pending.Currency} / {book.Settings.Budget} {book.Settings.Currency}", T("Cancelar", "Cancel"), null,
            T("Mantener mi versión", "Keep my version"), T("Usar versión del servidor", "Use server version"));
        if (choice == T("Mantener mi versión", "Keep my version") || choice == T("Usar versión del servidor", "Use server version"))
        { await store.ResolveSettingsAsync(current, choice == T("Mantener mi versión", "Keep my version")); await RefreshAsync(); }
    }
    private async Task BudgetAsync()
    {
        if (!active || book is null || scope is not { } current || !store.IsCurrent(current)) return;
        var effective = EffectiveBook();
        await Shell.Current.Navigation.PushModalAsync(new ExpenseBudgetPage(effective.Settings, async (currency, budget) =>
        {
            await store.SaveBudgetAsync(current, book.Settings, currency, budget);
            _ = SyncAfterLocalSaveAsync(current);
        }));
    }
    private async Task PremiumAsync(string action)
    {
        if (!active || scope is not { } current || book is null || !store.IsCurrent(current)) return;
        if (!store.CanUsePremiumOffline(current, book))
        {
            var pending = MauiProgram.Services.GetRequiredService<PendingExpenseAction>(); pending.UserId = current.UserId; pending.TripId = current.TripId; pending.Action = action;
            await PaywallNavigation.OpenAsync(PaywallEntryPoint.Expenses); return;
        }
        if (action == "breakdown")
        {
            var local = EffectiveBook();
            var data = ExpensePolicy.Breakdown(local.Items.Select(x => x.Value), local.Settings.Currency);
            await Shell.Current.Navigation.PushModalAsync(new ExpenseBreakdownPage(data,
                local.Items.Any(x => x.Pending is not null) || local.PendingSettings is not null));
            return;
        }
        if (book.Items.Any(x => x.Pending is not null) || book.PendingSettings is not null)
            throw new InvalidOperationException(T("Sincronizá los gastos pendientes antes de exportar.", "Sync pending expenses before exporting."));
        var token = await sessions.GetTokenAsync();
        if (!active || !store.IsCurrent(current)) return;
        if (token is null) throw new InvalidOperationException(T("Sesión no disponible.", "Session unavailable."));
        try
        {
            if (action == "export")
            {
                var bytes = await api.ExportExpensesAsync(token, current.TripId, default); if (!store.IsCurrent(current)) return;
                var path = Path.Combine(FileSystem.CacheDirectory, "yuku-gastos.csv"); await File.WriteAllBytesAsync(path, bytes);
                if (!store.IsCurrent(current)) return;
                await Share.Default.RequestAsync(new ShareFileRequest(T("Gastos del viaje", "Trip expenses"), new ShareFile(path)));
            }
        }
        catch (HttpRequestException error) when (error.StatusCode == System.Net.HttpStatusCode.Forbidden)
        { if (!active || !store.IsCurrent(current)) return; book = book with { HasPremium = false }; await PremiumAsync(action); }
    }
    private async Task SyncAfterLocalSaveAsync(JournalScope current)
    {
        try { await store.SyncAsync(current); }
        catch (OperationCanceledException) { }
        catch (Exception error) { ClientDiagnostics.Record("expenses_background_sync_failed", exception: error); }
    }
    private sealed record ExpenseRow(LocalExpense Entry, string Day, string Name, string Price, string Icon, string Detail)
    {
        public string OpenDescription => $"{Name}. {Price}. {T("Editar gasto", "Edit expense")}";
    }
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

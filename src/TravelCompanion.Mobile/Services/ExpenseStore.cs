using TravelCompanion.Shared;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Mobile.Services;

public sealed record LocalExpense(ExpenseDto Value, SaveExpenseRequest? Pending = null, bool Conflict = false, ExpenseDto? Server = null, bool NeedsEdit = false);
public sealed record ExpenseBook(ExpenseSettingsDto Settings, List<LocalExpense> Items, bool HasPremium = false,
    string TimeZoneId = "Asia/Tokyo", ExpenseActivityDto[]? Activities = null,
    SaveExpenseSettingsRequest? PendingSettings = null, bool SettingsConflict = false, string LastCurrency = "JPY");

public sealed class ExpenseStore(OfflineCacheService cache, AuthSessionService sessions, TravelCompanionApiClient api)
{
    private readonly SemaphoreSlim gate = new(1, 1);
    // A slow connection must never own the lock needed by local edits.
    private readonly SemaphoreSlim syncGate = new(1, 1);
    private readonly HashSet<Guid> deletedUsers = [];
    private readonly HashSet<(Guid, Guid)> deletedTrips = [];
    public event EventHandler? Changed;
    public JournalScope Scope() => sessions.HasSession && sessions.CurrentUserId is { } user && sessions.CurrentTripId is { } trip
        ? new(user, trip, sessions.ContextVersion) : throw new InvalidOperationException("Creá un viaje para registrar gastos.");
    public bool IsCurrent(JournalScope scope) => sessions.HasSession && sessions.CurrentUserId == scope.UserId
        && sessions.CurrentTripId == scope.TripId && sessions.ContextVersion == scope.Version;
    private void Check(JournalScope scope) { if (!IsCurrent(scope) || deletedUsers.Contains(scope.UserId) || deletedTrips.Contains((scope.UserId, scope.TripId))) throw new OperationCanceledException("La sesión cambió."); }
    private static string Key(JournalScope scope) => $"personal-expenses-{scope.UserId}-{scope.TripId}";
    public async Task DeleteAccountAsync(Guid user)
    {
        await gate.WaitAsync();
        try { deletedUsers.Add(user); await cache.DeleteByPrefixAsync($"personal-expenses-{user}-"); }
        finally { gate.Release(); }
    }
    public async Task DeleteTripAsync(Guid user, Guid trip)
    {
        await gate.WaitAsync();
        try { deletedTrips.Add((user, trip)); await cache.DeleteByPrefixAsync($"personal-expenses-{user}-{trip}"); }
        finally { gate.Release(); }
    }
    public async Task TransferLinkedTripAsync(AuthSessionDto verified)
    {
        if (!sessions.HasSession || sessions.CurrentTripId is null || verified.TripId != sessions.CurrentTripId
            || verified.LinkedFromUserId != sessions.CurrentUserId || verified.UserId == sessions.CurrentUserId) return;
        var source = Scope(); await gate.WaitAsync();
        try
        {
            var book = await ReadAsync(source);
            await cache.SaveAsync(Key(source with { UserId = verified.UserId }), book);
            Check(source);
            await cache.DeleteByPrefixAsync(Key(source));
        }
        finally { gate.Release(); }
    }
    public async Task<ExpenseBook> ReadAsync(JournalScope scope)
    {
        Check(scope);
        var book = (await cache.GetAsync<ExpenseBook>(Key(scope)))?.Value ?? new(new("EUR", null, 0, Guid.Empty), []);
        Check(scope); return book;
    }
    private async Task Write(JournalScope scope, ExpenseBook book)
    { Check(scope); await cache.SaveAsync(Key(scope), book); Check(scope); Changed?.Invoke(this, EventArgs.Empty); }

    public async Task SaveAsync(JournalScope scope, ExpenseDto value, decimal? manualRate, int settingsRevision)
    {
#if ANDROID
        using var measurement = MobileOperationMeasurement.Start("expense_save_measured");
#endif
        await gate.WaitAsync();
        try
        {
            Check(scope);
            if (value.TripId != scope.TripId) throw new OperationCanceledException("The expense belongs to another trip.");
            var book = await ReadAsync(scope);
            var request = new SaveExpenseRequest(value.Amount, value.Currency, value.Date, value.Category, value.Concept,
                value.ActivityId, manualRate, value.Revision, settingsRevision, Guid.NewGuid(), value.Deleted,
                value.Rate is { } rate && value.RateDate is { } rateDate && value.RateSource is "Frankfurter" or "cached"
                    ? new(value.Currency, value.BaseCurrency, rate, rateDate, value.RateSource) : null);
            ExpensePolicy.Validate(request);
            var index = book.Items.FindIndex(x => x.Value.Id == value.Id);
            // Keep the revision the editor actually saw. Never overwrite an unseen server revision.
            var entry = new LocalExpense(value, request);
            if (index < 0) book.Items.Add(entry); else book.Items[index] = entry;
            await Write(scope, book with { LastCurrency = value.Currency });
        }
        finally { gate.Release(); }
    }
    public async Task SaveBudgetAsync(JournalScope scope, ExpenseSettingsDto settings, string currency, decimal? budget)
    {
        await gate.WaitAsync();
        try
        {
            var book = await ReadAsync(scope);
            var request = new SaveExpenseSettingsRequest(currency, budget, settings.Revision, Guid.NewGuid());
            if (currency != book.Settings.Currency && book.Items.Count > 0)
            {
                if (book.Items.Any(x => x.Pending is not null) || book.PendingSettings is not null)
                    throw new InvalidOperationException(LocalizationResourceManager.Instance["UxExpensePendingCurrencyChange"]);
            }
            await Write(scope, book with { PendingSettings = request, SettingsConflict = false });
        }
        finally { gate.Release(); }
    }
    private async Task<string> Token(JournalScope scope)
    { var token = await sessions.GetTokenAsync(); Check(scope); return token ?? throw new HttpRequestException("Sesión no disponible."); }
    public async Task ReplayPendingAsync(CancellationToken ct)
    {
        var scope = Scope(); var book = await ReadAsync(scope);
        if (book.PendingSettings is not null || book.Items.Any(x => x.Pending is not null || (!x.Value.Deleted && x.Value.Rate is null)))
            await SyncAsync(scope, ct);
    }
    public async Task SyncAsync(JournalScope scope, CancellationToken ct = default)
    {
        if (!await syncGate.WaitAsync(0, ct)) return;
        try
        {
            var book = await SnapshotAsync(scope, ct);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(TimeSpan.FromSeconds(18));
            var token = await Token(scope);
            if (book.PendingSettings is { } settings && !book.SettingsConflict)
            {
                try
                {
                    var result = await api.SaveExpenseSettingsAsync(token, scope.TripId, settings, timeout.Token); Check(scope);
                    await MergeSettingsAsync(scope, settings, result, ct);
                }
                catch (HttpRequestException error) when (error.StatusCode == System.Net.HttpStatusCode.BadRequest)
                { await MergeSettingsAsync(scope, settings, null, ct); }
                book = await SnapshotAsync(scope, ct);
            }
            for (var i = 0; i < book.Items.Count; i++)
            {
                var entry = book.Items[i];
                if (entry.Conflict || entry.NeedsEdit) continue;
                var pending = entry.Pending;
                if (pending is null && entry.Value.Rate is null && !entry.Value.Deleted)
                {
                    var value = entry.Value;
                    var rate = await api.GetExpenseRateAsync(token, scope.TripId, value.Currency, book.Settings.Currency, value.Date, timeout.Token);
                    Check(scope);
                    if (rate is null) continue;
                    pending = new(value.Amount, value.Currency, value.Date, value.Category, value.Concept, value.ActivityId,
                        null, value.Revision, book.Settings.Revision, Guid.NewGuid(), CachedRate: rate);
                    if (!await AttachRateMutationAsync(scope, entry, pending, ct)) continue;
                }
                if (pending is null) continue;
                // An acknowledged local budget update can advance the settings revision without changing currency.
                if (entry.Value.BaseCurrency == book.Settings.Currency && pending.SettingsRevision < book.Settings.Revision)
                    pending = pending with { SettingsRevision = book.Settings.Revision };
                SaveExpenseResult result;
                try { result = await api.SaveExpenseAsync(token, scope.TripId, entry.Value.Id, pending, timeout.Token); Check(scope); }
                catch (HttpRequestException error) when (error.StatusCode == System.Net.HttpStatusCode.BadRequest)
                {
                    await MergeExpenseAsync(scope, entry.Value.Id, pending, null, rejected: true, ct); continue;
                }
                await MergeExpenseAsync(scope, entry.Value.Id, pending, result, rejected: false, ct);
            }
            var remote = await api.GetExpensesAsync(token, scope.TripId, timeout.Token); Check(scope);
            await gate.WaitAsync(ct);
            try
            {
                var latest = await ReadAsync(scope);
                foreach (var value in remote.Items)
                {
                    var index = latest.Items.FindIndex(x => x.Value.Id == value.Id);
                    if (index < 0) latest.Items.Add(new(value));
                    else if (latest.Items[index].Pending is null && !latest.Items[index].Conflict
                        && value.Revision >= latest.Items[index].Value.Revision) latest.Items[index] = new(value);
                }
                await Write(scope, latest with
                {
                    Settings = remote.Settings.Revision >= latest.Settings.Revision ? remote.Settings : latest.Settings,
                    HasPremium = remote.HasPremium, TimeZoneId = remote.TimeZoneId, Activities = remote.Activities.ToArray()
                });
            }
            finally { gate.Release(); }
        }
        catch (HttpRequestException) { /* Retain pending mutations and their IDs. */ }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested) { }
        finally { syncGate.Release(); }
    }

    private async Task<ExpenseBook> SnapshotAsync(JournalScope scope, CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try { var book = await ReadAsync(scope); return book with { Items = [.. book.Items] }; }
        finally { gate.Release(); }
    }

    private async Task MergeSettingsAsync(JournalScope scope, SaveExpenseSettingsRequest sent,
        ExpenseSettingsDto? result, CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            var latest = await ReadAsync(scope);
            var matches = latest.PendingSettings?.MutationId == sent.MutationId;
            var pending = latest.PendingSettings;
            if (!matches && result is not null && pending is not null
                && pending.ExpectedRevision == sent.ExpectedRevision && latest.Settings.Revision == sent.ExpectedRevision)
                // Rebase only a concurrent local successor over this store's acknowledged mutation.
                pending = pending with { ExpectedRevision = result.Revision };
            await Write(scope, latest with
            {
                Settings = result is not null && result.Revision >= latest.Settings.Revision ? result : latest.Settings,
                PendingSettings = matches && result is not null ? null : pending,
                SettingsConflict = matches ? result is null : latest.SettingsConflict
            });
        }
        finally { gate.Release(); }
    }

    private async Task<bool> AttachRateMutationAsync(JournalScope scope, LocalExpense original,
        SaveExpenseRequest pending, CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            var latest = await ReadAsync(scope);
            var index = latest.Items.FindIndex(x => x.Value.Id == original.Value.Id);
            if (index < 0 || latest.Items[index] != original) return false;
            latest.Items[index] = original with { Pending = pending };
            await Write(scope, latest); return true;
        }
        finally { gate.Release(); }
    }

    private async Task MergeExpenseAsync(JournalScope scope, Guid id, SaveExpenseRequest sent,
        SaveExpenseResult? result, bool rejected, CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            var latest = await ReadAsync(scope);
            var index = latest.Items.FindIndex(x => x.Value.Id == id);
            if (index < 0) return;
            var current = latest.Items[index];
            if (current.Pending?.MutationId == sent.MutationId)
                latest.Items[index] = rejected ? current with { NeedsEdit = true }
                    : result?.Saved == true && result.Entry is { } saved ? new(saved)
                    : current with { Conflict = true, Server = result?.Entry };
            else if (!rejected && result?.Saved == true && result.Entry is { } acknowledged
                && current.Pending is { } newer && newer.ExpectedRevision == sent.ExpectedRevision
                && current.Value.Revision == sent.ExpectedRevision)
                // Rebase only over our acknowledged mutation, never an unseen other-device edit.
                latest.Items[index] = current with
                {
                    Value = current.Value with { Revision = acknowledged.Revision },
                    Pending = newer with { ExpectedRevision = acknowledged.Revision }
                };
            else return;
            await Write(scope, latest);
        }
        finally { gate.Release(); }
    }

    public bool CanUsePremiumOffline(JournalScope scope, ExpenseBook book) => IsCurrent(scope)
        && book.HasPremium && sessions.HasKnownValidAccess
        && sessions.AccessMode is not (SessionAccessMode.FreeMapPreview or SessionAccessMode.BuilderReadOnly);
    public async Task ResolveAsync(JournalScope scope, Guid id, bool keepLocal)
    {
        await gate.WaitAsync();
        try
        {
            var book = await ReadAsync(scope); var index = book.Items.FindIndex(x => x.Value.Id == id);
            if (index < 0) return;
            var entry = book.Items[index]; if (!entry.Conflict) return;
            if (!keepLocal) { if (entry.Server is null) book.Items.RemoveAt(index); else book.Items[index] = new(entry.Server); }
            else
            {
                var sameCurrency = entry.Value.BaseCurrency == book.Settings.Currency;
                book.Items[index] = entry with { Conflict = false, Server = null,
                    Value = entry.Value with { Revision = entry.Server?.Revision ?? 0, BaseCurrency = book.Settings.Currency,
                        Rate = sameCurrency ? entry.Value.Rate : null, RateDate = sameCurrency ? entry.Value.RateDate : null,
                        RateSource = sameCurrency ? entry.Value.RateSource : null },
                    Pending = entry.Pending! with { ExpectedRevision = entry.Server?.Revision ?? 0, SettingsRevision = book.Settings.Revision,
                        ManualRate = sameCurrency ? entry.Pending.ManualRate : null,
                        CachedRate = sameCurrency ? entry.Pending.CachedRate : null, MutationId = Guid.NewGuid() } };
            }
            await Write(scope, book);
        }
        finally { gate.Release(); }
    }
    public async Task ResolveSettingsAsync(JournalScope scope, bool keepLocal)
    {
        await gate.WaitAsync();
        try
        {
            var book = await ReadAsync(scope);
            await Write(scope, book with { SettingsConflict = false, PendingSettings = keepLocal && book.PendingSettings is { } pending
                ? pending with { ExpectedRevision = book.Settings.Revision, MutationId = Guid.NewGuid() } : null });
        }
        finally { gate.Release(); }
    }
    public async Task<ExpenseRateDto?> RateAsync(JournalScope scope, ExpenseBook book, string currency, DateOnly date)
    {
        Check(scope);
        if (currency == book.Settings.Currency) return new(currency, currency, 1, date, "identity");
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(6));
            var result = await api.GetExpenseRateAsync(await Token(scope), scope.TripId, currency, book.Settings.Currency, date, timeout.Token);
            Check(scope); if (result is not null) return result;
        }
        catch (HttpRequestException) { }
        catch (TaskCanceledException) { }
        Check(scope);
        return book.Items.Select(x => x.Value).Where(x => x.Currency == currency && x.BaseCurrency == book.Settings.Currency && x.RateDate <= date && x.Rate.HasValue
                && x.RateSource is "Frankfurter" or "cached")
            .OrderByDescending(x => x.RateDate).Select(x => new ExpenseRateDto(currency, x.BaseCurrency, x.Rate!.Value, x.RateDate!.Value, "cached")).FirstOrDefault();
    }
}

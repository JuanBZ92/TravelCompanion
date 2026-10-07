using TravelCompanion.Mobile.Services;
using TravelCompanion.Shared;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Mobile.Tests;

[Collection("Free map session")]
public sealed class ExpenseStoreTests
{
    [Fact]
    public async Task ExpenseFromAnotherTripCannotBeSavedIntoCurrentBook()
    {
        var sessions = new AuthSessionService(); var disk = new OfflineCacheService();
        var store = new ExpenseStore(disk, sessions, new());
        try
        {
            await sessions.SaveAsync(Session()); var scope = store.Scope();
            await Assert.ThrowsAsync<OperationCanceledException>(() => store.SaveAsync(scope, Item(Guid.NewGuid()), null, 0));
            Assert.Empty((await store.ReadAsync(scope)).Items);
            Assert.Equal(0, disk.Writes);
        }
        finally { sessions.Clear(); }
    }

    [Fact]
    public async Task IdentityRateCannotBeReadWithPreviousSessionScope()
    {
        var sessions = new AuthSessionService(); var store = new ExpenseStore(new(), sessions, new());
        try
        {
            await sessions.SaveAsync(Session()); var previous = store.Scope(); var book = await store.ReadAsync(previous);
            await sessions.SaveAsync(Session());
            await Assert.ThrowsAsync<OperationCanceledException>(() => store.RateAsync(previous, book, book.Settings.Currency, new(2026, 10, 6)));
        }
        finally { sessions.Clear(); }
    }

    [Theory]
    [InlineData("es", "Sincronizá los gastos pendientes antes de cambiar de moneda.")]
    [InlineData("en", "Sync pending expenses before changing the currency.")]
    public async Task PendingExpensesExplainCurrencyChangeRestrictionInSelectedLanguage(string language, string expected)
    {
        var previous = LocalizationResourceManager.Instance.CurrentCulture.Name;
        var previousCulture = System.Globalization.CultureInfo.CurrentCulture;
        var previousUiCulture = System.Globalization.CultureInfo.CurrentUICulture;
        var sessions = new AuthSessionService(); var store = new ExpenseStore(new(), sessions, new());
        try
        {
            LocalizationResourceManager.Instance.SetCulture(language);
            await sessions.SaveAsync(Session()); var scope = store.Scope();
            await store.SaveAsync(scope, Item(scope.TripId), null, 0);
            var book = await store.ReadAsync(scope);
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => store.SaveBudgetAsync(scope, book.Settings, "USD", null));
            Assert.Equal(expected, error.Message);
            Assert.Null((await store.ReadAsync(scope)).PendingSettings);
        }
        finally
        {
            sessions.Clear(); LocalizationResourceManager.Instance.SetCulture(previous);
            System.Globalization.CultureInfo.CurrentCulture = previousCulture;
            System.Globalization.CultureInfo.CurrentUICulture = previousUiCulture;
        }
    }

    [Fact]
    public async Task SaveDuringBlockedSyncKeepsNewMutationAndDoesNotWaitForHttp()
    {
        var sessions = new AuthSessionService(); var api = new TravelCompanionApiClient();
        var store = new ExpenseStore(new(), sessions, api);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            await sessions.SaveAsync(Session()); var scope = store.Scope(); var item = Item(scope.TripId);
            await store.SaveAsync(scope, item, null, 0);
            var sentId = Assert.Single((await store.ReadAsync(scope)).Items).Pending!.MutationId;
            api.FetchExpenses = () => Task.FromResult(new ExpensesDto(new("EUR", null, 0, Guid.Empty), [], false, "Asia/Tokyo", []));
            api.SaveExpense = async (_, request) =>
            { started.SetResult(); await release.Task; return new(true, item with { Revision = 1, MutationId = request.MutationId }); };
            var sync = store.SyncAsync(scope);
            await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await store.SaveAsync(scope, item with { Amount = 3000 }, null, 0).WaitAsync(TimeSpan.FromSeconds(5));
            var newerId = Assert.Single((await store.ReadAsync(scope)).Items).Pending!.MutationId;
            Assert.NotEqual(sentId, newerId); Assert.False(sync.IsCompleted);
            release.SetResult(); await sync;
            var newer = Assert.Single((await store.ReadAsync(scope)).Items);
            Assert.Equal(3000, newer.Value.Amount); Assert.Equal(newerId, newer.Pending!.MutationId);
            Assert.Equal(1, newer.Pending.ExpectedRevision);
            api.SaveExpense = (_, request) => Task.FromResult(new SaveExpenseResult(true, newer.Value with { Revision = 2, MutationId = request.MutationId }));
            await store.SyncAsync(scope);
            Assert.Null(Assert.Single((await store.ReadAsync(scope)).Items).Pending);
        }
        finally { release.TrySetResult(); sessions.Clear(); }
    }

    [Fact]
    public async Task EditorSnapshotSavedAfterOwnConversionAcknowledgementRetainsLocalEditAsConflict()
    {
        var sessions = new AuthSessionService(); var api = new TravelCompanionApiClient(); var disk = new OfflineCacheService();
        var store = new ExpenseStore(disk, sessions, api);
        try
        {
            await sessions.SaveAsync(Session()); var scope = store.Scope();
            var opened = Item(scope.TripId) with { Revision = 1, Rate = null, RateDate = null, RateSource = null };
            var quote = new ExpenseRateDto("JPY", "EUR", .006m, opened.Date, "cached");
            // A conversion mutation can be pending while an editor still holds the prior snapshot.
            var conversion = new SaveExpenseRequest(opened.Amount, opened.Currency, opened.Date, opened.Category,
                opened.Concept, opened.ActivityId, null, opened.Revision, 0, Guid.NewGuid(), CachedRate: quote);
            await disk.SaveAsync($"personal-expenses-{scope.UserId}-{scope.TripId}",
                new ExpenseBook(new("EUR", null, 0, Guid.Empty), [new(opened, conversion)]));
            var converted = opened with { Revision = 2, Rate = quote.Rate, RateDate = quote.Date,
                RateSource = quote.Source, MutationId = conversion.MutationId };
            api.FetchExpenses = () => Task.FromResult(new ExpensesDto(new("EUR", null, 0, Guid.Empty), [], false, "Asia/Tokyo", []));
            api.SaveExpense = (_, request) =>
            {
                Assert.Equal(conversion.MutationId, request.MutationId); Assert.Equal(quote, request.CachedRate);
                return Task.FromResult(new SaveExpenseResult(true, converted));
            };
            await store.SyncAsync(scope);
            var acknowledged = Assert.Single((await store.ReadAsync(scope)).Items);
            Assert.Null(acknowledged.Pending); Assert.Equal(2, acknowledged.Value.Revision);

            var local = opened with { Amount = 3000, Concept = "Editor still open" };
            await store.SaveAsync(scope, local, null, 0);
            var pending = Assert.Single((await store.ReadAsync(scope)).Items).Pending!;
            Assert.Equal(opened.Revision, pending.ExpectedRevision);
            api.SaveExpense = (_, request) =>
            {
                Assert.Equal(1, request.ExpectedRevision);
                return Task.FromResult(new SaveExpenseResult(false, converted));
            };
            await store.SyncAsync(scope);
            var conflict = Assert.Single((await store.ReadAsync(scope)).Items);
            Assert.True(conflict.Conflict); Assert.Equal(local, conflict.Value); Assert.Equal(converted, conflict.Server);
            Assert.Equal(pending.MutationId, conflict.Pending!.MutationId);
            Assert.Equal(opened.Revision, conflict.Pending.ExpectedRevision);
        }
        finally { sessions.Clear(); }
    }

    [Fact]
    public async Task EditorSnapshotSavedAfterOtherDeviceRefreshCannotOverwriteUnseenVersion()
    {
        var sessions = new AuthSessionService(); var api = new TravelCompanionApiClient(); var disk = new OfflineCacheService();
        var store = new ExpenseStore(disk, sessions, api);
        try
        {
            await sessions.SaveAsync(Session()); var scope = store.Scope();
            var opened = Item(scope.TripId) with { Revision = 1 };
            await disk.SaveAsync($"personal-expenses-{scope.UserId}-{scope.TripId}",
                new ExpenseBook(new("EUR", null, 0, Guid.Empty), [new(opened)]));
            var otherDevice = opened with { Revision = 2, Amount = 2000, Concept = "Other device", MutationId = Guid.NewGuid() };
            api.FetchExpenses = () => Task.FromResult(new ExpensesDto(new("EUR", null, 0, Guid.Empty), [otherDevice], false, "Asia/Tokyo", []));
            await store.SyncAsync(scope);
            Assert.Equal(otherDevice, Assert.Single((await store.ReadAsync(scope)).Items).Value);

            var local = opened with { Amount = 3000, Concept = "Local editor" };
            await store.SaveAsync(scope, local, null, 0);
            var pending = Assert.Single((await store.ReadAsync(scope)).Items).Pending!;
            api.SaveExpense = (_, request) =>
            {
                Assert.Equal(opened.Revision, request.ExpectedRevision);
                return Task.FromResult(new SaveExpenseResult(false, otherDevice));
            };
            await store.SyncAsync(scope);
            var conflict = Assert.Single((await store.ReadAsync(scope)).Items);
            Assert.True(conflict.Conflict); Assert.Equal(local, conflict.Value); Assert.Equal(otherDevice, conflict.Server);
            Assert.Equal(pending.MutationId, conflict.Pending!.MutationId);
            Assert.Equal(opened.Revision, conflict.Pending.ExpectedRevision);
        }
        finally { sessions.Clear(); }
    }

    [Fact]
    public async Task RemoteRefreshDoesNotEraseNewExpenseOrBudgetSavedWhileWaiting()
    {
        var sessions = new AuthSessionService(); var api = new TravelCompanionApiClient(); var store = new ExpenseStore(new(), sessions, api);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<ExpensesDto>(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            await sessions.SaveAsync(Session()); var scope = store.Scope();
            api.FetchExpenses = () => { started.TrySetResult(); return release.Task; };
            var sync = store.SyncAsync(scope); await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await store.SaveAsync(scope, Item(scope.TripId), null, 0).WaitAsync(TimeSpan.FromSeconds(5));
            await store.SaveBudgetAsync(scope, new("EUR", null, 0, Guid.Empty), "EUR", 200).WaitAsync(TimeSpan.FromSeconds(5));
            release.SetResult(new(new("EUR", null, 0, Guid.Empty), [], false, "Asia/Tokyo", [])); await sync;
            var local = await store.ReadAsync(scope);
            Assert.NotNull(Assert.Single(local.Items).Pending); Assert.Equal(200, local.PendingSettings!.Budget);
        }
        finally { release.TrySetResult(new(new("EUR", null, 0, Guid.Empty), [], false, "Asia/Tokyo", [])); sessions.Clear(); }
    }

    [Fact]
    public async Task OlderBudgetAcknowledgementCannotClearNewBudgetMutation()
    {
        var sessions = new AuthSessionService(); var api = new TravelCompanionApiClient(); var store = new ExpenseStore(new(), sessions, api);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            await sessions.SaveAsync(Session()); var scope = store.Scope();
            await store.SaveBudgetAsync(scope, new("EUR", null, 0, Guid.Empty), "EUR", 100);
            api.SaveExpenseSettings = async request => { started.SetResult(); await release.Task; return new("EUR", 100, 1, request.MutationId); };
            api.FetchExpenses = () => Task.FromResult(new ExpensesDto(new("EUR", 100, 1, Guid.Empty), [], false, "Asia/Tokyo", []));
            var sync = store.SyncAsync(scope); await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await store.SaveBudgetAsync(scope, new("EUR", null, 0, Guid.Empty), "EUR", 200).WaitAsync(TimeSpan.FromSeconds(5));
            var newerId = (await store.ReadAsync(scope)).PendingSettings!.MutationId;
            release.SetResult(); await sync;
            var book = await store.ReadAsync(scope);
            Assert.Equal(200, book.PendingSettings!.Budget); Assert.Equal(newerId, book.PendingSettings.MutationId);
            Assert.Equal(1, book.PendingSettings.ExpectedRevision);
            api.SaveExpenseSettings = request =>
            {
                Assert.Equal(newerId, request.MutationId); Assert.Equal(1, request.ExpectedRevision);
                Assert.Equal(200, request.Budget);
                return Task.FromResult<ExpenseSettingsDto?>(new("EUR", request.Budget, 2, request.MutationId));
            };
            api.FetchExpenses = () => Task.FromResult(new ExpensesDto(new("EUR", 200, 2, newerId), [], false, "Asia/Tokyo", []));
            await store.SyncAsync(scope);
            var saved = await store.ReadAsync(scope);
            Assert.Null(saved.PendingSettings); Assert.False(saved.SettingsConflict);
            Assert.Equal(200, saved.Settings.Budget); Assert.Equal(2, saved.Settings.Revision);
        }
        finally { release.TrySetResult(); sessions.Clear(); }
    }

    [Fact]
    public async Task CachedPremiumDoesNotUnlockFreeOrExpiredSession()
    {
        var sessions = new AuthSessionService(); var store = new ExpenseStore(new(), sessions, new());
        try
        {
            var account = Session(); await sessions.SaveAsync(account); var book = new ExpenseBook(new("EUR", null, 0, Guid.Empty), [], true);
            Assert.False(store.CanUsePremiumOffline(store.Scope(), book));
            await sessions.SaveAsync(account with { AccessMode = SessionAccessMode.Trip, Capabilities = null });
            Assert.True(store.CanUsePremiumOffline(store.Scope(), book));
            sessions.ApplySyncState(new(new(true, true, true, true, false), DateTimeOffset.UtcNow.AddMinutes(-1), account.TripId,
                null, null, 0, 0, 0, 0, 0));
            Assert.False(store.CanUsePremiumOffline(store.Scope(), book));
        }
        finally { sessions.Clear(); }
    }
    [Fact]
    public async Task CachedPremiumCannotOverrideAReadOnlyBuilderSessionWithAStillValidLogin()
    {
        var sessions = new AuthSessionService(); var store = new ExpenseStore(new(), sessions, new());
        try
        {
            var account = Session() with { AccessMode = SessionAccessMode.Builder,
                ExperienceMode = ExperienceMode.SelfServiceBuilder, Capabilities = new(true, true, true, false, false, true) };
            await sessions.SaveAsync(account);
            var book = new ExpenseBook(new("EUR", null, 0, Guid.Empty), [], true);
            Assert.True(store.CanUsePremiumOffline(store.Scope(), book));
            // A revoked/expired paid grant keeps the account login available,
            // while synchronization changes its itinerary access to read-only.
            sessions.ApplySyncState(new(new(false, false, false, false, false), DateTimeOffset.UtcNow.AddDays(1),
                account.TripId, null, null, 0, 0, 0, 0, 0) { AccessMode = SessionAccessMode.BuilderReadOnly });
            Assert.True(sessions.HasKnownValidAccess);
            Assert.True(sessions.HasSession);
            Assert.False(store.CanUsePremiumOffline(store.Scope(), book));
        }
        finally { sessions.Clear(); }
    }
    private static AuthSessionDto Session() => new(Guid.NewGuid(), "expenses@test.local", "Test", false, "token", Guid.NewGuid(),
        AccessMode: SessionAccessMode.FreeMapPreview, ExperienceMode: ExperienceMode.FreePreview, Capabilities: new(false, false, false, false, false));
    private static ExpenseDto Item(Guid trip) => new(Guid.NewGuid(), trip, 1000, "JPY", new(2026, 9, 29), ExpenseCategory.Food,
        "Comida", null, null, .006m, new(2026, 9, 29), "Frankfurter", "EUR", 0, false, Guid.Empty);
    [Fact]
    public async Task OfflineMutationSurvivesRestartAndIsIsolatedAcrossAccountsAndTrips()
    {
        var sessions = new AuthSessionService(); var account = Session(); var disk = new OfflineCacheService(); var api = new TravelCompanionApiClient();
        try
        {
            await sessions.SaveAsync(account); var store = new ExpenseStore(disk, sessions, api); var scope = store.Scope();
            await store.SaveAsync(scope, Item(scope.TripId), null, 0); await store.SyncAsync(scope);
            var saved = Assert.Single((await store.ReadAsync(scope)).Items); Assert.NotNull(saved.Pending);
            var restarted = new ExpenseStore(disk, sessions, api);
            Assert.Equal(saved.Pending.MutationId, Assert.Single((await restarted.ReadAsync(scope)).Items).Pending!.MutationId);
            await sessions.SaveAsync(Session()); Assert.Empty((await restarted.ReadAsync(restarted.Scope())).Items);
            await sessions.SaveAsync(account with { TripId = Guid.NewGuid() }); Assert.Empty((await restarted.ReadAsync(restarted.Scope())).Items);
            await sessions.SaveAsync(account); Assert.Single((await restarted.ReadAsync(restarted.Scope())).Items);
        }
        finally { sessions.Clear(); }
    }
    [Fact]
    public async Task AcknowledgedSaveDoesNotDuplicateAndConflictRetainsBothVersions()
    {
        var sessions = new AuthSessionService(); var api = new TravelCompanionApiClient(); var store = new ExpenseStore(new(), sessions, api);
        try
        {
            await sessions.SaveAsync(Session()); var scope = store.Scope(); var item = Item(scope.TripId);
            api.FetchExpenses = () => Task.FromResult(new ExpensesDto(new("EUR", null, 0, Guid.Empty), [], false, "Asia/Tokyo", []));
            api.SaveExpense = (id, request) => Task.FromResult(new SaveExpenseResult(true, item with { Revision = 1, MutationId = request.MutationId }));
            await store.SaveAsync(scope, item, null, 0); await store.SyncAsync(scope);
            var saved = Assert.Single((await store.ReadAsync(scope)).Items); Assert.Null(saved.Pending);
            var server = item with { Amount = 2000, Revision = 2 };
            api.SaveExpense = (_, _) => Task.FromResult(new SaveExpenseResult(false, server));
            await store.SaveAsync(scope, saved.Value with { Amount = 3000 }, null, 0); await store.SyncAsync(scope);
            var conflict = Assert.Single((await store.ReadAsync(scope)).Items);
            Assert.True(conflict.Conflict); Assert.Equal(3000, conflict.Value.Amount); Assert.Equal(2000, conflict.Server!.Amount);
            await store.ResolveAsync(scope, item.Id, false);
            Assert.Equal(2000, Assert.Single((await store.ReadAsync(scope)).Items).Value.Amount);
        }
        finally { sessions.Clear(); }
    }
    [Fact]
    public async Task SessionChangeDuringNetworkCallCannotWriteIntoNewAccount()
    {
        var sessions = new AuthSessionService(); var api = new TravelCompanionApiClient(); var disk = new OfflineCacheService(); var store = new ExpenseStore(disk, sessions, api);
        try
        {
            await sessions.SaveAsync(Session()); var scope = store.Scope(); var item = Item(scope.TripId);
            await store.SaveAsync(scope, item, null, 0);
            api.SaveExpense = async (_, _) => { await sessions.SaveAsync(Session()); return new(true, item with { Revision = 1 }); };
            await Assert.ThrowsAsync<OperationCanceledException>(() => store.SyncAsync(scope));
            Assert.Empty((await store.ReadAsync(store.Scope())).Items);
        }
        finally { sessions.Clear(); }
    }
    [Fact]
    public async Task DeletingTripClearsItsOfflineExpensesAndPreventsLateWrites()
    {
        var sessions = new AuthSessionService(); var store = new ExpenseStore(new(), sessions, new());
        try
        {
            await sessions.SaveAsync(Session()); var scope = store.Scope(); var item = Item(scope.TripId);
            await store.SaveAsync(scope, item, null, 0); await store.DeleteTripAsync(scope.UserId, scope.TripId);
            await Assert.ThrowsAsync<OperationCanceledException>(() => store.SaveAsync(scope, item, null, 0));
        }
        finally { sessions.Clear(); }
    }
    [Fact]
    public async Task RejectedExpenseCanBeEditedAndRetriedWithoutLosingTheDraft()
    {
        var sessions = new AuthSessionService(); var api = new TravelCompanionApiClient(); var store = new ExpenseStore(new(), sessions, api);
        try
        {
            await sessions.SaveAsync(Session()); var scope = store.Scope(); var item = Item(scope.TripId);
            api.FetchExpenses = () => Task.FromResult(new ExpensesDto(new("EUR", null, 0, Guid.Empty), [], false, "Asia/Tokyo", []));
            api.SaveExpense = (_, _) => throw new HttpRequestException("Invalid activity", null, System.Net.HttpStatusCode.BadRequest);
            await store.SaveAsync(scope, item, null, 0); await store.SyncAsync(scope);
            var rejected = Assert.Single((await store.ReadAsync(scope)).Items);
            Assert.True(rejected.NeedsEdit); Assert.Equal(item.Amount, rejected.Value.Amount);
            api.SaveExpense = (_, request) => Task.FromResult(new SaveExpenseResult(true, item with { Revision = 1, MutationId = request.MutationId }));
            await store.SaveAsync(scope, rejected.Value, null, 0); await store.SyncAsync(scope);
            var saved = Assert.Single((await store.ReadAsync(scope)).Items);
            Assert.False(saved.NeedsEdit); Assert.Null(saved.Pending);
        }
        finally { sessions.Clear(); }
    }
    [Fact]
    public async Task OfflineQuoteNeverReusesAnotherExpensesManualRate()
    {
        var sessions = new AuthSessionService(); var store = new ExpenseStore(new(), sessions, new());
        try
        {
            await sessions.SaveAsync(Session()); var scope = store.Scope();
            var manual = Item(scope.TripId) with { RateSource = "manual" };
            await store.SaveAsync(scope, manual, manual.Rate, 0);
            var book = await store.ReadAsync(scope);
            Assert.Null(await store.RateAsync(scope, book, "JPY", manual.Date));
            await store.SaveAsync(scope, Item(scope.TripId), null, 0);
            var quote = await store.RateAsync(scope, await store.ReadAsync(scope), "JPY", manual.Date);
            Assert.NotNull(quote); Assert.Equal("cached", quote.Source);
        }
        finally { sessions.Clear(); }
    }
    [Fact]
    public async Task KeepingLocalConflictRetainsManualRateWhenBaseCurrencyDidNotChange()
    {
        var sessions = new AuthSessionService(); var api = new TravelCompanionApiClient(); var store = new ExpenseStore(new(), sessions, api);
        try
        {
            await sessions.SaveAsync(Session()); var scope = store.Scope(); var item = Item(scope.TripId) with { RateSource = "manual" };
            api.FetchExpenses = () => Task.FromResult(new ExpensesDto(new("EUR", null, 0, Guid.Empty), [], false, "Asia/Tokyo", []));
            api.SaveExpense = (_, _) => Task.FromResult(new SaveExpenseResult(false, item with { Revision = 2, Amount = 2000 }));
            await store.SaveAsync(scope, item, item.Rate, 0); await store.SyncAsync(scope);
            await store.ResolveAsync(scope, item.Id, true);
            var resolved = Assert.Single((await store.ReadAsync(scope)).Items);
            Assert.False(resolved.Conflict); Assert.Equal(item.Rate, resolved.Pending!.ManualRate);
            Assert.Equal(2, resolved.Pending.ExpectedRevision); Assert.Equal(item.Amount, resolved.Value.Amount);
        }
        finally { sessions.Clear(); }
    }
    [Fact]
    public async Task VerifiedAccountLinkTransfersOfflineExpenses()
    {
        var sessions = new AuthSessionService(); var original = Session(); var store = new ExpenseStore(new(), sessions, new());
        try
        {
            await sessions.SaveAsync(original); var scope = store.Scope(); await store.SaveAsync(scope, Item(scope.TripId), null, 0);
            var linked = original with { UserId = Guid.NewGuid(), LinkedFromUserId = original.UserId };
            await store.TransferLinkedTripAsync(linked); await sessions.SaveAsync(linked);
            Assert.Single((await store.ReadAsync(store.Scope())).Items);
            await sessions.SaveAsync(original); Assert.Empty((await store.ReadAsync(store.Scope())).Items);
        }
        finally { sessions.Clear(); }
    }

    [Fact]
    public async Task AccountChangeWhileLinkedBookIsSavedCannotEraseOriginalExpenses()
    {
        var sessions = new AuthSessionService(); var account = Session(); var disk = new OfflineCacheService();
        var store = new ExpenseStore(disk, sessions, new());
        try
        {
            await sessions.SaveAsync(account); var scope = store.Scope();
            await store.SaveAsync(scope, Item(scope.TripId), null, 0);
            var linked = account with { UserId = Guid.NewGuid(), LinkedFromUserId = account.UserId };
            disk.BeforeSave = () => sessions.SaveAsync(Session());
            await Assert.ThrowsAsync<OperationCanceledException>(() => store.TransferLinkedTripAsync(linked));
            disk.BeforeSave = null;
            await sessions.SaveAsync(account);
            Assert.NotNull(Assert.Single((await store.ReadAsync(store.Scope())).Items).Pending);
        }
        finally { disk.BeforeSave = null; sessions.Clear(); }
    }
}

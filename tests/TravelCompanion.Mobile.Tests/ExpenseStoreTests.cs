using TravelCompanion.Mobile.Services;
using TravelCompanion.Shared;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Mobile.Tests;

[Collection("Free map session")]
public sealed class ExpenseStoreTests
{
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
}

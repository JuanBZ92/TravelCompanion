using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using TravelCompanion.Api.Data;
using TravelCompanion.Api.Models;
using TravelCompanion.Api.Services;
using TravelCompanion.Shared;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Api.Tests;

public sealed class ExpenseServiceTests
{
    [Fact]
    public async Task FreeCanSaveRetryAndEditWithoutChangingItinerary()
    {
        await using var f = await Fixture.Create(); var id = Guid.NewGuid(); var request = f.Request();
        var first = await f.Service.SaveAsync(f.Context, f.Trip.Id, id, request, default);
        var retry = await f.Service.SaveAsync(f.Context, f.Trip.Id, id, request, default);
        Assert.Equal(first, retry); Assert.Single(f.Db.TripExpenses); Assert.Equal(0, f.Trip.PlanRevision);
        Assert.Equal(6m, ExpensePolicy.Converted(first.Entry!));
        var stale = await f.Service.SaveAsync(f.Context, f.Trip.Id, id, request with { MutationId = Guid.NewGuid(), Amount = 2000 }, default);
        Assert.False(stale.Saved); Assert.Equal(1000, stale.Entry!.Amount);
        await Assert.ThrowsAsync<ExpensePremiumException>(() => f.Service.ExportAsync(f.Context, f.Trip.Id, default));
    }
    [Fact]
    public async Task DeletingReservationRetainsExpenseAndDeletingExpenseCannotBeUndoneByOldRetry()
    {
        await using var f = await Fixture.Create(); var id = Guid.NewGuid(); var request = f.Request() with { ActivityId = f.Activity.Id };
        var saved = await f.Service.SaveAsync(f.Context, f.Trip.Id, id, request, default);
        f.Db.Reservations.Remove(f.Activity); await f.Db.SaveChangesAsync();
        var deleted = await f.Service.SaveAsync(f.Context, f.Trip.Id, id, request with { ExpectedRevision = 1, MutationId = Guid.NewGuid(), Deleted = true }, default);
        Assert.True(deleted.Saved); Assert.Equal("Café", deleted.Entry!.ActivityTitle);
        var retry = await f.Service.SaveAsync(f.Context, f.Trip.Id, id, request, default);
        Assert.False(retry.Saved); Assert.True(retry.Entry!.Deleted);
    }
    [Fact]
    public async Task IsolatesTripsAndActivityReferences()
    {
        await using var f = await Fixture.Create();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => f.Service.GetAsync(f.Context, Guid.NewGuid(), default));
        await Assert.ThrowsAsync<ArgumentException>(() => f.Service.SaveAsync(f.Context, f.Trip.Id, Guid.NewGuid(), f.Request() with { ActivityId = Guid.NewGuid() }, default));
    }
    [Fact]
    public async Task RateIsFrozenButCurrencyChangeRevaluesAndDetectsOldEditors()
    {
        await using var f = await Fixture.Create(); var id = Guid.NewGuid(); var request = f.Request();
        await f.Service.SaveAsync(f.Context, f.Trip.Id, id, request, default);
        f.Rates.Rate = .02m;
        var edited = await f.Service.SaveAsync(f.Context, f.Trip.Id, id, request with { ExpectedRevision = 1, MutationId = Guid.NewGuid(), Concept = "Nuevo" }, default);
        Assert.Equal(.006m, edited.Entry!.Rate);
        await f.Service.SaveSettingsAsync(f.Context, f.Trip.Id, new("USD", 100, 0, Guid.NewGuid()), default);
        var data = await f.Service.GetAsync(f.Context, f.Trip.Id, default);
        Assert.Equal("USD", data.Settings.Currency); Assert.Equal(.02m, data.Items.Single().Rate);
        f.Trip.ExperienceMode = ExperienceMode.CuratedPremium; await f.Db.SaveChangesAsync();
        var breakdown = await f.Service.BreakdownAsync(f.Context, f.Trip.Id, default);
        Assert.Equal("USD", breakdown.Currency); Assert.Equal(20m, Assert.Single(breakdown.Categories).Total);
        var conflict = await f.Service.SaveAsync(f.Context, f.Trip.Id, id, request with { ExpectedRevision = 2, MutationId = Guid.NewGuid() }, default);
        Assert.True(conflict.SettingsChanged);
    }
    [Fact]
    public async Task MissingRatesKeepExpenseAndFailedCurrencyChangeKeepsOldBudget()
    {
        await using var f = await Fixture.Create(); f.Rates.Rate = null;
        var result = await f.Service.SaveAsync(f.Context, f.Trip.Id, Guid.NewGuid(), f.Request(), default);
        Assert.True(result.Saved); Assert.Null(result.Entry!.Rate);
        await Assert.ThrowsAsync<ArgumentException>(() => f.Service.SaveSettingsAsync(f.Context, f.Trip.Id, new("USD", 200, 0, Guid.NewGuid()), default));
        var data = await f.Service.GetAsync(f.Context, f.Trip.Id, default);
        Assert.Equal("EUR", data.Settings.Currency); Assert.Null(data.Settings.Budget);
    }
    [Fact]
    public async Task ExpiredPassKeepsBasicExpensesButLosesPremiumReports()
    {
        await using var f = await Fixture.Create();
        var grant = new BuilderAccessGrant { Id = Guid.NewGuid(), AppUserId = f.Trip.AppUserId!.Value,
            TripId = f.Trip.Id, ExpiresAtUtc = DateTimeOffset.UtcNow.AddDays(1) };
        f.Db.BuilderAccessGrants.Add(grant); await f.Db.SaveChangesAsync();
        Assert.True((await f.Service.GetAsync(f.Context, f.Trip.Id, default)).HasPremium);
        await f.Service.BreakdownAsync(f.Context, f.Trip.Id, default);
        grant.ExpiresAtUtc = DateTimeOffset.UtcNow.AddDays(-1); await f.Db.SaveChangesAsync();
        Assert.False((await f.Service.GetAsync(f.Context, f.Trip.Id, default)).HasPremium);
        var saved = await f.Service.SaveAsync(f.Context, f.Trip.Id, Guid.NewGuid(), f.Request(), default);
        Assert.True(saved.Saved);
        await Assert.ThrowsAsync<ExpensePremiumException>(() => f.Service.BreakdownAsync(f.Context, f.Trip.Id, default));
        await Assert.ThrowsAsync<ExpensePremiumException>(() => f.Service.ExportAsync(f.Context, f.Trip.Id, default));
    }
    [Fact]
    public async Task CachedRatePersistsAndCuratedCanExport()
    {
        await using var f = await Fixture.Create(); f.Trip.ExperienceMode = ExperienceMode.CuratedPremium; await f.Db.SaveChangesAsync();
        var request = f.Request() with { CachedRate = new("JPY", "EUR", .005m, new(2026, 9, 25), "cached"), Concept = "=HYPERLINK(\"unsafe\")" };
        var result = await f.Service.SaveAsync(f.Context, f.Trip.Id, Guid.NewGuid(), request, default);
        Assert.Equal("cached", result.Entry!.RateSource); Assert.Equal(5m, ExpensePolicy.Converted(result.Entry));
        Assert.Contains("'=HYPERLINK", await f.Service.ExportAsync(f.Context, f.Trip.Id, default));
        Assert.Single((await f.Service.BreakdownAsync(f.Context, f.Trip.Id, default)).Categories);
    }
    [Fact]
    public async Task BreakdownUsesSettingsCurrencyAndSharedPolicyForStaleAndMissingRates()
    {
        await using var f = await Fixture.Create();
        f.Trip.ExperienceMode = ExperienceMode.CuratedPremium;
        await f.Db.SaveChangesAsync();
        await f.Service.SaveSettingsAsync(f.Context, f.Trip.Id, new("USD", null, 0, Guid.NewGuid()), default);
        var date = new DateOnly(2026, 9, 29);
        TripExpense Entry(string baseCurrency, decimal? rate, bool deleted = false) => new()
        {
            Id = Guid.NewGuid(), TripId = f.Trip.Id, UserId = f.Trip.AppUserId!.Value,
            Amount = 1000, Currency = "JPY", BaseCurrency = baseCurrency, Rate = rate,
            Date = date, Category = ExpenseCategory.Food, Deleted = deleted
        };
        f.Db.TripExpenses.AddRange(Entry("USD", .02m), Entry("EUR", .006m), Entry("USD", null), Entry("USD", 1, true));
        var otherAccount = Entry("USD", 1); otherAccount.UserId = Guid.NewGuid();
        f.Db.TripExpenses.Add(otherAccount);
        await f.Db.SaveChangesAsync();

        var data = await f.Service.GetAsync(f.Context, f.Trip.Id, default);
        var expected = ExpensePolicy.Breakdown(data.Items, data.Settings.Currency);
        var breakdown = await f.Service.BreakdownAsync(f.Context, f.Trip.Id, default);
        Assert.Equal("USD", breakdown.Currency);
        Assert.Equal(expected.Categories, breakdown.Categories);
        Assert.Equal(expected.Days, breakdown.Days);
        var category = Assert.Single(breakdown.Categories);
        Assert.Equal(20m, category.Total);
        Assert.Equal(2, category.Pending);
        Assert.Equal(20m, Assert.Single(breakdown.Days).Total);
    }

    [Fact]
    public async Task EmptyBreakdownKeepsTheConfiguredCurrency()
    {
        await using var f = await Fixture.Create();
        f.Trip.ExperienceMode = ExperienceMode.CuratedPremium;
        await f.Db.SaveChangesAsync();
        await f.Service.SaveSettingsAsync(f.Context, f.Trip.Id, new("GBP", null, 0, Guid.NewGuid()), default);
        var breakdown = await f.Service.BreakdownAsync(f.Context, f.Trip.Id, default);
        Assert.Equal("GBP", breakdown.Currency);
        Assert.Empty(breakdown.Categories);
        Assert.Empty(breakdown.Days);
    }

    private sealed class Rates : IExpenseRateService
    {
        public decimal? Rate = .006m;
        public Task<ExpenseRateDto?> GetAsync(string currency, string target, DateOnly date, CancellationToken ct) => Task.FromResult<ExpenseRateDto?>(
            currency == target ? new(currency, target, 1, date, "identity") : Rate.HasValue ? new(currency, target, Rate.Value, date, "test") : null);
    }
    private sealed class Fixture : IAsyncDisposable
    {
        public required TravelCompanionDbContext Db;
        public required ExpenseService Service;
        public required DefaultHttpContext Context;
        public required Trip Trip;
        public required Reservation Activity;
        public required Rates Rates;
        public SaveExpenseRequest Request() => new(1000, "JPY", new(2026, 9, 29), ExpenseCategory.Food, "Comida", null, null, 0, 0, Guid.NewGuid());
        public static async Task<Fixture> Create()
        {
            var db = new TravelCompanionDbContext(new DbContextOptionsBuilder<TravelCompanionDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
            var user = new AppUser { Id = Guid.NewGuid(), Email = "expense@test.local", DisplayName = "Test" };
            var trip = new Trip { Id = Guid.NewGuid(), AppUserId = user.Id, TravelerName = "Test", StartsOn = new(2026, 9, 28), EndsOn = new(2026, 10, 4), ExperienceMode = ExperienceMode.SelfServiceBuilder };
            var activity = new Reservation { Id = Guid.NewGuid(), TripId = trip.Id, Title = "Café", City = "Tokyo", LocationName = "", Address = "", ConfirmationCode = "", Notes = "" };
            db.AddRange(user, trip, activity); await db.SaveChangesAsync();
            var sessions = new UserSessionService(db); var (_, token) = await sessions.CreateSessionAsync(user, tripId: trip.Id, accessMode: SessionAccessMode.FreeMapPreview);
            var context = new DefaultHttpContext(); context.Request.Headers.Authorization = $"Bearer {token}"; var rates = new Rates();
            return new() { Db = db, Service = new(db, sessions, rates), Context = context, Trip = trip, Activity = activity, Rates = rates };
        }
        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }
}

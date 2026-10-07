using Microsoft.EntityFrameworkCore;
using TravelCompanion.Api.Data;
using TravelCompanion.Api.Models;
using TravelCompanion.Shared;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Api.Services;

public sealed class ExpenseService(TravelCompanionDbContext db, UserSessionService sessions, IExpenseRateService rates)
{
    private async Task<Trip> Authorize(HttpContext context, Guid tripId, CancellationToken ct)
    {
        var session = await sessions.GetSessionContextAsync(context, ct) ?? throw new UnauthorizedAccessException();
        if (session.TripId != tripId || session.User.DeletedAtUtc.HasValue
            || !await db.AppUsers.AnyAsync(x => x.Id == session.User.Id && x.DeletedAtUtc == null, ct)) throw new UnauthorizedAccessException();
        return await db.Trips.AsNoTracking().SingleOrDefaultAsync(x => x.Id == tripId && x.AppUserId == session.User.Id
            && x.PublicationStatus == TripPublicationStatus.Published, ct) ?? throw new UnauthorizedAccessException();
    }
    private async Task<bool> Premium(Trip trip, CancellationToken ct)
    {
        if (trip.ExperienceMode == ExperienceMode.CuratedPremium) return true;
        var grants = await db.BuilderAccessGrants.AsNoTracking().Where(x => x.TripId == trip.Id && x.AppUserId == trip.AppUserId).ToListAsync(ct);
        return grants.Any(x => AccessGrantPolicy.ResolveState(x, DateTimeOffset.UtcNow) == TrialAccessState.Paid);
    }
    private async Task<TripExpenseSettings> Settings(Trip trip, CancellationToken ct) =>
        await db.TripExpenseSettings.SingleOrDefaultAsync(x => x.TripId == trip.Id, ct)
        ?? new() { TripId = trip.Id, UserId = trip.AppUserId!.Value };
    private IQueryable<TripExpense> Items(Trip trip) => db.TripExpenses.Where(x => x.TripId == trip.Id && x.UserId == trip.AppUserId);
    public async Task<ExpensesDto> GetAsync(HttpContext context, Guid tripId, CancellationToken ct)
    {
        var trip = await Authorize(context, tripId, ct);
        var settings = await Settings(trip, ct);
        var items = await Items(trip).AsNoTracking().ToListAsync(ct);
        var activities = await db.Reservations.AsNoTracking().Where(x => x.TripId == tripId).ToListAsync(ct);
        return new(ToDto(settings), items.Select(ToDto).ToArray(), await Premium(trip, ct), trip.TimeZoneId,
            activities.Select(x => new ExpenseActivityDto(x.Id, x.Title, x.Date, x.Type == ReservationType.Lodging ? ExpenseCategory.Accommodation
                : x.Type == ReservationType.Flight ? ExpenseCategory.Flights : ExpenseCategory.Other)).ToArray());
    }
    public async Task<SaveExpenseResult> SaveAsync(HttpContext context, Guid tripId, Guid id, SaveExpenseRequest value, CancellationToken ct)
    {
        ExpensePolicy.Validate(value);
        if (id == Guid.Empty) throw new ArgumentException("Gasto no válido.");
        if (DbExecutionStrategy.ShouldExecute(db))
            return await DbExecutionStrategy.ExecuteAsync(db, () => SaveAsync(context, tripId, id, value, ct), ct);
        await Authorize(context, tripId, ct);
        await using var tx = db.Database.IsRelational() ? await db.Database.BeginTransactionAsync(ct) : null;
        await TripConcurrencyLock.LockAsync(db, tripId, ct);
        var trip = await Authorize(context, tripId, ct);
        var settings = await Settings(trip, ct);
        var item = await Items(trip).SingleOrDefaultAsync(x => x.Id == id, ct);
        if (item?.MutationId == value.MutationId) return new(true, ToDto(item));
        if (value.SettingsRevision != settings.Revision) return new(false, item is null ? null : ToDto(item), true);
        if ((item?.Revision ?? 0) != value.ExpectedRevision) return new(false, item is null ? null : ToDto(item));
        var activity = value.ActivityId.HasValue ? await db.Reservations.SingleOrDefaultAsync(x => x.Id == value.ActivityId && x.TripId == tripId, ct) : null;
        if (value.ActivityId.HasValue && activity is null && item?.ActivityId != value.ActivityId)
            throw new ArgumentException("La actividad ya no está disponible.");
        var preserveRate = item is not null && item.Currency == value.Currency && item.Date == value.Date && item.BaseCurrency == settings.Currency;
        var rate = value.ManualRate is { } manual ? new ExpenseRateDto(value.Currency, settings.Currency, manual, value.Date, "manual")
            : preserveRate && item!.Rate is { } saved ? new(value.Currency, settings.Currency, saved, item.RateDate!.Value, item.RateSource!)
            : value.CachedRate is { } cached && cached.BaseCurrency == settings.Currency ? cached
            : await rates.GetAsync(value.Currency, settings.Currency, value.Date, ct);
        if (value.Currency == settings.Currency) rate = new(value.Currency, settings.Currency, 1, value.Date, "identity");
        if (item is null)
        {
            if (await db.TripExpenses.AnyAsync(x => x.Id == id, ct)) throw new ArgumentException("Identificador de gasto no disponible.");
            item = new() { Id = id, TripId = tripId, UserId = trip.AppUserId!.Value };
            db.TripExpenses.Add(item);
        }
        item.Amount = value.Amount; item.Currency = value.Currency; item.Date = value.Date; item.Category = value.Category;
        item.Concept = value.Concept.Trim(); item.ActivityId = value.ActivityId;
        item.ActivityTitle = activity?.Title ?? (value.ActivityId.HasValue ? item.ActivityTitle : null);
        item.Rate = rate?.Rate; item.RateDate = rate?.Date; item.RateSource = rate?.Source; item.BaseCurrency = settings.Currency;
        item.Revision++; item.MutationId = value.MutationId; item.Deleted = value.Deleted;
        await db.SaveChangesAsync(ct);
        if (tx is not null) await tx.CommitAsync(ct);
        return new(true, ToDto(item));
    }
    public async Task<ExpenseSettingsDto?> SaveSettingsAsync(HttpContext context, Guid tripId, SaveExpenseSettingsRequest value, CancellationToken ct)
    {
        if (!ExpensePolicy.Currencies.Contains(value.Currency) || value.MutationId == Guid.Empty || value.ExpectedRevision < 0
            || value.Budget is <= 0 or > 999999999m || (value.Budget.HasValue && ExpensePolicy.Round(value.Budget.Value, value.Currency) != value.Budget))
            throw new ArgumentException("Presupuesto no válido.");
        if (DbExecutionStrategy.ShouldExecute(db))
            return await DbExecutionStrategy.ExecuteAsync(db, () => SaveSettingsAsync(context, tripId, value, ct), ct);
        await Authorize(context, tripId, ct);
        await using var tx = db.Database.IsRelational() ? await db.Database.BeginTransactionAsync(ct) : null;
        await TripConcurrencyLock.LockAsync(db, tripId, ct);
        var trip = await Authorize(context, tripId, ct);
        var settings = await Settings(trip, ct);
        if (settings.MutationId == value.MutationId) return ToDto(settings);
        if (settings.Revision != value.ExpectedRevision) return null;
        if (settings.Currency != value.Currency)
        {
            var items = await Items(trip).Where(x => !x.Deleted).ToListAsync(ct);
            var conversions = new Dictionary<Guid, ExpenseRateDto>();
            foreach (var item in items)
                conversions[item.Id] = await rates.GetAsync(item.Currency, value.Currency, item.Date, ct)
                    ?? throw new ArgumentException("Faltan cotizaciones. Conservamos la moneda y el presupuesto anteriores.");
            foreach (var item in items)
            {
                var rate = conversions[item.Id]; item.Rate = rate.Rate; item.RateDate = rate.Date;
                item.RateSource = rate.Source; item.BaseCurrency = value.Currency; item.Revision++; item.MutationId = Guid.Empty;
            }
        }
        if (settings.Revision == 0) db.TripExpenseSettings.Add(settings);
        settings.Currency = value.Currency; settings.Budget = value.Budget; settings.Revision++; settings.MutationId = value.MutationId;
        await db.SaveChangesAsync(ct);
        if (tx is not null) await tx.CommitAsync(ct);
        return ToDto(settings);
    }
    public async Task<ExpenseRateDto?> RateAsync(HttpContext context, Guid tripId, string currency, string target, DateOnly date, CancellationToken ct)
    { await Authorize(context, tripId, ct); if (date.Year < 2000 || date.Year > 2100) throw new ArgumentException("Fecha no válida."); return await rates.GetAsync(currency, target, date, ct); }
    public async Task<ExpenseBreakdownDto> BreakdownAsync(HttpContext context, Guid tripId, CancellationToken ct)
    {
        var trip = await Authorize(context, tripId, ct);
        if (!await Premium(trip, ct)) throw new ExpensePremiumException();
        var data = (await Items(trip).AsNoTracking().Where(x => !x.Deleted).ToListAsync(ct)).Select(ToDto).ToArray();
        return ExpensePolicy.Breakdown(data, (await Settings(trip, ct)).Currency);
    }
    public async Task<string> ExportAsync(HttpContext context, Guid tripId, CancellationToken ct)
    {
        var trip = await Authorize(context, tripId, ct);
        if (!await Premium(trip, ct)) throw new ExpensePremiumException();
        return ExpensePolicy.Csv((await Items(trip).AsNoTracking().ToListAsync(ct)).Select(ToDto));
    }
    private static ExpenseSettingsDto ToDto(TripExpenseSettings x) => new(x.Currency, x.Budget, x.Revision, x.MutationId);
    public static ExpenseDto ToDto(TripExpense x) => new(x.Id, x.TripId, x.Amount, x.Currency, x.Date, x.Category, x.Concept,
        x.ActivityId, x.ActivityTitle, x.Rate, x.RateDate, x.RateSource, x.BaseCurrency, x.Revision, x.Deleted, x.MutationId);
}
public sealed class ExpensePremiumException : Exception;

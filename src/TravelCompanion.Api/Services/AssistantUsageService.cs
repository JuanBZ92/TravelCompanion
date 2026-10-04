using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TravelCompanion.Api.Data;
using TravelCompanion.Api.Models;
using TravelCompanion.Api.Options;
using TravelCompanion.Shared;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Api.Services;

public sealed record AssistantUsageLeaseResult(Guid LeaseId, int RemainingAfterReservation, bool IsTrial);

public sealed class AssistantUsageService(
    TravelCompanionDbContext dbContext,
    IOptions<FreePreviewOptions> freeOptions,
    IOptions<StorePurchaseOptions> paidOptions)
{
    public const string FullDayPrefix = "full-day:";
    public const string PersonalizedDayPrefix = "full-day:personalized:";
    public const string PlannerPrefix = "full-day:plan-days:";

    public async Task<AssistantUsageLeaseResult> ReserveAsync(Guid userId, Guid? tripId, string operationKey, CancellationToken ct,
        string? requestHash = null)
    {
        if (DbExecutionStrategy.ShouldExecute(dbContext))
            return await DbExecutionStrategy.ExecuteAsync(dbContext,
                () => ReserveAsync(userId, tripId, operationKey, ct, requestHash), ct);
        var now = DateTimeOffset.UtcNow;
        // The grant lock serializes planner reservations. ReadCommitted sees the lease inserted
        // by a worker that committed while this request was waiting for that lock.
        var isolation = operationKey.StartsWith(PlannerPrefix, StringComparison.Ordinal)
            ? IsolationLevel.ReadCommitted : IsolationLevel.Serializable;
        await using var transaction = dbContext.Database.IsRelational() && dbContext.Database.CurrentTransaction is null
            ? await dbContext.Database.BeginTransactionAsync(isolation, ct) : null;
        var grant = await dbContext.BuilderAccessGrants.Where(item => item.AppUserId == userId
                && (tripId == null || item.TripId == tripId)
                && item.RevokedAtUtc == null
                && (item.IsTrial || item.Status == BuilderAccessStatus.Active)
                && (!item.ExpiresAtUtc.HasValue || item.ExpiresAtUtc > now))
            .OrderByDescending(item => item.CreatedAtUtc).FirstOrDefaultAsync(ct)
            ?? throw new TrialUpgradeRequiredException(new(false, TravelCompanion.Shared.Dtos.TrialAccessState.NoAccess, null, null, 0,
                freeOptions.Value.PassPrice, freeOptions.Value.Currency, freeOptions.Value.PurchaseUrl));
        var personalizedTrial = grant.IsTrial && operationKey.StartsWith(PersonalizedDayPrefix, StringComparison.Ordinal);
        if (personalizedTrial) await LockUserAsync(userId, ct);
        await LockGrantAsync(grant.Id, ct);
        await dbContext.Entry(grant).ReloadAsync(ct);
        var prior = await dbContext.AssistantUsageLeases.FirstOrDefaultAsync(item =>
            item.BuilderAccessGrantId == grant.Id && item.OperationKey == operationKey, ct);
        if (prior is not null && requestHash is not null && prior.RequestHash != requestHash)
            throw new DayPlanException(409, "operation", "Esta operación ya se utilizó con otros parámetros.");
        if (prior?.CompletedAtUtc is not null || prior is { CancelledAtUtc: null } && prior.ExpiresAtUtc > now)
        {
            var existingRemaining = await RemainingAsync(grant, now, operationKey.StartsWith(FullDayPrefix, StringComparison.Ordinal), ct);
            if (transaction is not null) await transaction.CommitAsync(ct);
            return new(prior!.Id, existingRemaining, grant.IsTrial);
        }
        var fullDay = operationKey.StartsWith(FullDayPrefix, StringComparison.Ordinal);
        var limit = grant.IsTrial ? (fullDay ? FreePlanningPolicy.MaximumDayImprovements : Math.Clamp(freeOptions.Value.AssistantRequestLimit, 0, 3))
            : Math.Clamp(paidOptions.Value.DailyAssistantLimit, 1, 100);
        var used = grant.IsTrial ? (fullDay
            ? await dbContext.AssistantUsageLeases.CountAsync(item => item.BuilderAccessGrantId == grant.Id && item.OperationKey.StartsWith(FullDayPrefix) && item.CompletedAtUtc != null, ct)
            : grant.TrialAssistantRequestsUsed)
            : (await dbContext.AssistantDailyUsages.AsNoTracking().FirstOrDefaultAsync(item => item.BuilderAccessGrantId == grant.Id && item.UtcDate == DateOnly.FromDateTime(now.UtcDateTime), ct))?.SuccessfulRequests ?? 0;
        var reserved = await dbContext.AssistantUsageLeases.CountAsync(item => item.BuilderAccessGrantId == grant.Id
            && (grant.IsTrial ? item.OperationKey.StartsWith(FullDayPrefix) == fullDay : item.UtcDate == DateOnly.FromDateTime(now.UtcDateTime)) && item.CompletedAtUtc == null && item.CancelledAtUtc == null && item.ExpiresAtUtc > now, ct);
        if (used + reserved >= limit)
        {
            var dayUsed = fullDay ? used : await dbContext.AssistantUsageLeases.CountAsync(item =>
                item.BuilderAccessGrantId == grant.Id && item.OperationKey.StartsWith(FullDayPrefix) && item.CompletedAtUtc != null, ct);
            throw new TrialUpgradeRequiredException(new(grant.IsTrial, AccessGrantPolicy.ResolveState(grant, now),
                grant.TrialEditingExpiresAtUtc, grant.TrialDraftExpiresAtUtc,
                fullDay ? Math.Max(0, Math.Clamp(freeOptions.Value.AssistantRequestLimit, 0, 3) - grant.TrialAssistantRequestsUsed) : 0,
                freeOptions.Value.PassPrice, freeOptions.Value.Currency, freeOptions.Value.PurchaseUrl)
                { FreePolicy = grant.FreePolicy, DayImprovementsRemaining = Math.Max(0, FreePlanningPolicy.MaximumDayImprovements - dayUsed) });
        }
        if (personalizedTrial)
        {
            var alreadyUsed = await dbContext.AppUsers.AsNoTracking().AnyAsync(item =>
                item.Id == userId && item.PersonalizedDayTrialUsedAtUtc != null, ct);
            var pending = await dbContext.AssistantUsageLeases.AsNoTracking().AnyAsync(item =>
                item.BuilderAccessGrant!.AppUserId == userId
                && item.OperationKey.StartsWith(PersonalizedDayPrefix)
                && item.CompletedAtUtc == null && item.CancelledAtUtc == null && item.ExpiresAtUtc > now, ct);
            if (alreadyUsed || pending)
                throw new TrialUpgradeRequiredException(new(true, AccessGrantPolicy.ResolveState(grant, now),
                    grant.TrialEditingExpiresAtUtc, grant.TrialDraftExpiresAtUtc, 0,
                    freeOptions.Value.PassPrice, freeOptions.Value.Currency, freeOptions.Value.PurchaseUrl)
                    { FreePolicy = grant.FreePolicy, PersonalizedDayTrialAvailable = false });
        }
        var lease = prior ?? new AssistantUsageLease
        {
            Id = Guid.NewGuid(), BuilderAccessGrantId = grant.Id, OperationKey = operationKey
        };
        lease.UtcDate = DateOnly.FromDateTime(now.UtcDateTime);
        lease.CreatedAtUtc = now;
        lease.ExpiresAtUtc = now.AddMinutes(10);
        lease.CancelledAtUtc = null;
        lease.RequestHash = requestHash;
        if (prior is null) dbContext.AssistantUsageLeases.Add(lease);
        await dbContext.SaveChangesAsync(ct);
        if (transaction is not null) await transaction.CommitAsync(ct);
        return new(lease.Id, Math.Max(0, limit - used - reserved - 1), grant.IsTrial);
    }

    public async Task CompleteAsync(Guid leaseId, CancellationToken ct, string? responseJson = null)
    {
        if (DbExecutionStrategy.ShouldExecute(dbContext))
        {
            await DbExecutionStrategy.ExecuteAsync(dbContext, () => CompleteAsync(leaseId, ct, responseJson), ct);
            return;
        }
        var now = DateTimeOffset.UtcNow;
        await using var transaction = dbContext.Database.IsRelational() && dbContext.Database.CurrentTransaction is null
            ? await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct) : null;
        var lease = await dbContext.AssistantUsageLeases.Include(item => item.BuilderAccessGrant).SingleAsync(item => item.Id == leaseId, ct);
        if (lease.CompletedAtUtc.HasValue || lease.CancelledAtUtc.HasValue) return;
        var grant = lease.BuilderAccessGrant!;
        if (grant.IsTrial && lease.OperationKey.StartsWith(PersonalizedDayPrefix, StringComparison.Ordinal))
            await LockUserAsync(grant.AppUserId, ct);
        await LockGrantAsync(grant.Id, ct);
        await dbContext.Entry(grant).ReloadAsync(ct);
        await dbContext.Entry(lease).ReloadAsync(ct);
        if (lease.CompletedAtUtc.HasValue || lease.CancelledAtUtc.HasValue) return;
        if (lease.ExpiresAtUtc <= now)
        {
            lease.CancelledAtUtc = now;
            await dbContext.SaveChangesAsync(ct);
            if (transaction is not null) await transaction.CommitAsync(ct);
            throw new InvalidOperationException("La reserva de cuota caducó; vuelve a intentar la operación.");
        }
        if (responseJson is not null) lease.ResponseJson = responseJson;
        if (grant.IsTrial)
        {
            if (!lease.OperationKey.StartsWith(FullDayPrefix, StringComparison.Ordinal)) grant.TrialAssistantRequestsUsed++;
            if (lease.OperationKey.StartsWith(PersonalizedDayPrefix, StringComparison.Ordinal))
            {
                var user = await dbContext.AppUsers.SingleAsync(item => item.Id == grant.AppUserId, ct);
                if (user.PersonalizedDayTrialUsedAtUtc.HasValue)
                    throw new InvalidOperationException("La prueba personalizada ya se utilizó.");
                user.PersonalizedDayTrialUsedAtUtc = now;
            }
        }
        else
        {
            var usage = await dbContext.AssistantDailyUsages.SingleOrDefaultAsync(item =>
                item.BuilderAccessGrantId == grant.Id && item.UtcDate == lease.UtcDate, ct);
            if (usage is null)
            {
                usage = new AssistantDailyUsage { Id = Guid.NewGuid(), BuilderAccessGrantId = grant.Id, UtcDate = lease.UtcDate };
                dbContext.AssistantDailyUsages.Add(usage);
            }
            usage.SuccessfulRequests++;
        }
        lease.CompletedAtUtc = now;
        await dbContext.SaveChangesAsync(ct);
        if (transaction is not null) await transaction.CommitAsync(ct);
    }

    public async Task CancelAsync(Guid leaseId, CancellationToken ct)
    {
        if (dbContext.Database.IsRelational())
        {
            await dbContext.AssistantUsageLeases.Where(item => item.Id == leaseId
                && item.CompletedAtUtc == null && item.CancelledAtUtc == null)
                .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.CancelledAtUtc, DateTimeOffset.UtcNow), ct);
            return;
        }
        var lease = await dbContext.AssistantUsageLeases.FirstOrDefaultAsync(item => item.Id == leaseId, ct);
        if (lease is null || lease.CompletedAtUtc.HasValue) return;
        lease.CancelledAtUtc ??= DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(ct);
    }

    // The canonical payload and the quota consumption commit together. A lost response can be replayed.
    public async Task<string> CompletePlanAsync(Guid leaseId, Guid tripId, int expectedRevision,
        string responseJson, CancellationToken ct, bool consume = true)
    {
        if (DbExecutionStrategy.ShouldExecute(dbContext))
            return await DbExecutionStrategy.ExecuteAsync(dbContext,
                () => CompletePlanAsync(leaseId, tripId, expectedRevision, responseJson, ct, consume), ct);
        await using var transaction = dbContext.Database.IsRelational()
            ? await dbContext.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct) : null;
        var lease = await dbContext.AssistantUsageLeases.Include(item => item.BuilderAccessGrant).SingleAsync(item => item.Id == leaseId, ct);
        await LockGrantAsync(lease.BuilderAccessGrantId, ct);
        await dbContext.Entry(lease).ReloadAsync(ct);
        if (lease.ResponseJson is not null)
        {
            if (transaction is not null) await transaction.CommitAsync(ct);
            return lease.ResponseJson;
        }
        await dbContext.Entry(lease.BuilderAccessGrant!).ReloadAsync(ct);
        var grant = lease.BuilderAccessGrant!;
        if (grant.TripId != tripId || AccessGrantPolicy.ResolveState(grant, DateTimeOffset.UtcNow)
            is not (TrialAccessState.Editing or TrialAccessState.Paid))
            throw new DayPlanException(403, "upgrade", "El acceso al viaje cambió. Actualizá la sesión para continuar.");
        await TripConcurrencyLock.LockAsync(dbContext, tripId, ct);
        var current = await dbContext.Trips.AsNoTracking().FirstOrDefaultAsync(item => item.Id == tripId
            && item.AppUserId == lease.BuilderAccessGrant!.AppUserId && !item.IsArchived, ct);
        if (current is null || current.PlanRevision != expectedRevision)
            throw new DayPlanException(409, "stale", "El itinerario cambió. Actualizá el viaje antes de generar otra propuesta.");
        if (lease.CancelledAtUtc.HasValue || lease.ExpiresAtUtc <= DateTimeOffset.UtcNow)
            throw new DayPlanException(409, "operation", "La generación caducó. Volvé a intentarlo.");
        if (consume) await CompleteAsync(leaseId, ct, responseJson);
        else
        {
            lease.ResponseJson = responseJson;
            lease.CancelledAtUtc = DateTimeOffset.UtcNow;
            await dbContext.SaveChangesAsync(ct);
        }
        if (transaction is not null) await transaction.CommitAsync(ct);
        return responseJson;
    }

    private async Task<int> RemainingAsync(BuilderAccessGrant grant, DateTimeOffset now, bool fullDay, CancellationToken ct)
    {
        var limit = grant.IsTrial ? (fullDay ? FreePlanningPolicy.MaximumDayImprovements : Math.Clamp(freeOptions.Value.AssistantRequestLimit, 0, 3)) : paidOptions.Value.DailyAssistantLimit;
        var used = grant.IsTrial ? (fullDay
            ? await dbContext.AssistantUsageLeases.CountAsync(item => item.BuilderAccessGrantId == grant.Id && item.OperationKey.StartsWith(FullDayPrefix) && item.CompletedAtUtc != null, ct)
            : grant.TrialAssistantRequestsUsed)
            : (await dbContext.AssistantDailyUsages.AsNoTracking().FirstOrDefaultAsync(item => item.BuilderAccessGrantId == grant.Id && item.UtcDate == DateOnly.FromDateTime(now.UtcDateTime), ct))?.SuccessfulRequests ?? 0;
        return Math.Max(0, limit - used);
    }

    private async Task LockGrantAsync(Guid grantId, CancellationToken ct)
    {
        if (dbContext.Database.ProviderName?.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) != true
            || dbContext.Database.CurrentTransaction is null)
            return;
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT 1 FROM \"BuilderAccessGrants\" WHERE \"Id\" = {grantId} FOR UPDATE", ct);
    }

    private async Task LockUserAsync(Guid userId, CancellationToken ct)
    {
        if (dbContext.Database.ProviderName?.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) != true
            || dbContext.Database.CurrentTransaction is null) return;
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT 1 FROM \"AppUsers\" WHERE \"Id\" = {userId} FOR UPDATE", ct);
    }
}

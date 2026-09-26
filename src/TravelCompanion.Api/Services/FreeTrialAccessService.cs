using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TravelCompanion.Api.Data;
using TravelCompanion.Api.Models;
using TravelCompanion.Api.Options;
using TravelCompanion.Shared;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Api.Services;

public sealed class FreeTrialAccessService(
    TravelCompanionDbContext dbContext,
    IOptions<FreePreviewOptions> options,
    ILogger<FreeTrialAccessService> logger,
    ProductAnalyticsService? analytics = null)
{
    public async Task RequirePlanningDateAsync(Guid userId, Guid? tripId, DateOnly date, CancellationToken ct)
    {
        var trip = await dbContext.Trips.AsNoTracking().FirstOrDefaultAsync(t => t.Id == tripId && t.AppUserId == userId, ct);
        if (trip is null || !FreePlanningPolicy.CanPlanDate(trip.StartsOn, date))
            throw new TrialUpgradeRequiredException(await GetStatusAsync(userId, ct));
    }

    public async Task<BuilderAccessGrant?> GetGrantAsync(Guid userId, CancellationToken cancellationToken = default) =>
        await dbContext.BuilderAccessGrants
            .Include(grant => grant.Destination)
            .Where(grant => grant.AppUserId == userId
                && grant.IsTrial
                && grant.Status == BuilderAccessStatus.Active
                && grant.RevokedAtUtc == null)
            .OrderByDescending(grant => grant.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

    public TrialAccessStatusDto ToStatus(BuilderAccessGrant? grant, DateTimeOffset? now = null)
    {
        var current = now ?? DateTimeOffset.UtcNow;
        var state = AccessGrantPolicy.ResolveState(grant, current);
        var limit = Math.Clamp(options.Value.AssistantRequestLimit, 0, 3);
        return new TrialAccessStatusDto(
            grant?.IsTrial == true && grant.ConvertedAtUtc is null,
            state,
            grant?.TrialEditingExpiresAtUtc,
            grant?.TrialDraftExpiresAtUtc,
            state == TrialAccessState.Paid ? int.MaxValue : Math.Max(0, limit - (grant?.TrialAssistantRequestsUsed ?? 0)),
            options.Value.PassPrice,
            string.IsNullOrWhiteSpace(options.Value.Currency) ? "EUR" : options.Value.Currency.Trim().ToUpperInvariant(),
            options.Value.PurchaseUrl) { FreePolicy = grant?.FreePolicy ?? FreeAccessPolicy.TimedTrial };
    }

    public async Task<TrialAccessStatusDto> GetStatusAsync(Guid userId, CancellationToken cancellationToken = default)
        => await GetStatusAsync(await GetGrantAsync(userId, cancellationToken), cancellationToken);

    public async Task<TrialAccessStatusDto> GetStatusAsync(BuilderAccessGrant? grant, CancellationToken cancellationToken = default)
    {
        var used = grant is null ? 0 : await dbContext.AssistantUsageLeases.CountAsync(item =>
            item.BuilderAccessGrantId == grant.Id && item.OperationKey.StartsWith(AssistantUsageService.FullDayPrefix)
            && item.CompletedAtUtc != null, cancellationToken);
        return ToStatus(grant) with { DayImprovementsRemaining = Math.Max(0, FreePlanningPolicy.MaximumDayImprovements - used) };
    }

    public async Task<TrialAccessStatusDto> StartEditingAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var grant = await GetGrantAsync(userId, cancellationToken)
            ?? throw new InvalidOperationException("No free trial access was found.");
        if (!grant.TrialEditingStartedAtUtc.HasValue)
        {
            var now = DateTimeOffset.UtcNow;
            var editingMinutes = Math.Clamp(options.Value.TrialEditingMinutes, 5, 180);
            var retentionDays = Math.Clamp(options.Value.DraftRetentionDays, 1, 30);
            grant.TrialEditingStartedAtUtc = now;
            grant.TrialEditingExpiresAtUtc = grant.FreePolicy == FreeAccessPolicy.PersistentFree ? null : now.AddMinutes(editingMinutes);
            grant.TrialDraftExpiresAtUtc = grant.TrialEditingExpiresAtUtc?.AddDays(retentionDays);
            await dbContext.SaveChangesAsync(cancellationToken);
            if (analytics is not null)
                await analytics.RecordServerEventAsync(userId, grant.TripId, "trial_started", "editing", null, cancellationToken);
            logger.LogInformation(
                "Free trial started. UserId={UserId}; EditingExpiresAtUtc={EditingExpiresAtUtc}; DraftExpiresAtUtc={DraftExpiresAtUtc}.",
                userId,
                grant.TrialEditingExpiresAtUtc,
                grant.TrialDraftExpiresAtUtc);
        }

        return await GetStatusAsync(userId, cancellationToken);
    }

    public async Task<TrialAccessStatusDto> RequireEditingAsync(Guid userId, bool startIfNeeded, CancellationToken cancellationToken = default)
    {
        var grant = await GetGrantAsync(userId, cancellationToken);
        var status = startIfNeeded && grant?.TrialEditingStartedAtUtc is null
            ? await StartEditingAsync(userId, cancellationToken)
            : await GetStatusAsync(userId, cancellationToken);
        if (!status.CanEdit)
        {
            logger.LogInformation("Free trial edit paywall reached. UserId={UserId}; State={TrialState}.", userId, status.State);
            throw new TrialUpgradeRequiredException(status);
        }

        return status;
    }

    public async Task<IReadOnlyList<Recommendation>> FilterToFreeRadiusAsync(
        IEnumerable<Recommendation> recommendations,
        Guid destinationId,
        CancellationToken cancellationToken = default)
    {
        var cities = await dbContext.FreeMapCities.AsNoTracking()
            .Where(city => city.IsEnabled && city.DestinationId == destinationId)
            .ToListAsync(cancellationToken);
        if (cities.Count == 0)
        {
            return [];
        }

        return recommendations
            .Where(recommendation => recommendation.AccessLevel == ContentAccessLevel.Free
                && cities.Any(city =>
                    FreeMapPreviewService.CalculateDistanceKm(
                        city.CenterLatitude,
                        city.CenterLongitude,
                        recommendation.Latitude,
                        recommendation.Longitude) <= city.FreeRadiusKm))
            .ToList();
    }

    public async Task<bool> IsRecommendationInFreeRadiusAsync(
        Recommendation recommendation,
        CancellationToken cancellationToken = default) =>
        (await FilterToFreeRadiusAsync([recommendation], recommendation.DestinationId, cancellationToken)).Count == 1;
}

public sealed class TrialUpgradeRequiredException(TrialAccessStatusDto status)
    : Exception("Activa el pase para conservar y seguir editando tu viaje.")
{
    public TrialAccessStatusDto Status { get; } = status;
}

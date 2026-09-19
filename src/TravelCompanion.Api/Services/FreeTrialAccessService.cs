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
    ILogger<FreeTrialAccessService> logger)
{
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
        var state = grant switch
        {
            null => TrialAccessState.Paid,
            { IsTrial: false } => TrialAccessState.Paid,
            { ConvertedAtUtc: not null } => TrialAccessState.Paid,
            { TrialEditingStartedAtUtc: null } => TrialAccessState.NotStarted,
            { TrialEditingExpiresAtUtc: { } editExpiry } when editExpiry > current => TrialAccessState.Editing,
            { TrialDraftExpiresAtUtc: { } draftExpiry } when draftExpiry > current => TrialAccessState.ReadOnly,
            _ => TrialAccessState.Expired
        };
        var limit = Math.Clamp(options.Value.AssistantRequestLimit, 0, 20);
        return new TrialAccessStatusDto(
            grant?.IsTrial == true && grant.ConvertedAtUtc is null,
            state,
            grant?.TrialEditingExpiresAtUtc,
            grant?.TrialDraftExpiresAtUtc,
            state == TrialAccessState.Paid ? int.MaxValue : Math.Max(0, limit - (grant?.TrialAssistantRequestsUsed ?? 0)),
            options.Value.PassPrice,
            string.IsNullOrWhiteSpace(options.Value.Currency) ? "EUR" : options.Value.Currency.Trim().ToUpperInvariant(),
            options.Value.PurchaseUrl);
    }

    public async Task<TrialAccessStatusDto> GetStatusAsync(Guid userId, CancellationToken cancellationToken = default) =>
        ToStatus(await GetGrantAsync(userId, cancellationToken));

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
            grant.TrialEditingExpiresAtUtc = now.AddMinutes(editingMinutes);
            grant.TrialDraftExpiresAtUtc = grant.TrialEditingExpiresAtUtc.Value.AddDays(retentionDays);
            await dbContext.SaveChangesAsync(cancellationToken);
            logger.LogInformation(
                "Free trial started. UserId={UserId}; EditingExpiresAtUtc={EditingExpiresAtUtc}; DraftExpiresAtUtc={DraftExpiresAtUtc}.",
                userId,
                grant.TrialEditingExpiresAtUtc,
                grant.TrialDraftExpiresAtUtc);
        }

        return ToStatus(grant);
    }

    public async Task<TrialAccessStatusDto> RequireEditingAsync(Guid userId, bool startIfNeeded, CancellationToken cancellationToken = default)
    {
        var grant = await GetGrantAsync(userId, cancellationToken);
        var status = startIfNeeded && grant?.TrialEditingStartedAtUtc is null
            ? await StartEditingAsync(userId, cancellationToken)
            : ToStatus(grant);
        if (!status.CanEdit)
        {
            logger.LogInformation("Free trial edit paywall reached. UserId={UserId}; State={TrialState}.", userId, status.State);
            throw new TrialUpgradeRequiredException(status);
        }

        return status;
    }

    public async Task<TrialAccessStatusDto> RequireAssistantQuotaAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var status = await GetStatusAsync(userId, cancellationToken);
        if (!status.CanUseAssistant)
        {
            logger.LogInformation("Free trial assistant paywall reached. UserId={UserId}.", userId);
            throw new TrialUpgradeRequiredException(status);
        }

        return status;
    }

    public async Task<TrialAccessStatusDto> RecordSuccessfulAssistantRequestAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var grant = await GetGrantAsync(userId, cancellationToken);
        if (grant is null || !grant.IsTrial || grant.ConvertedAtUtc.HasValue)
        {
            return ToStatus(grant);
        }

        var limit = Math.Clamp(options.Value.AssistantRequestLimit, 0, 20);
        if (grant.TrialAssistantRequestsUsed < limit)
        {
            grant.TrialAssistantRequestsUsed++;
            await dbContext.SaveChangesAsync(cancellationToken);
            logger.LogInformation(
                "Free trial assistant request consumed. UserId={UserId}; Used={Used}; Limit={Limit}.",
                userId,
                grant.TrialAssistantRequestsUsed,
                limit);
        }

        return ToStatus(grant);
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

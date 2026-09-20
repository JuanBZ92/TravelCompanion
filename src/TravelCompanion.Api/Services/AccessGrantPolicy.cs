using TravelCompanion.Api.Models;
using TravelCompanion.Shared;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Api.Services;

public static class AccessGrantPolicy
{
    public static TrialAccessState ResolveState(BuilderAccessGrant? grant, DateTimeOffset now)
    {
        if (grant is null) return TrialAccessState.NoAccess;
        if (grant.Status == BuilderAccessStatus.PurchasePending) return TrialAccessState.PurchasePending;
        if (grant.Status == BuilderAccessStatus.Revoked || grant.RevokedAtUtc.HasValue) return TrialAccessState.Revoked;

        if (grant.IsTrial)
        {
            if (grant.TrialDraftExpiresAtUtc.HasValue && grant.TrialDraftExpiresAtUtc <= now)
                return TrialAccessState.Expired;
            if (!grant.TrialEditingStartedAtUtc.HasValue) return TrialAccessState.NotStarted;
            if (grant.TrialEditingExpiresAtUtc.HasValue && grant.TrialEditingExpiresAtUtc <= now)
                return TrialAccessState.ReadOnly;
            return TrialAccessState.Editing;
        }

        if (grant.Status != BuilderAccessStatus.Active
            || grant.ExpiresAtUtc.HasValue && grant.ExpiresAtUtc <= now)
            return TrialAccessState.Expired;
        return TrialAccessState.Paid;
    }

    public static SessionAccessMode ResolveSessionMode(BuilderAccessGrant? grant, DateTimeOffset now) =>
        ResolveState(grant, now) switch
        {
            TrialAccessState.Paid => SessionAccessMode.Builder,
            TrialAccessState.NotStarted or TrialAccessState.Editing or TrialAccessState.ReadOnly => SessionAccessMode.FreeMapPreview,
            _ when grant is { IsTrial: false } => SessionAccessMode.BuilderReadOnly,
            _ => SessionAccessMode.FreeMapPreview
        };
}

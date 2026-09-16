using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TravelCompanion.Api.Data;
using TravelCompanion.Api.Models;

namespace TravelCompanion.Api.Services;

public static class AccessPinAvailability
{
    public static async Task<bool> IsAvailableAsync(TravelCompanionDbContext db, string pin,
        Guid? exceptTripId = null, Guid? exceptGrantId = null, CancellationToken cancellationToken = default)
    {
        if (pin == "0000") return false;
        var tripHasher = new PasswordHasher<Trip>();
        var grantHasher = new PasswordHasher<BuilderAccessGrant>();
        var trips = await db.Trips.AsNoTracking().Include(t => t.PlanDraft)
            .Where(t => !exceptTripId.HasValue || t.Id != exceptTripId).ToListAsync(cancellationToken);
        if (trips.Any(t => Matches(t.AccessPinHash) || Matches(t.PlanDraft?.PendingAccessPinHash))) return false;
        var grants = await db.BuilderAccessGrants.AsNoTracking()
            .Where(g => !exceptGrantId.HasValue || g.Id != exceptGrantId).ToListAsync(cancellationToken);
        return grants.All(g => string.IsNullOrWhiteSpace(g.PinHash) || grantHasher.VerifyHashedPassword(g, g.PinHash, pin) == PasswordVerificationResult.Failed);

        bool Matches(string? hash) => !string.IsNullOrWhiteSpace(hash)
            && tripHasher.VerifyHashedPassword(null!, hash, pin) != PasswordVerificationResult.Failed;
    }
}

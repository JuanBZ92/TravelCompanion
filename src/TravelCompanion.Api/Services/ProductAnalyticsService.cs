using Microsoft.EntityFrameworkCore;
using TravelCompanion.Api.Data;
using TravelCompanion.Api.Models;
using TravelCompanion.Shared.Dtos;
using Microsoft.Extensions.Options;
using TravelCompanion.Api.Options;
using TravelCompanion.Shared;

namespace TravelCompanion.Api.Services;

public sealed class ProductAnalyticsService(TravelCompanionDbContext dbContext, IOptions<ProductFeatureOptions>? features = null)
{
    private static readonly HashSet<string> BusinessEvents = new(StringComparer.Ordinal)
    {
        "checkout_started", "checkout_pending", "checkout_cancelled", "checkout_failed",
        "purchase_verified", "pass_activated", "purchase_restored", "refund", "duplicate_trip_purchase"
    };
    private static readonly HashSet<string> ClientEvents = new(StringComparer.Ordinal)
    {
        "paywall_shown", "paywall_cta_selected", "email_verification_started",
        "day_review_viewed", "proposal_previewed", "route_viewed", "trip_created",
        "limit_reached", "paid_trip_opened", "offline_download_completed", "offline_download_failed"
    };

    public async Task<int> IngestAsync(HttpContext httpContext, ProductAnalyticsBatchDto batch,
        UserSessionService sessions, CancellationToken cancellationToken)
    {
        var session = await sessions.GetSessionContextAsync(httpContext, cancellationToken)
            ?? throw new UnauthorizedAccessException();
        if (features?.Value.AnalyticsEnabled == false) return 0;
        var events = batch.Events.Take(100)
            .Where(item => session.User.BehaviorAnalyticsConsent && item.BehaviorConsent && ClientEvents.Contains(item.Name)
                && item.SchemaVersion is >= 1 and <= 2
                && item.OccurredAtUtc >= DateTimeOffset.UtcNow.AddDays(-90)
                && item.OccurredAtUtc <= DateTimeOffset.UtcNow.AddMinutes(5))
            .GroupBy(item => item.EventId).Select(group => group.First()).ToList();
        if (events.Count == 0) return 0;
        var ids = events.Select(item => item.EventId).ToList();
        var existing = await dbContext.ProductAnalyticsEvents.AsNoTracking()
            .Where(item => ids.Contains(item.EventId)).Select(item => item.EventId).ToListAsync(cancellationToken);
        var existingSet = existing.ToHashSet();
        var isAnonymous = session.User.Email.StartsWith(FreePreviewAccountService.AccountEmailPrefix, StringComparison.OrdinalIgnoreCase)
            || session.User.Email == FreePreviewAccountService.AccountEmail;
        var ownedTripIds = await dbContext.Trips.AsNoTracking().Where(item => item.AppUserId == session.User.Id)
            .Select(item => item.Id).ToListAsync(cancellationToken);
        foreach (var item in events.Where(item => !existingSet.Contains(item.EventId)))
        {
            var tripId = item.TripId.HasValue && ownedTripIds.Contains(item.TripId.Value) ? item.TripId : session.TripId;
            dbContext.ProductAnalyticsEvents.Add(new ProductAnalyticsEvent
            {
                Id = Guid.NewGuid(), EventId = item.EventId,
                AppUserId = isAnonymous ? null : session.User.Id,
                AnonymousUserId = isAnonymous ? session.User.Id : null,
                TripId = item.TripId.HasValue && ownedTripIds.Contains(item.TripId.Value) ? item.TripId : session.TripId,
                Name = item.Name, OccurredAtUtc = item.OccurredAtUtc,
                ReceivedAtUtc = DateTimeOffset.UtcNow, Source = item.Source, AppVersion = item.AppVersion,
                Platform = item.Platform, PaywallVariant = item.PaywallVariant,
                FreePolicyVariant = await ResolveFreePolicyAsync(session.User.Id, tripId, cancellationToken),
                AccessState = session.AccessMode.ToString(), BehaviorConsent = true,
                IsBusinessEvent = false, SchemaVersion = item.SchemaVersion
            });
        }
        return await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task RecordServerEventAsync(Guid userId, Guid? tripId, string name, string? source,
        string? variant, CancellationToken cancellationToken, bool sandbox = false,
        string? platform = null, string? appVersion = null, Guid? eventId = null)
    {
        var isBusiness = BusinessEvents.Contains(name);
        if (!isBusiness && features?.Value.AnalyticsEnabled == false) return;
        var user = await dbContext.AppUsers.AsNoTracking().SingleAsync(item => item.Id == userId, cancellationToken);
        if (!isBusiness && !user.BehaviorAnalyticsConsent) return;
        var accessState = await ResolveAccessStateAsync(userId, tripId, cancellationToken);
        dbContext.ProductAnalyticsEvents.Add(new ProductAnalyticsEvent
        {
            Id = Guid.NewGuid(), EventId = eventId ?? Guid.NewGuid(), AppUserId = userId, TripId = tripId,
            Name = name, OccurredAtUtc = DateTimeOffset.UtcNow, ReceivedAtUtc = DateTimeOffset.UtcNow,
            Source = source, PaywallVariant = variant, Platform = platform, AppVersion = appVersion, AccessState = accessState,
            FreePolicyVariant = await ResolveFreePolicyAsync(userId, tripId, cancellationToken),
            BehaviorConsent = !isBusiness, IsBusinessEvent = isBusiness, IsSandbox = sandbox, SchemaVersion = 1
        });
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task<string> ResolveAccessStateAsync(Guid userId, Guid? tripId, CancellationToken cancellationToken)
    {
        if (!tripId.HasValue) return "Account";
        var grant = await dbContext.BuilderAccessGrants.AsNoTracking().FirstOrDefaultAsync(item =>
            item.AppUserId == userId && item.TripId == tripId, cancellationToken);
        return AccessGrantPolicy.ResolveState(grant, DateTimeOffset.UtcNow).ToString();
    }

    private async Task<string?> ResolveFreePolicyAsync(Guid userId, Guid? tripId, CancellationToken ct)
    {
        var grant = await dbContext.BuilderAccessGrants.AsNoTracking()
            .Where(item => item.AppUserId == userId && (tripId == null || item.TripId == tripId))
            .OrderByDescending(item => item.CreatedAtUtc).FirstOrDefaultAsync(ct);
        return grant is not null && (grant.IsTrial || grant.TrialEditingStartedAtUtc.HasValue
            || grant.FreePolicy == TravelCompanion.Shared.FreeAccessPolicy.PersistentFree)
            ? grant.FreePolicy.ToString() : null;
    }
}

using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using TravelCompanion.Api.Data;
using TravelCompanion.Api.Models;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Api.Pages.Admin;

public sealed class ConversionModel(TravelCompanionDbContext dbContext) : PageModel
{
    public DateOnly From { get; private set; }
    public DateOnly To { get; private set; }
    public IReadOnlyList<StageRow> Stages { get; private set; } = [];
    public IReadOnlyList<SignalRow> Signals { get; private set; } = [];
    public IReadOnlyList<RevenueRow> Revenue { get; private set; } = [];
    public int PurchasesWithoutVerifiedAmount { get; private set; }
    public int PendingPurchases { get; private set; }
    public int Refunds { get; private set; }
    public int Restores { get; private set; }
    public int FailedCheckouts { get; private set; }
    public int ActivationFailures { get; private set; }
    public double ConsentCoveragePercent { get; private set; }
    public double? MedianHoursToPurchase { get; private set; }
    public double? SevenDayConversionPercent { get; private set; }
    public double? ThirtyDayConversionPercent { get; private set; }
    public int SevenDayCohortSize { get; private set; }
    public int ThirtyDayCohortSize { get; private set; }
    public bool SevenDayCohortIncomplete { get; private set; }
    public bool ThirtyDayCohortIncomplete { get; private set; }
    public IReadOnlyList<DuplicatePurchaseRow> DuplicatePurchases { get; private set; } = [];

    public async Task OnGetAsync(DateOnly? from, DateOnly? to, string? platform, string? appVersion,
        string? entry, string? variant, string? freePolicy = null)
    {
        To = to ?? DateOnly.FromDateTime(DateTime.UtcNow);
        From = from ?? To.AddDays(-30);
        if (To < From) (From, To) = (To, From);
        var start = new DateTimeOffset(From.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var end = new DateTimeOffset(To.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var now = DateTimeOffset.UtcNow;
        var excludedUserIds = await dbContext.AppUsers.AsNoTracking()
            .Where(item => item.IsDemo || item.IsInternal)
            .Select(item => item.Id).ToListAsync();

        IQueryable<ProductAnalyticsEvent> BaseEvents(DateTimeOffset rangeEnd) =>
            dbContext.ProductAnalyticsEvents.AsNoTracking().Where(item => item.OccurredAtUtc >= start
                && item.OccurredAtUtc < rangeEnd && !item.IsSandbox
                && (!item.AppUserId.HasValue || !excludedUserIds.Contains(item.AppUserId.Value))
                && (!item.AnonymousUserId.HasValue || !excludedUserIds.Contains(item.AnonymousUserId.Value)));

        IQueryable<ProductAnalyticsEvent> FilterEvents(DateTimeOffset rangeEnd)
        {
            var query = BaseEvents(rangeEnd);
            if (!string.IsNullOrWhiteSpace(platform)) query = query.Where(item => item.Platform == platform);
            if (!string.IsNullOrWhiteSpace(appVersion)) query = query.Where(item => item.AppVersion == appVersion);
            if (!string.IsNullOrWhiteSpace(entry)) query = query.Where(item => item.Source == entry);
            if (!string.IsNullOrWhiteSpace(variant)) query = query.Where(item => item.PaywallVariant == variant);
            if (!string.IsNullOrWhiteSpace(freePolicy)) query = query.Where(item => item.FreePolicyVariant == freePolicy);
            return query;
        }

        // Attribute users using matching events, then include their server-side
        // activation/purchase events even when those have no client metadata.
        var matchedUsers = await FilterEvents(end).Select(item => item.AppUserId ?? item.AnonymousUserId)
            .Where(item => item.HasValue).Select(item => item!.Value).Distinct().ToListAsync();
        IQueryable<ProductAnalyticsEvent> AttributedEvents(DateTimeOffset rangeEnd) => BaseEvents(rangeEnd)
            .Where(item => matchedUsers.Contains((item.AppUserId ?? item.AnonymousUserId) ?? Guid.Empty));
        var events = await AttributedEvents(end).Select(item => new AnalyticsRow(
            item.Name, item.AppUserId, item.AnonymousUserId, item.OccurredAtUtc, item.BehaviorConsent)).ToListAsync();
        var funnelNames = new[] { "trial_started", "first_item_saved", "paywall_shown", "checkout_started", "purchase_verified", "pass_activated" };
        Stages = BuildOrderedFunnel(events, funnelNames);
        var signalNames = new[] { "trip_created", "first_item_saved", "first_useful_response", "day_review_viewed", "limit_reached", "paid_trip_opened", "offline_download_completed", "offline_download_failed" };
        Signals = signalNames.Select(name => new SignalRow(name, events.Where(item => item.Name == name)
            .Select(UserId).Where(item => item.HasValue).Distinct().Count())).ToList();

        var pendingQuery = dbContext.StorePurchaseIntents.AsNoTracking().Where(item => item.State == PurchaseIntentState.Pending
            && item.CreatedAtUtc >= start && item.CreatedAtUtc < end && !excludedUserIds.Contains(item.AppUserId)
            && item.Environment != StoreEnvironment.Sandbox);
        if (!string.IsNullOrWhiteSpace(variant)) pendingQuery = pendingQuery.Where(item => item.PaywallVariant == variant);
        if (!string.IsNullOrWhiteSpace(appVersion)) pendingQuery = pendingQuery.Where(item => item.AppVersion == appVersion);
        if (Enum.TryParse<PaywallEntryPoint>(entry, true, out var entryPoint)) pendingQuery = pendingQuery.Where(item => item.EntryPoint == entryPoint);
        if (string.Equals(platform, "ios", StringComparison.OrdinalIgnoreCase)) pendingQuery = pendingQuery.Where(item => item.Provider == StoreProvider.Apple);
        else if (string.Equals(platform, "android", StringComparison.OrdinalIgnoreCase)) pendingQuery = pendingQuery.Where(item => item.Provider == StoreProvider.Google);
        PendingPurchases = await pendingQuery.CountAsync();
        Refunds = events.Count(item => item.Name == "refund");
        Restores = events.Count(item => item.Name == "purchase_restored");
        FailedCheckouts = events.Count(item => item.Name == "checkout_failed");
        var observedUsers = events.Select(UserId).Where(item => item.HasValue).Select(item => item!.Value).Distinct().ToList();
        var consentingUsers = events.Where(item => item.BehaviorConsent).Select(UserId)
            .Where(item => item.HasValue).Select(item => item!.Value).Distinct().Count();
        ConsentCoveragePercent = observedUsers.Count == 0 ? 0 : Math.Round(consentingUsers * 100d / observedUsers.Count, 1);

        var purchasesQuery = dbContext.StorePurchaseTransactions.AsNoTracking().Include(item => item.PurchaseIntent)
            .Where(item => item.VerifiedAtUtc >= start && item.VerifiedAtUtc < end
                && item.Environment == StoreEnvironment.Production
                && !excludedUserIds.Contains(item.PurchaseIntent!.AppUserId));
        if (!string.IsNullOrWhiteSpace(variant)) purchasesQuery = purchasesQuery.Where(item => item.PurchaseIntent!.PaywallVariant == variant);
        if (!string.IsNullOrWhiteSpace(appVersion)) purchasesQuery = purchasesQuery.Where(item => item.PurchaseIntent!.AppVersion == appVersion);
        if (Enum.TryParse<PaywallEntryPoint>(entry, true, out entryPoint)) purchasesQuery = purchasesQuery.Where(item => item.PurchaseIntent!.EntryPoint == entryPoint);
        if (string.Equals(platform, "ios", StringComparison.OrdinalIgnoreCase)) purchasesQuery = purchasesQuery.Where(item => item.Provider == StoreProvider.Apple);
        else if (string.Equals(platform, "android", StringComparison.OrdinalIgnoreCase)) purchasesQuery = purchasesQuery.Where(item => item.Provider == StoreProvider.Google);
        var purchases = await purchasesQuery.ToListAsync();
        var transactionIds = purchases.Select(item => item.Id).ToList();
        var activatedTransactionIds = await dbContext.BuilderAccessGrants.AsNoTracking()
            .Where(item => item.PurchaseTransactionId.HasValue && transactionIds.Contains(item.PurchaseTransactionId.Value))
            .Select(item => item.PurchaseTransactionId!.Value).ToListAsync();
        ActivationFailures = transactionIds.Except(activatedTransactionIds).Count();
        PurchasesWithoutVerifiedAmount = purchases.Count(item => !item.GrossAmount.HasValue || string.IsNullOrWhiteSpace(item.Currency));
        Revenue = purchases.Where(item => item.GrossAmount.HasValue && item.Currency != null).GroupBy(item => item.Currency!)
            .Select(group => new RevenueRow(group.Key,
                group.Where(item => item.RevokedAtUtc == null).Sum(item => item.GrossAmount!.Value),
                group.Count(item => item.RevokedAtUtc == null),
                group.Where(item => item.RevokedAtUtc != null).Sum(item => item.GrossAmount!.Value),
                group.Count(item => item.RevokedAtUtc != null)))
            .OrderBy(item => item.Currency).ToList();

        DuplicatePurchases = await dbContext.StorePurchaseTransactions.AsNoTracking()
            .Where(item => item.RevocationReason == "duplicate_trip_purchase_review")
            .Include(item => item.PurchaseIntent)
            .OrderByDescending(item => item.VerifiedAtUtc)
            .Take(100)
            .Select(item => new DuplicatePurchaseRow(item.Id, item.Provider, item.Environment,
                item.ProviderTransactionId, item.PurchaseIntent!.TripId, item.PurchaseIntent.AppUserId,
                item.VerifiedAtUtc, item.AcknowledgedOrConsumed))
            .ToListAsync();

        var cohortEvents = await AttributedEvents(end.AddDays(30)).Where(item => item.Name == "first_item_saved" || item.Name == "purchase_verified")
            .Select(item => new AnalyticsRow(item.Name, item.AppUserId, item.AnonymousUserId, item.OccurredAtUtc, item.BehaviorConsent))
            .ToListAsync();
        var trialByUser = cohortEvents.Where(item => item.Name == "first_item_saved" && item.OccurredAtUtc < end && UserId(item).HasValue)
            .GroupBy(item => UserId(item)!.Value).ToDictionary(group => group.Key, group => group.Min(item => item.OccurredAtUtc));
        // A first activity on another trip does not start a new user cohort.
        var previouslyActivated = await dbContext.ProductAnalyticsEvents.AsNoTracking()
            .Where(item => item.Name == "first_item_saved" && item.OccurredAtUtc < start && !item.IsSandbox)
            .Select(item => item.AppUserId ?? item.AnonymousUserId).Distinct().ToListAsync();
        foreach (var userId in previouslyActivated)
            if (userId.HasValue) trialByUser.Remove(userId.Value);
        var purchaseByUser = cohortEvents.Where(item => item.Name == "purchase_verified" && UserId(item).HasValue)
            .GroupBy(item => UserId(item)!.Value).ToDictionary(group => group.Key, group => group.Select(item => item.OccurredAtUtc).Order().ToList());
        var purchaseTimes = trialByUser.Where(item => purchaseByUser.ContainsKey(item.Key))
            .SelectMany(trial => purchaseByUser[trial.Key].Where(purchase => purchase >= trial.Value)
                .Take(1).Select(purchase => (purchase - trial.Value).TotalHours)).Order().ToList();
        MedianHoursToPurchase = Median(purchaseTimes);
        var sevenDayTrials = trialByUser.Where(item => item.Value <= now.AddDays(-7)).ToList();
        var thirtyDayTrials = trialByUser.Where(item => item.Value <= now.AddDays(-30)).ToList();
        SevenDayCohortSize = sevenDayTrials.Count;
        ThirtyDayCohortSize = thirtyDayTrials.Count;
        SevenDayConversionPercent = ConversionPercent(sevenDayTrials, purchaseByUser, 7);
        ThirtyDayConversionPercent = ConversionPercent(thirtyDayTrials, purchaseByUser, 30);
        SevenDayCohortIncomplete = trialByUser.Any(item => item.Value > now.AddDays(-7));
        ThirtyDayCohortIncomplete = trialByUser.Any(item => item.Value > now.AddDays(-30));
    }

    public sealed record StageRow(string Name, int Users, double? ConversionFromPrevious);
    public sealed record SignalRow(string Name, int Users);
    public sealed record RevenueRow(string Currency, decimal Gross, int Purchases, decimal RefundedGross, int Refunds);
    public sealed record DuplicatePurchaseRow(Guid Id, StoreProvider Provider, StoreEnvironment Environment,
        string ProviderTransactionId, Guid TripId, Guid AppUserId, DateTimeOffset VerifiedAtUtc,
        bool Finalized);
    private sealed record AnalyticsRow(string Name, Guid? AppUserId, Guid? AnonymousUserId,
        DateTimeOffset OccurredAtUtc, bool BehaviorConsent);

    private static IReadOnlyList<StageRow> BuildOrderedFunnel(IReadOnlyList<AnalyticsRow> events, IReadOnlyList<string> names)
    {
        var result = new List<StageRow>();
        Dictionary<Guid, DateTimeOffset>? previous = null;
        foreach (var name in names)
        {
            var stageEvents = events.Where(item => item.Name == name && UserId(item).HasValue)
                .Where(item => previous is null || previous.TryGetValue(UserId(item)!.Value, out var at) && item.OccurredAtUtc >= at)
                .GroupBy(item => UserId(item)!.Value)
                .ToDictionary(group => group.Key, group => group.Min(item => item.OccurredAtUtc));
            result.Add(new StageRow(name, stageEvents.Count, previous is null || previous.Count == 0
                ? null : Math.Round(stageEvents.Count * 100d / previous.Count, 1)));
            previous = stageEvents;
        }
        return result;
    }

    private static Guid? UserId(AnalyticsRow item) => item.AppUserId ?? item.AnonymousUserId;
    private static double? Median(IReadOnlyList<double> values) => values.Count switch
    {
        0 => null,
        var count when count % 2 == 1 => values[count / 2],
        var count => (values[count / 2 - 1] + values[count / 2]) / 2d
    };

    private static double? ConversionPercent(IReadOnlyList<KeyValuePair<Guid, DateTimeOffset>> trials,
        IReadOnlyDictionary<Guid, List<DateTimeOffset>> purchases, int days)
    {
        if (trials.Count == 0) return null;
        var converted = trials.Count(trial => purchases.TryGetValue(trial.Key, out var times)
            && times.Any(purchase => purchase >= trial.Value && purchase <= trial.Value.AddDays(days)));
        return Math.Round(converted * 100d / trials.Count, 1);
    }
}

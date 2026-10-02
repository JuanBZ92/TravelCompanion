using Microsoft.EntityFrameworkCore;
using TravelCompanion.Api.Data;

namespace TravelCompanion.Api.Services;

internal static class ProductAnalyticsRetention
{
    // One atomic statement: a retry after a lost commit acknowledgement selects new rows,
    // never increments aggregates for already deleted events. Locks isolate concurrent workers.
    public static async Task<int> ProcessBatchAsync(TravelCompanionDbContext db, DateTimeOffset cutoff, CancellationToken ct)
    {
        var counts = await db.Database.SqlQuery<int>($"""
            WITH selected AS (
                SELECT "Id" FROM "ProductAnalyticsEvents"
                WHERE NOT "IsBusinessEvent" AND "OccurredAtUtc" < {cutoff}
                ORDER BY "OccurredAtUtc", "Id" LIMIT 5000 FOR UPDATE SKIP LOCKED
            ), removed AS (
                DELETE FROM "ProductAnalyticsEvents" e USING selected s
                WHERE e."Id" = s."Id" RETURNING e.*
            ), aggregated AS (
                INSERT INTO "ProductAnalyticsDailyAggregates"
                    ("Id", "Date", "Name", "Source", "Platform", "AppVersion", "PaywallVariant", "FreePolicyVariant", "EventCount")
                SELECT gen_random_uuid(), ("OccurredAtUtc" AT TIME ZONE 'UTC')::date, "Name",
                    COALESCE("Source", ''), COALESCE("Platform", ''), COALESCE("AppVersion", ''),
                    COALESCE("PaywallVariant", ''), COALESCE("FreePolicyVariant", ''), count(*)::int
                FROM removed GROUP BY 2,3,4,5,6,7,8 ORDER BY 2,3,4,5,6,7,8
                ON CONFLICT ("Date", "Name", "Source", "Platform", "AppVersion", "PaywallVariant", "FreePolicyVariant")
                DO UPDATE SET "EventCount" = "ProductAnalyticsDailyAggregates"."EventCount" + EXCLUDED."EventCount"
                RETURNING "Id"
            ) SELECT count(*)::int AS "Value" FROM removed
            """).ToListAsync(ct);
        return counts.Single();
    }
}

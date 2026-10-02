using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using TravelCompanion.Api.Models;
using TravelCompanion.Api.Services;
using TravelCompanion.Shared;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Api.Tests;

public sealed class PostgresQueryPerformanceTests
{
    [PostgresItineraryIdempotencyTests.PostgresFact]
    public async Task Retention_is_atomic_concurrent_and_preserves_business_events()
    {
        await using var fixture = new PerformanceDatabase();
        await fixture.InitializeAsync();
        await fixture.SeedEventsAsync(55001);
        await using var db = fixture.Open();
        db.ProductAnalyticsEvents.Add(new() { Id = Guid.NewGuid(), EventId = Guid.NewGuid(), Name = "purchase_verified", IsBusinessEvent = true, OccurredAtUtc = new(2020,1,1,0,0,0,TimeSpan.Zero) });
        db.ProductAnalyticsEvents.Add(new() { Id = Guid.NewGuid(), EventId = Guid.NewGuid(), Name = "paywall_shown", OccurredAtUtc = DateTimeOffset.UtcNow });
        db.ProductAnalyticsDailyAggregates.Add(new() { Id = Guid.NewGuid(), Date = new(2025,1,1), Name = "paywall_shown", Source = "", Platform = "", AppVersion = "", PaywallVariant = "", EventCount = 7 });
        await db.SaveChangesAsync();
        var cutoff = DateTimeOffset.UtcNow.AddDays(-90);
        await using (var transaction = await db.Database.BeginTransactionAsync())
        {
            Assert.Equal(5000, await ProductAnalyticsRetention.ProcessBatchAsync(db, cutoff, default));
            await transaction.RollbackAsync();
        }
        Assert.Equal(55003, await db.ProductAnalyticsEvents.CountAsync());
        Assert.Equal(7, await db.ProductAnalyticsDailyAggregates.SumAsync(a => a.EventCount));
        await db.Database.ExecuteSqlRawAsync("ALTER TABLE \"ProductAnalyticsDailyAggregates\" ADD CONSTRAINT test_failure CHECK (\"EventCount\" < 10)");
        await Assert.ThrowsAsync<Npgsql.PostgresException>(() => ProductAnalyticsRetention.ProcessBatchAsync(db, cutoff, default));
        Assert.Equal(55003, await db.ProductAnalyticsEvents.CountAsync());
        Assert.Equal(7, await db.ProductAnalyticsDailyAggregates.SumAsync(a => a.EventCount));
        await db.Database.ExecuteSqlRawAsync("ALTER TABLE \"ProductAnalyticsDailyAggregates\" DROP CONSTRAINT test_failure");
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ProductAnalyticsRetention.ProcessBatchAsync(db, cutoff, cancelled.Token));
        fixture.Counter.Reset();
        async Task Drain()
        {
            await using var worker = fixture.Open();
            while (await ProductAnalyticsRetention.ProcessBatchAsync(worker, cutoff, default) > 0) { }
        }
        await Task.WhenAll(Drain(), Drain());
        Assert.InRange(fixture.Counter.Commands.Count, 13, 16);
        Assert.Equal(2, await db.ProductAnalyticsEvents.CountAsync());
        Assert.Equal(55008, await db.ProductAnalyticsDailyAggregates.SumAsync(a => a.EventCount));
        Assert.Equal(100, await db.ProductAnalyticsDailyAggregates.CountAsync());
        Assert.Equal(0, await ProductAnalyticsRetention.ProcessBatchAsync(db, cutoff, default));
    }

    [PostgresItineraryIdempotencyTests.PostgresFact]
    public async Task Hundred_events_share_one_grant_lookup_and_preserve_invalid_trip_fallback()
    {
        await using var fixture = new PerformanceDatabase();
        await fixture.InitializeAsync();
        await using var db = fixture.Open();
        var user = new AppUser { Id = Guid.NewGuid(), Email = "performance@example.test", DisplayName = "Test", BehaviorAnalyticsConsent = true };
        db.AppUsers.Add(user);
        await db.SaveChangesAsync();
        var sessions = new UserSessionService(db);
        var (_, token) = await sessions.CreateSessionAsync(user);
        var http = new DefaultHttpContext();
        http.Request.Headers.Authorization = "Bearer " + token;
        var batch = new ProductAnalyticsBatchDto(Enumerable.Range(0,100).Select(i => new ProductAnalyticsEventDto(
            Guid.NewGuid(), "paywall_shown", DateTimeOffset.UtcNow, null, null, null, null, Guid.NewGuid(), true)).ToArray());
        fixture.Counter.Reset();
        Assert.Equal(100, await new ProductAnalyticsService(db).IngestAsync(http, batch, sessions, default));
        Assert.Single(fixture.Counter.Commands, sql => sql.Contains("FROM \"BuilderAccessGrants\""));
        Assert.InRange(fixture.Counter.Commands.Count, 4, 7);
        Assert.All(await db.ProductAnalyticsEvents.ToListAsync(), row => Assert.Null(row.TripId));
        Assert.Equal(0, await new ProductAnalyticsService(db).IngestAsync(http, batch, sessions, default));
    }

    [PostgresItineraryIdempotencyTests.PostgresFact]
    public async Task Planning_SQL_preserves_access_policy_city_fallback_and_ties()
    {
        await using var fixture = new PerformanceDatabase();
        await fixture.InitializeAsync();
        await using var db = fixture.Open();
        var destination = new Destination { Id = Guid.NewGuid(), Name = "Japan", Slug = "japan", Country = "Japan", HeroImageUrl = "", ShortDescription = "" };
        var user = new AppUser { Id = Guid.NewGuid(), Email = "ranking@example.test", DisplayName = "Test" };
        var package = new TravelPackage { Id = Guid.NewGuid(), DestinationId = destination.Id, Name = "Pass", Slug = "pass", Description = "", Currency = "EUR" };
        db.AddRange(destination, user, package);
        foreach (var level in Enum.GetValues<ContentAccessLevel>())
        foreach (var packaged in new[] { false, true })
            db.Recommendations.Add(new() { Id = Guid.NewGuid(), DestinationId = destination.Id, Title = "Tie", Category = "culture", Neighborhood = "Tokyo", Description = packaged ? "KYOTO" : "Osaka", AccessLevel = level, Packages = packaged ? [package] : [] });
        await db.SaveChangesAsync();
        foreach (var subscription in new[] { false, true })
        foreach (var purchased in new[] { false, true })
        foreach (var city in new[] { "KyOtO", "Missing", "" })
        {
            user.Entitlements = [];
            if (subscription) user.Entitlements.Add(new() { DestinationId = destination.Id, AccessLevel = ContentAccessLevel.Subscription, Source = "test" });
            if (purchased) user.Entitlements.Add(new() { TravelPackageId = package.Id, AccessLevel = ContentAccessLevel.Paid, Source = "test" });
            var entitlement = UserEntitlementProjection.Map(user, user.Entitlements);
            var all = await db.Recommendations.AsNoTracking().Include(r => r.Packages).OrderBy(r => r.Title).ToListAsync();
            var expected = all.Where(r => ContentAccessPolicy.IsRecommendationUnlocked(entitlement, r.AccessLevel, r.DestinationId, r.Packages.Select(p => p.Id).ToList())).ToList();
            var matches = expected.Where(r => r.Neighborhood.Contains(city, StringComparison.OrdinalIgnoreCase) || r.Description.Contains(city, StringComparison.OrdinalIgnoreCase)).ToList();
            if (matches.Count > 0) expected = matches;
            var ranker = new RecordingRanker();
            await new TravelRecommendationPlanningService(db, ranker).RankAsync(user, [destination.Id], city,
                new(), [], new(city, new(2026,10,1), null, null, null, null), "", null, new HashSet<string>(), default);
            Assert.Equal(expected.Select(r => r.Id), ranker.Seen.Select(r => r.Id));
        }
        db.ChangeTracker.Clear();
        user.Entitlements = [];
        db.BuilderAccessGrants.Add(new() { Id = Guid.NewGuid(), AppUserId = user.Id, DestinationId = destination.Id, IsTrial = true });
        db.FreeMapCities.Add(new() { Id = Guid.NewGuid(), DestinationId = destination.Id, CitySlug = "tokyo", DisplayName = "Tokyo", CenterLatitude = 0, CenterLongitude = 0, FreeRadiusKm = 2 });
        db.Recommendations.Add(new() { Id = Guid.NewGuid(), DestinationId = destination.Id, Title = "Far", Category = "culture", Neighborhood = "Tokyo", Description = "Tokyo", Latitude = 30, Longitude = 30 });
        await db.SaveChangesAsync();
        var freeRanker = new RecordingRanker();
        var free = new FreeTrialAccessService(db, Microsoft.Extensions.Options.Options.Create(new TravelCompanion.Api.Options.FreePreviewOptions()), Microsoft.Extensions.Logging.Abstractions.NullLogger<FreeTrialAccessService>.Instance);
        await new TravelRecommendationPlanningService(db, freeRanker, free).RankAsync(user, [destination.Id], "Missing",
            new(), [], new("Missing", new(2026,10,1), null, null, null, null), "", null, new HashSet<string>(), default);
        var only = Assert.Single(freeRanker.Seen);
        Assert.Equal(ContentAccessLevel.Free, only.AccessLevel);
        Assert.Equal(0, only.Latitude);
    }
    private sealed class RecordingRanker : IRecommendationRanker
    {
        public IReadOnlyList<Recommendation> Seen = [];
        public IReadOnlyList<ScoredRecommendation> Rank(TravelPreferenceProfile profile, IReadOnlyList<Reservation> reservations, IReadOnlyList<Recommendation> recommendations, TravelPlanningContext context)
        { Seen = recommendations; return recommendations.Select(r => new ScoredRecommendation(r, 0, null, null, [], [])).ToList(); }
    }

    [PostgresItineraryIdempotencyTests.PostgresFact]
    public async Task Index_migration_is_reversible_without_losing_events()
    {
        await using var fixture = new PerformanceDatabase();
        await fixture.InitializeAsync();
        await fixture.SeedEventsAsync(10);
        await using var db = fixture.Open();
        var migrations = (await db.Database.GetAppliedMigrationsAsync()).ToArray();
        Assert.EndsWith("OptimizeQueryWorkloads", migrations[^1]);
        var migrator = Microsoft.EntityFrameworkCore.Infrastructure.AccessorExtensions.GetService<Microsoft.EntityFrameworkCore.Migrations.IMigrator>(db);
        await migrator.MigrateAsync(migrations[^2]);
        Assert.Equal(10, await db.ProductAnalyticsEvents.CountAsync());
        await db.Database.MigrateAsync();
        var indexes = await db.Database.SqlQueryRaw<string>("""
            SELECT indexname AS "Value" FROM pg_indexes WHERE schemaname = current_schema()
            AND indexname IN ('IX_AnalyticsEvents_BehaviorRetention','IX_PurchaseIntents_PendingAttempt','IX_PurchaseTransactions_Unconfirmed')
            """).ToListAsync();
        Assert.Equal(3, indexes.Count);
        Assert.Equal(10, await db.ProductAnalyticsEvents.CountAsync());
    }
}

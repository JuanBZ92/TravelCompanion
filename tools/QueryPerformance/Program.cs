using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using TravelCompanion.Api.Data;
using TravelCompanion.Api.Models;
using TravelCompanion.Api.Services;
using TravelCompanion.Api.Tests;
using TravelCompanion.Shared;
using TravelCompanion.Shared.Dtos;

// Run from repository root. The helper enforces localhost and an isolated, disposable schema.
if (args.FirstOrDefault() == "--cold-probe")
{
    if (args.Length != 2 || !System.Text.RegularExpressions.Regex.IsMatch(args[1], "^tc_perf_[a-f0-9]{32}$"))
        throw new InvalidOperationException("Invalid benchmark schema.");
    var builder = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("TRAVELCOMPANION_TEST_POSTGRES"));
    if (builder.Host is not ("localhost" or "127.0.0.1" or "::1")) throw new InvalidOperationException("Local only.");
    builder.SearchPath = args[1];
    builder.Pooling = false;
    await using var connection = new NpgsqlConnection(builder.ConnectionString);
    var start = Stopwatch.StartNew();
    await connection.OpenAsync();
    var openMs = start.Elapsed.TotalMilliseconds;
    await using var db = new TravelCompanionDbContext(new DbContextOptionsBuilder<TravelCompanionDbContext>().UseNpgsql(connection).Options);
    var rows = await db.Recommendations.AsNoTracking().OrderBy(r => r.Title).Take(25).ToListAsync();
    Console.WriteLine(JsonSerializer.Serialize(new { openMs, firstQueryAndInitializationMs = start.Elapsed.TotalMilliseconds - openMs, rows = rows.Count }));
    return;
}
var output = Path.GetFullPath(args.FirstOrDefault() ?? "artifacts/query-performance.json");
await using var fixture = new PerformanceDatabase();
await fixture.InitializeAsync();
var destination = new Destination { Id = Id("destination"), Name = "Japan", Slug = "japan", Country = "Japan", HeroImageUrl = "", ShortDescription = "" };
var user = new AppUser { Id = Id("user"), Email = "synthetic@example.test", DisplayName = "Synthetic", BehaviorAnalyticsConsent = true };
var trip = new Trip { Id = Id("trip"), AppUserId = user.Id, DestinationId = destination.Id, TravelerName = "Synthetic", StartsOn = new(2026,10,1), EndsOn = new(2026,10,7), PublicationStatus = TripPublicationStatus.Published };
string token;
await using (var db = fixture.Open())
{
    db.AddRange(destination, user, trip);
    db.BuilderAccessGrants.Add(new() { Id = Id("grant"), AppUserId = user.Id, DestinationId = destination.Id, TripId = trip.Id, FreePolicy = FreeAccessPolicy.PersistentFree });
    for (var i = 0; i < 10000; i++)
        db.Recommendations.Add(new() { Id = Id("recommendation" + i), DestinationId = destination.Id, Title = $"Place {i / 2:D5}", Category = "culture", Neighborhood = "City " + i % 20,
            Description = "Museum park food walking " + new string('x', 200), Latitude = 35.6m, Longitude = 139.7m,
            ExtraDescription = new string('e', 3000), DescriptionEn = new string('d', 500), OpeningHours = "09:00-18:00", Rating = 4.5,
            Tags = ["culture"], SuggestedDurationMinutes = 60, AccessLevel = i % 5 == 0 ? ContentAccessLevel.AdminOnly : ContentAccessLevel.Free });
    await db.SaveChangesAsync();
    (_, token) = await new UserSessionService(db).CreateSessionAsync(user, tripId: trip.Id);
    await db.Database.ExecuteSqlInterpolatedAsync($"""
        INSERT INTO "StorePurchaseIntents" ("Id","AppUserId","TripId","Provider","ProductId","OpaqueAccountId","State","EntryPoint","PaywallVariant","CreatedAtUtc","RetryCount","ProtectedEvidence","LastAttemptAtUtc")
        SELECT md5('intent-' || i)::uuid, {user.Id}, {trip.Id}, 'Google', 'synthetic', 'account-' || i,
          CASE WHEN i % 1000=0 THEN 'Pending' ELSE 'Active' END, 'Map', 'test', TIMESTAMPTZ '2025-01-01Z', 0,
          CASE WHEN i % 1000=0 THEN 'synthetic' ELSE NULL END, TIMESTAMPTZ '2025-01-01Z' + i * interval '1 second'
        FROM generate_series(1,100000) i;
        INSERT INTO "StorePurchaseTransactions" ("Id","PurchaseIntentId","Provider","Environment","ProviderTransactionId","ProductId","PurchasedAtUtc","VerifiedAtUtc","AcknowledgedOrConsumed","ProtectedProviderToken")
        SELECT md5('transaction-' || i)::uuid, md5('intent-' || i)::uuid, 'Google', 'Production', 'transaction-' || i, 'synthetic', TIMESTAMPTZ '2025-01-01Z',
          TIMESTAMPTZ '2025-01-01Z' + i * interval '1 second', i % 1000 <> 0, CASE WHEN i % 1000=0 THEN 'synthetic' ELSE NULL END
        FROM generate_series(1,100000) i
        """);
}
await fixture.SeedEventsAsync(100000);
await using (var db = fixture.Open()) await db.Database.ExecuteSqlRawAsync("ANALYZE");
var cold = new List<object>();
for (var probe = 0; probe < 5; probe++)
{
    var info = new ProcessStartInfo("dotnet") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
    info.ArgumentList.Add(typeof(PerformanceDatabase).Assembly.Location);
    info.ArgumentList.Add("--cold-probe");
    info.ArgumentList.Add(new NpgsqlConnectionStringBuilder(fixture.ConnectionString).SearchPath!);
    var clock = Stopwatch.StartNew();
    using var child = Process.Start(info)!;
    var json = await child.StandardOutput.ReadToEndAsync();
    var error = await child.StandardError.ReadToEndAsync();
    await child.WaitForExitAsync();
    if (child.ExitCode != 0) throw new InvalidOperationException(error);
    cold.Add(new { processTotalMs = clock.Elapsed.TotalMilliseconds, measurements = JsonSerializer.Deserialize<JsonElement>(json) });
}
var results = new List<object>();
var fingerprints = new Dictionary<string, string>();
var plans = new List<object>();
foreach (var baseline in new[] { true, false })
{
    // This schema contains only synthetic data. Toggle only the three new indexes.
    await using (var db = fixture.Open())
    {
        if (baseline) await db.Database.ExecuteSqlRawAsync("""
            DROP INDEX "IX_AnalyticsEvents_BehaviorRetention";
            DROP INDEX "IX_PurchaseIntents_PendingAttempt";
            DROP INDEX "IX_PurchaseTransactions_Unconfirmed";
            """);
        else await db.Database.ExecuteSqlRawAsync("""
            CREATE INDEX "IX_AnalyticsEvents_BehaviorRetention" ON "ProductAnalyticsEvents" ("OccurredAtUtc","Id") WHERE NOT "IsBusinessEvent";
            CREATE INDEX "IX_PurchaseIntents_PendingAttempt" ON "StorePurchaseIntents" ("LastAttemptAtUtc") WHERE "State"='Pending' AND "ProtectedEvidence" IS NOT NULL;
            CREATE INDEX "IX_PurchaseTransactions_Unconfirmed" ON "StorePurchaseTransactions" ("VerifiedAtUtc") WHERE NOT "AcknowledgedOrConsumed" AND "ProtectedProviderToken" IS NOT NULL;
            """);
    }
    foreach (var scenario in new[] { "planning.city", "planning.fallback", "today.new", "today.persisted", "analytics.ingest", "analytics.retention", "purchases.pending", "purchases.unconfirmed" })
    {
        var samples = new List<object>();
        var elapsedSamples = new List<double>();
        for (var iteration = -5; iteration < 30; iteration++)
        {
            await using var db = fixture.Open();
            // Open outside measurement so connection/JIT warmup is not confused with SQL work.
            await db.Database.OpenConnectionAsync();
            await using var transaction = await db.Database.BeginTransactionAsync();
            if (scenario.StartsWith("today"))
            {
                await db.Database.ExecuteSqlRawAsync("DELETE FROM \"RecommendationInteractionSignals\"");
                if (scenario == "today.persisted") await Today(db, baseline);
            }
            fixture.Counter.Reset();
            var allocations = GC.GetTotalAllocatedBytes(true);
            var watch = Stopwatch.StartNew();
            object? value = null;
            int rows;
            if (scenario.StartsWith("planning"))
            {
                var city = scenario == "planning.city" ? "City 3" : "missing";
                ITravelRecommendationPlanningService service = baseline
                    ? new BaselineTravelRecommendationPlanningService(db, new DeterministicRecommendationRanker())
                    : new TravelRecommendationPlanningService(db, new DeterministicRecommendationRanker());
                var ranking = await service.RankAsync(user, [destination.Id], city, new(), [], new(city, trip.StartsOn, null, null, null, null), "", null, new HashSet<string>(), default);
                rows = ranking.RankedRecommendations.Count;
                value = ranking.RankedRecommendations.Select(r => r.Recommendation.Id).ToArray();
            }
            else if (scenario.StartsWith("today"))
            {
                var today = await Today(db, baseline);
                rows = today!.Sections.Sum(s => s.Recommendations.Count);
                value = today with { GeneratedAtUtc = DateTimeOffset.UnixEpoch };
            }
            else if (scenario == "analytics.ingest")
            {
                var http = new DefaultHttpContext();
                http.Request.Headers.Authorization = "Bearer " + token;
                var batch = new ProductAnalyticsBatchDto(Enumerable.Range(0,100).Select(i => new ProductAnalyticsEventDto(Id("ingest" + i), "paywall_shown", DateTimeOffset.UtcNow, null, null, null, null, trip.Id, true)).ToArray());
                rows = baseline ? await new BaselineProductAnalyticsService(db).IngestAsync(http, batch, new(db), default)
                    : await new ProductAnalyticsService(db).IngestAsync(http, batch, new(db), default);
            }
            else if (scenario == "analytics.retention")
            {
                if (baseline)
                {
                    var services = new ServiceCollection().AddSingleton(db).BuildServiceProvider();
                    await using (services)
                    {
                        var worker = new BaselineProductAnalyticsRetentionWorker(services.GetRequiredService<IServiceScopeFactory>(), NullLogger<BaselineProductAnalyticsRetentionWorker>.Instance);
                        await (Task)typeof(BaselineProductAnalyticsRetentionWorker).GetMethod("RemoveExpiredBehaviorEventsAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(worker, [CancellationToken.None])!;
                    }
                    rows = 5000;
                }
                else rows = await ProductAnalyticsRetention.ProcessBatchAsync(db, DateTimeOffset.UtcNow.AddDays(-90), default);
            }
            else if (scenario == "purchases.pending")
                rows = (await db.StorePurchaseIntents.Include(i => i.Trip).Where(i => i.State == PurchaseIntentState.Pending && i.ProtectedEvidence != null).OrderBy(i => i.LastAttemptAtUtc).Take(25).ToListAsync()).Count;
            else rows = (await db.StorePurchaseTransactions.Where(i => !i.AcknowledgedOrConsumed && i.ProtectedProviderToken != null).OrderBy(i => i.VerifiedAtUtc).Take(25).ToListAsync()).Count;
            watch.Stop();
            allocations = GC.GetTotalAllocatedBytes(true) - allocations;
            if (value is not null && iteration == 0)
            {
                var hash = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value)));
                if (baseline) fingerprints[scenario] = hash;
                else if (fingerprints[scenario] != hash) throw new InvalidOperationException("Behavior changed: " + scenario);
            }
            if (iteration >= 0)
            {
                elapsedSamples.Add(watch.Elapsed.TotalMilliseconds);
                samples.Add(new { ms = watch.Elapsed.TotalMilliseconds, allocatedBytes = allocations, commands = fixture.Counter.Commands.Count, rows });
            }
            // Actual EF SQL and parameters. Retention CTE is also explained inside this disposable transaction.
            if (iteration == 0)
            {
                var commands = fixture.Counter.Commands.ToArray();
                var parameters = fixture.Counter.Parameters.ToArray();
                for (var i = 0; i < commands.Length; i++)
                {
                    if (!commands[i].TrimStart().StartsWith("SELECT", StringComparison.Ordinal)
                        && !(scenario == "analytics.retention" && commands[i].TrimStart().StartsWith("WITH", StringComparison.Ordinal))) continue;
                    await using var explain = ((NpgsqlConnection)db.Database.GetDbConnection()).CreateCommand();
                    explain.CommandText = "EXPLAIN (ANALYZE, BUFFERS, FORMAT JSON) " + commands[i];
                    explain.Parameters.AddRange(parameters[i]);
                    var plan = await explain.ExecuteScalarAsync();
                    if (!baseline && scenario.StartsWith("purchases") && !plan!.ToString()!.Contains(
                        scenario == "purchases.pending" ? "IX_PurchaseIntents_PendingAttempt" : "IX_PurchaseTransactions_Unconfirmed"))
                        throw new InvalidOperationException("Expected partial index was not selected: " + scenario);
                    if (!baseline && scenario == "analytics.retention" && commands[i].TrimStart().StartsWith("WITH")
                        && !plan!.ToString()!.Contains("IX_AnalyticsEvents_BehaviorRetention"))
                        throw new InvalidOperationException("Retention index was not selected.");
                    plans.Add(new { baseline, scenario, plan = JsonSerializer.Deserialize<JsonElement>(plan!.ToString()!) });
                }
            }
            await transaction.RollbackAsync();
        }
        elapsedSamples.Sort();
        results.Add(new { baseline, scenario, p50 = elapsedSamples[14], p95 = elapsedSamples[28], samples });
        Console.WriteLine($"{(baseline ? "baseline" : "optimized")} {scenario}: p50={elapsedSamples[14]:F2}ms p95={elapsedSamples[28]:F2}ms");
        // Persist progress, including evidence if a later scenario fails.
        await File.WriteAllTextAsync(output, JsonSerializer.Serialize(new { baselineRevision = File.Exists("artifacts/query-baseline/revision.txt") ? File.ReadAllText("artifacts/query-baseline/revision.txt").Trim() : "HEAD", cold, results, plans, fingerprints }, new JsonSerializerOptions { WriteIndented = true }));
    }
}
Guid Id(string text) => new(MD5.HashData(Encoding.UTF8.GetBytes(text)));
Task<TodayDto?> Today(TravelCompanionDbContext db, bool baseline) => baseline
    ? new BaselineTodayRecommendationService(db, NullLogger<BaselineTodayRecommendationService>.Instance).GetTodayAsync(user, trip.Id, trip.StartsOn, null, default)
    : new TodayRecommendationService(db, NullLogger<TodayRecommendationService>.Instance).GetTodayAsync(user, trip.Id, trip.StartsOn, null, default);

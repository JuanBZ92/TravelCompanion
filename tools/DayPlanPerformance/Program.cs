using System.Diagnostics;
using System.Text.Json;
using Npgsql;
using TravelCompanion.Api.Tests;

var output = Path.GetFullPath(args.FirstOrDefault() ?? "artifacts/day-plan-performance.json");
var connectionSettings = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("TRAVELCOMPANION_TEST_POSTGRES"));
var scenarios = new List<object>();
foreach (var days in new[] { 1, 3, 7 })
{
    await using var database = new PerformanceDatabase();
    await database.InitializeAsync();
    DayPlanTestWorld world;
    await using (var seed = database.Open())
        world = await DayPlanTestWorld.SeedAsync(seed, perCity: 500, existing: 100);

    async Task<Sample> Measure()
    {
        database.Counter.Reset();
        var allocated = GC.GetTotalAllocatedBytes(true);
        var timer = Stopwatch.StartNew();
        await using var db = database.Open();
        var result = await DayPlanTestWorld.Service(db, analytics: true).GenerateAsync(world.Access(), world.Request(days), default);
        timer.Stop();
        return new(timer.Elapsed.TotalMilliseconds, database.Counter.Commands.Count,
            GC.GetTotalAllocatedBytes(true) - allocated, result.Days.Sum(day => day.Stops.Count),
            database.Counter.Commands.Count(sql => sql.Contains("FROM \"Recommendations\"", StringComparison.Ordinal)));
    }

    var firstCall = await Measure();
    for (var i = 0; i < 5; i++) await Measure();
    var samples = new List<Sample>();
    for (var i = 0; i < 30; i++) samples.Add(await Measure());
    double Percentile(Func<Sample, double> value, double percentile)
    {
        var ordered = samples.Select(value).Order().ToArray();
        return ordered[(int)Math.Ceiling(ordered.Length * percentile) - 1];
    }
    scenarios.Add(new
    {
        dayCount = days, recommendations = 1000, existingPlans = 100, warmups = 5, repetitions = 30,
        firstCall, p50Milliseconds = Percentile(sample => sample.Milliseconds, .50),
        p95Milliseconds = Percentile(sample => sample.Milliseconds, .95),
        p50Commands = Percentile(sample => sample.Commands, .50),
        p50AllocatedBytes = Percentile(sample => sample.AllocatedBytes, .50),
        samples
    });
    Console.WriteLine($"{days} day(s): p50={Percentile(sample => sample.Milliseconds, .50):F1}ms; " +
        $"p95={Percentile(sample => sample.Milliseconds, .95):F1}ms; commands={Percentile(sample => sample.Commands, .50):F0}");
}
Directory.CreateDirectory(Path.GetDirectoryName(output)!);
await File.WriteAllTextAsync(output, JsonSerializer.Serialize(new
{
    measuredAtUtc = DateTimeOffset.UtcNow, environment = "Local PostgreSQL, synthetic data only",
    poolingEnabled = connectionSettings.Pooling, scenarios
}, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine($"Report: {output}");

internal sealed record Sample(double Milliseconds, int Commands, long AllocatedBytes, int Ideas, int CatalogQueries);

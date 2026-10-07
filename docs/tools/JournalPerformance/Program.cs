using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using TravelCompanion.Mobile.Services;
using TravelCompanion.Shared.Dtos;

CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-US");
if (args.Length != 4) throw new ArgumentException("Arguments: variant output-directory warmups repetitions");
var variant = args[0]; var output = Path.GetFullPath(args[1]);
var warmups = int.Parse(args[2]); var repetitions = int.Parse(args[3]);
if (warmups < 5 || repetitions < 30) throw new ArgumentException("Use at least five warmups and thirty repetitions.");
Directory.CreateDirectory(output);
FileSystem.AppDataDirectory = Path.Combine(output, variant, "synthetic-cache");
Directory.CreateDirectory(FileSystem.AppDataDirectory);
var user = Guid.Parse("00000001-0000-0000-0000-000000000001");
var trip = Guid.Parse("00000002-0000-0000-0000-000000000002");
var session = new AuthSessionService(user, trip); var api = new TravelCompanionApiClient();
var cache = new OfflineCacheService(); var journal = new JournalStore(cache, session, api);
var expenses = new ExpenseStore(cache, session, api); var scope = journal.Scope();
var date = new DateOnly(2026, 10, 6); const int networkHoldMs = 100;
var memories = Enumerable.Range(1, 250).Select(i => new JournalMemory(new(Id(i), trip, $"Memory {i}", "Tokyo", date,
    new string('a', 1000), 1, DateTimeOffset.UnixEpoch))).ToList();
var items = Enumerable.Range(1, 250).Select(i => new LocalExpense(new(Id(i), trip, 12.50m, "EUR", date,
    ExpenseCategory.Food, $"Expense {i}", null, null, 1m, date, "manual", "EUR", 1, false, Guid.Empty))).ToList();
var book = new ExpenseBook(new("EUR", null, 1, Guid.Empty), items);
var journalKey = $"personal-journal-{user}-{trip}-index";
var expenseKey = $"personal-expenses-{user}-{trip}";
var scenarios = new List<Scenario>();
await Run("journal-save-during-http", async i =>
{
    await cache.SaveAsync(journalKey, memories);
    return await HeldSave(() => journal.LoadAsync(scope, [], true, default),
        () => journal.SaveAsync(scope, memories[0], $"Concurrent edit {i}"), async () =>
        {
            var saved = (await journal.LoadAsync(scope, [], false, default)).Single(x => x.Id == memories[0].Id);
            if (saved.Text != $"Concurrent edit {i}" || saved.Pending is null) throw new InvalidOperationException("Local edit lost");
        });
});
await Run("expense-save-during-http", async i =>
{
    await cache.SaveAsync(expenseKey, book);
    return await HeldSave(() => expenses.SyncAsync(scope),
        () => expenses.SaveAsync(scope, items[0].Value with { Concept = $"Concurrent expense {i}" }, 1, 1), async () =>
        {
            var saved = (await expenses.ReadAsync(scope)).Items.Single(x => x.Value.Id == items[0].Value.Id);
            if (saved.Value.Concept != $"Concurrent expense {i}" || saved.Pending is null) throw new InvalidOperationException("Local expense lost");
        });
});
await cache.SaveAsync(journalKey, memories);
await journal.AddPhotosAsync(scope, memories[0], Enumerable.Range(0, 6).Select(_ => new FileResult()));
var photographed = (await journal.LoadAsync(scope, [], false, default)).Single(x => x.Id == memories[0].Id);
if (photographed.Images.Length != 6) throw new InvalidOperationException("Photo fixture failed");
await Run("six-encrypted-thumbnails", async _ =>
{
    api.Reset(); BenchmarkIo.Reset();
    var allocationStart = GC.GetTotalAllocatedBytes(true); var watch = Stopwatch.StartNew();
    foreach (var photo in photographed.Images)
    {
        var thumbnail = await journal.PhotoAsync(scope, photo.Id, true);
        if (thumbnail?.Length != 8 * 1024) throw new InvalidOperationException("Thumbnail changed");
    }
    watch.Stop();
    return new(watch.Elapsed.TotalMilliseconds, GC.GetTotalAllocatedBytes(true) - allocationStart,
        BenchmarkIo.Reads, BenchmarkIo.BytesRead, api.Requests);
});
var report = new
{
    variant, runtime = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
    os = System.Runtime.InteropServices.RuntimeInformation.OSDescription, processorCount = Environment.ProcessorCount,
    warmups, repetitions, networkHoldMs,
    fixture = new { journalEntries = 250, charactersPerEntry = 1000, expenseEntries = 250, photoBytes = 2 * 1024 * 1024, thumbnailBytes = 8 * 1024, visiblePhotos = 6 },
    scope = "Host Release, real AES-GCM/file cache, controlled HTTP and media adapters; excludes native image decode, SecureStorage OS latency and Android rendering. Save allocation/IO covers the complete overlapping sync + local save; elapsed covers only local save. Thumbnail elapsed covers six reads. First repetition is pre-warmup, not an app cold start.",
    scenarios
};
var json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
await File.WriteAllTextAsync(Path.Combine(output, variant + ".json"), json);
foreach (var scenario in scenarios) Console.WriteLine($"{variant} {scenario.Name}: p50={scenario.P50Ms:F3} ms p95={scenario.P95Ms:F3} ms alloc.p50={scenario.P50AllocatedBytes} reads.p50={scenario.P50FileReads} bytes.p50={scenario.P50BytesRead} requests.p50={scenario.P50Requests}");

async Task<Sample> HeldSave(Func<Task> startSync, Func<Task> save, Func<Task> verify)
{
    api.Reset(); BenchmarkIo.Reset();
    var allocations = GC.GetTotalAllocatedBytes(true);
    var sync = startSync();
    await api.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
    var release = ReleaseAfterDelay();
    var watch = Stopwatch.StartNew();
    await save(); watch.Stop();
    await sync; await release;
    var sample = new Sample(watch.Elapsed.TotalMilliseconds, GC.GetTotalAllocatedBytes(true) - allocations,
        BenchmarkIo.Reads, BenchmarkIo.BytesRead, api.Requests);
    await verify();
    return sample;
    async Task ReleaseAfterDelay() { await Task.Delay(networkHoldMs); api.Release.TrySetResult(); }
}

async Task Run(string name, Func<int, Task<Sample>> action)
{
    var first = await action(-1);
    for (var i = 0; i < warmups; i++) await action(i);
    var samples = new List<Sample>();
    for (var i = 0; i < repetitions; i++) samples.Add(await action(i + warmups));
    scenarios.Add(new(name, first, samples, Quantile(samples.Select(x => x.ElapsedMs), .5), Quantile(samples.Select(x => x.ElapsedMs), .95),
        (long)Quantile(samples.Select(x => (double)x.AllocatedBytes), .5), (long)Quantile(samples.Select(x => (double)x.FileReads), .5),
        (long)Quantile(samples.Select(x => (double)x.BytesRead), .5), (int)Quantile(samples.Select(x => (double)x.Requests), .5)));
}

static double Quantile(IEnumerable<double> values, double fraction)
{
    var sorted = values.Order().ToArray(); return sorted[Math.Clamp((int)Math.Ceiling(sorted.Length * fraction) - 1, 0, sorted.Length - 1)];
}
static Guid Id(int i) => new(i, 0, 0, new byte[8]);
internal sealed record Sample(double ElapsedMs, long AllocatedBytes, long FileReads, long BytesRead, int Requests);
internal sealed record Scenario(string Name, Sample FirstBeforeWarmup, IReadOnlyList<Sample> Samples, double P50Ms, double P95Ms,
    long P50AllocatedBytes, long P50FileReads, long P50BytesRead, int P50Requests);

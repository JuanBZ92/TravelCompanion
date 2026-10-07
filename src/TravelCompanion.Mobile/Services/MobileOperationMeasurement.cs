#if ANDROID
using System.Diagnostics;
namespace TravelCompanion.Mobile.Services;

// Review diagnostics only: names are constants, counters cover the whole process interval.
// Never record identifiers, query text, payloads or paths. Normal builds do no measurement work.
internal readonly struct MobileOperationMeasurement(string? name, long started, long allocated, long requests, long reads, long bytes) : IDisposable
{
    private static long requestCount, readCount, readBytes;
    public static MobileOperationMeasurement Start(string operation)
    {
        if (!MobileDiagnosticsSettings.IsEnabled) return default;
        // Precise collection avoids a cached counter hiding allocations in short intervals.
        // Gather it before starting the duration measurement; review builds only.
        var allocated = GC.GetTotalAllocatedBytes(true);
        return new(operation, Stopwatch.GetTimestamp(), allocated, Interlocked.Read(ref requestCount), Interlocked.Read(ref readCount), Interlocked.Read(ref readBytes));
    }
    public static void Request() { if (MobileDiagnosticsSettings.IsEnabled) Interlocked.Increment(ref requestCount); }
    public static void Read(long bytes)
    {
        if (!MobileDiagnosticsSettings.IsEnabled) return;
        Interlocked.Increment(ref readCount); Interlocked.Add(ref readBytes, bytes);
    }
    public void Dispose()
    {
        if (name is null) return;
        var elapsed = Stopwatch.GetTimestamp() - started;
        var endedAllocated = GC.GetTotalAllocatedBytes(true);
        // These encrypted reads/writes allocate buffers. An unchanged runtime counter
        // cannot substantiate zero allocations; preserve the missing measurement.
        long? totalAllocated = endedAllocated <= allocated ? null : endedAllocated - allocated;
        ClientDiagnostics.Record(name, new() { ElapsedTicks = elapsed, TickFrequency = Stopwatch.Frequency,
            ElapsedMs = (long)((double)elapsed / Stopwatch.Frequency * 1000), AllocatedBytes = totalAllocated,
            Requests = Interlocked.Read(ref requestCount) - requests, Reads = Interlocked.Read(ref readCount) - reads,
            BytesRead = Interlocked.Read(ref readBytes) - bytes });
    }
}
#endif
